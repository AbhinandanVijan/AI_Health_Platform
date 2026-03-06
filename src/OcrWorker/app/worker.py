from __future__ import annotations

import json
import logging
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from functools import lru_cache
from pathlib import Path
from urllib.parse import quote_plus
from urllib.parse import urlparse

import boto3
from botocore.exceptions import ClientError
from sqlalchemy import create_engine, text
from sqlalchemy.engine import Engine

from app.config import Settings, load_settings
from app.normalization import biomarker_name_to_code
from app.ocr_parser import parse_and_normalize


logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(message)s",
)
logger = logging.getLogger("ocr-worker")


JOB_STATUS_READY = 1
JOB_STATUS_PROCESSING = 2
JOB_STATUS_SUCCEEDED = 3
JOB_STATUS_FAILED = 4
JOB_STATUS_INSUFFICIENT_DATA = 5

BIOMARKER_SOURCE_OCR = 1

DOCUMENT_STATUS_UPLOADED = 1
DOCUMENT_STATUS_PROCESSING = 2
DOCUMENT_STATUS_PROCESSED = 3
DOCUMENT_STATUS_FAILED = 4


@dataclass
class QueueEnvelope:
    message_id: str
    receipt_handle: str
    sent_timestamp_ms: int
    body: dict


@lru_cache(maxsize=1)
def _load_mandatory_policy() -> dict:
    policy_path = Path(__file__).resolve().parent / "data" / "mandatory_biomarkers.json"
    if not policy_path.exists():
        raise FileNotFoundError(f"Missing mandatory biomarker policy file: {policy_path}")

    with policy_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    if not isinstance(payload, dict):
        raise ValueError("Invalid mandatory biomarker policy format: root must be an object")

    required_names = payload.get("mandatoryBiomarkers")
    if not isinstance(required_names, list) or not required_names:
        raise ValueError("Invalid mandatory biomarker policy: mandatoryBiomarkers must be a non-empty array")

    min_count = payload.get("minimumRequiredCanonicalBiomarkerCount", 0)
    if not isinstance(min_count, int) or min_count < 0:
        raise ValueError("Invalid mandatory biomarker policy: minimumRequiredCanonicalBiomarkerCount must be a non-negative integer")

    required = []
    for item in required_names:
        if not isinstance(item, str) or not item.strip():
            raise ValueError("Invalid mandatory biomarker policy: each mandatory biomarker must be a non-empty string")
        required.append({"name": item, "code": biomarker_name_to_code(item)})

    return {
        "minimum": min_count,
        "required": required,
    }


def _build_insufficient_data_error(extracted_codes: set[str]) -> str | None:
    policy = _load_mandatory_policy()
    missing = [entry["name"] for entry in policy["required"] if entry["code"] not in extracted_codes]
    present = [entry["name"] for entry in policy["required"] if entry["code"] in extracted_codes]
    min_required = policy["minimum"]

    if not missing and len(extracted_codes) >= min_required:
        return None

    if missing:
        message = "Uploaded report is missing mandatory blood biomarkers."
    else:
        message = (
            f"Lab report contains {len(extracted_codes)} biomarker(s), "
            f"below the minimum of {min_required} required for a complete analysis."
        )

    payload = {
        "code": "LAB_REPORT_VALIDATION_INSUFFICIENT_DATA",
        "message": message,
        "missingMandatoryBiomarkers": missing,
        "presentMandatoryBiomarkers": present,
        "extractedCanonicalBiomarkerCount": len(extracted_codes),
        "minimumRequiredCanonicalBiomarkerCount": min_required,
    }
    return json.dumps(payload, separators=(",", ":"))


def _infer_region_from_queue_url(queue_url: str) -> str | None:
    parsed = urlparse(queue_url)
    host = parsed.netloc.lower()
    if not host.startswith("sqs."):
        return None

    parts = host.split(".")
    if len(parts) < 4:
        return None

    return parts[1]


def _create_db_engine(settings: Settings) -> Engine:
    conn = settings.db_conn
    if conn.startswith("Host="):
        parts = dict(item.split("=", 1) for item in conn.split(";") if "=" in item)
        host = parts.get("Host", "localhost")
        port = parts.get("Port", "5432")
        db = parts.get("Database")
        user = parts.get("Username")
        password = parts.get("Password")
        if not all([db, user, password]):
            raise ValueError("Invalid CONNSTR_* value")
        safe_user = quote_plus(user)
        safe_password = quote_plus(password)
        conn = f"postgresql+psycopg2://{safe_user}:{safe_password}@{host}:{port}/{db}"

    return create_engine(conn, pool_pre_ping=True, future=True)


def _receive_latest_message(sqs, queue_url: str, wait_seconds: int) -> QueueEnvelope | None:
    response = sqs.receive_message(
        QueueUrl=queue_url,
        MaxNumberOfMessages=10,
        WaitTimeSeconds=wait_seconds,
        VisibilityTimeout=90,
        AttributeNames=["SentTimestamp"],
    )
    messages = response.get("Messages", [])
    if not messages:
        return None

    envelopes: list[QueueEnvelope] = []
    for msg in messages:
        sent = int(msg.get("Attributes", {}).get("SentTimestamp", "0"))
        try:
            body = json.loads(msg["Body"])
        except json.JSONDecodeError:
            body = {}

        envelopes.append(
            QueueEnvelope(
                message_id=msg["MessageId"],
                receipt_handle=msg["ReceiptHandle"],
                sent_timestamp_ms=sent,
                body=body,
            )
        )

    envelopes.sort(key=lambda x: x.sent_timestamp_ms, reverse=True)
    latest = envelopes[0]

    for extra in envelopes[1:]:
        sqs.change_message_visibility(
            QueueUrl=queue_url,
            ReceiptHandle=extra.receipt_handle,
            VisibilityTimeout=0,
        )

    return latest


def _resolve_queue_url(sqs, configured_queue_url: str) -> str:
    try:
        sqs.get_queue_attributes(
            QueueUrl=configured_queue_url,
            AttributeNames=["QueueArn"],
        )
        return configured_queue_url
    except ClientError as exc:
        code = exc.response.get("Error", {}).get("Code")
        if code == "AccessDenied":
            logger.warning(
                "No permission for sqs:GetQueueAttributes; using configured SQS_QUEUE_URL directly"
            )
            return configured_queue_url
        if code != "AWS.SimpleQueueService.NonExistentQueue":
            raise

    parsed = urlparse(configured_queue_url)
    path_parts = [part for part in parsed.path.split("/") if part]
    if len(path_parts) < 2:
        raise ValueError(f"Invalid SQS_QUEUE_URL: {configured_queue_url}")

    account_id = path_parts[0]
    queue_name = path_parts[1]

    response = sqs.get_queue_url(
        QueueName=queue_name,
        QueueOwnerAWSAccountId=account_id,
    )
    return response["QueueUrl"]


def _load_document_metadata(engine: Engine, doc_id: str) -> dict | None:
    query = text(
        """
        SELECT "Id", "UserId", "Bucket", "ObjectKey", "OriginalFileName", "ContentType"
        FROM "RawDocuments"
        WHERE "Id" = :doc_id
        """
    )
    with engine.begin() as conn:
        row = conn.execute(query, {"doc_id": doc_id}).mappings().first()
        return dict(row) if row else None


def _update_job_state(engine: Engine, job_id: str, status: int, error: str | None = None) -> None:
    now = datetime.now(timezone.utc)
    with engine.begin() as conn:
        if status == 2:
            conn.execute(
                text(
                    """
                    UPDATE "ProcessingJobs"
                    SET "Status" = :status,
                        "StartedAtUtc" = :now,
                        "AttemptCount" = "AttemptCount" + 1,
                        "Error" = NULL
                    WHERE "Id" = :job_id
                    """
                ),
                {"status": status, "now": now, "job_id": job_id},
            )
            return

        conn.execute(
            text(
                """
                UPDATE "ProcessingJobs"
                SET "Status" = :status,
                    "CompletedAtUtc" = :now,
                    "Error" = :error
                WHERE "Id" = :job_id
                """
            ),
            {"status": status, "now": now, "error": error, "job_id": job_id},
        )


def _update_document_state(engine: Engine, doc_id: str, status: int) -> None:
    now = datetime.now(timezone.utc)
    with engine.begin() as conn:
        if status == 3:
            conn.execute(
                text(
                    """
                    UPDATE "RawDocuments"
                    SET "Status" = :status,
                        "ProcessedAtUtc" = :now
                    WHERE "Id" = :doc_id
                    """
                ),
                {"status": status, "now": now, "doc_id": doc_id},
            )
            return

        conn.execute(
            text(
                """
                UPDATE "RawDocuments"
                SET "Status" = :status
                WHERE "Id" = :doc_id
                """
            ),
            {"status": status, "doc_id": doc_id},
        )


def _insert_readings(engine: Engine, doc_id: str, user_id: str, rows) -> int:
    observed_at = datetime.now(timezone.utc)
    created_at = datetime.now(timezone.utc)

    insert_query = text(
        """
        INSERT INTO "BiomarkerReadings"
            ("Id", "UserId", "BiomarkerCode", "SourceName", "Value", "Unit",
             "NormalizedValue", "NormalizedUnit", "ObservedAtUtc", "DocumentId",
             "SourceType", "EnteredByUserId", "CreatedAtUtc", "UpdatedAtUtc")
        VALUES
            (:id, :user_id, :code, :source_name, :value, :unit,
             :normalized_value, :normalized_unit, :observed_at, :document_id,
             :source_type, :entered_by_user_id, :created_at, :updated_at)
        """
    )

    existing_query = text(
        """
        SELECT "Id" FROM "BiomarkerReadings"
        WHERE "UserId" = :user_id
          AND "DocumentId" = :document_id
          AND "BiomarkerCode" = :code
          AND "SourceType" = :source_type
        ORDER BY "CreatedAtUtc" DESC
        LIMIT 1
        """
    )

    update_query = text(
        """
        UPDATE "BiomarkerReadings"
        SET "SourceName" = :source_name,
            "Value" = :value,
            "Unit" = :unit,
            "NormalizedValue" = :normalized_value,
            "NormalizedUnit" = :normalized_unit,
            "ObservedAtUtc" = :observed_at,
            "UpdatedAtUtc" = :updated_at,
            "EnteredByUserId" = :entered_by_user_id
        WHERE "Id" = :id
        """
    )

    inserted = 0
    with engine.begin() as conn:
        for row in rows:
            existing_id = conn.execute(
                existing_query,
                {
                    "user_id": user_id,
                    "code": row.biomarker_code,
                    "document_id": doc_id,
                    "source_type": BIOMARKER_SOURCE_OCR,
                },
            ).scalar_one_or_none()

            if existing_id:
                conn.execute(
                    update_query,
                    {
                        "id": existing_id,
                        "source_name": row.source_name,
                        "value": row.value,
                        "unit": row.unit,
                        "normalized_value": row.normalized_value,
                        "normalized_unit": row.normalized_unit,
                        "observed_at": observed_at,
                        "updated_at": created_at,
                        "entered_by_user_id": user_id,
                    },
                )
                continue

            conn.execute(
                insert_query,
                {
                    "id": str(uuid.uuid4()),
                    "user_id": user_id,
                    "code": row.biomarker_code,
                    "source_name": row.source_name,
                    "value": row.value,
                    "unit": row.unit,
                    "normalized_value": row.normalized_value,
                    "normalized_unit": row.normalized_unit,
                    "observed_at": observed_at,
                    "document_id": doc_id,
                    "source_type": BIOMARKER_SOURCE_OCR,
                    "entered_by_user_id": user_id,
                    "created_at": created_at,
                    "updated_at": created_at,
                },
            )
            inserted += 1

    return inserted


def _load_document_codes(engine: Engine, doc_id: str, user_id: str) -> set[str]:
    query = text(
        """
        SELECT DISTINCT "BiomarkerCode"
        FROM "BiomarkerReadings"
        WHERE "DocumentId" = :doc_id
          AND "UserId" = :user_id
        """
    )

    with engine.begin() as conn:
        rows = conn.execute(query, {"doc_id": doc_id, "user_id": user_id}).scalars().all()

    return {row for row in rows if row}


def _process_message(settings: Settings, engine: Engine, sqs, s3, queue_url: str, envelope: QueueEnvelope) -> None:
    body = envelope.body
    job_id = body.get("jobId")
    doc_id = body.get("docId")
    user_id = body.get("userId")

    if not job_id or not doc_id or not user_id:
        logger.error("Invalid message payload: %s", body)
        sqs.delete_message(QueueUrl=queue_url, ReceiptHandle=envelope.receipt_handle)
        return

    _update_job_state(engine, job_id=job_id, status=JOB_STATUS_PROCESSING)
    _update_document_state(engine, doc_id=doc_id, status=DOCUMENT_STATUS_PROCESSING)

    try:
        doc = _load_document_metadata(engine, doc_id)
        if not doc:
            raise ValueError(f"RawDocument not found: {doc_id}")

        s3_object = s3.get_object(Bucket=doc["Bucket"], Key=doc["ObjectKey"])
        content = s3_object["Body"].read()

        readings = parse_and_normalize(
            content=content,
            content_type=doc.get("ContentType"),
            file_name=doc.get("OriginalFileName"),
        )

        inserted = _insert_readings(engine, doc_id=doc_id, user_id=user_id, rows=readings)
        extracted_codes = _load_document_codes(engine, doc_id=doc_id, user_id=user_id)
        insufficient_data_error = _build_insufficient_data_error(extracted_codes)
        if insufficient_data_error:
            _update_job_state(
                engine,
                job_id=job_id,
                status=JOB_STATUS_INSUFFICIENT_DATA,
                error=insufficient_data_error,
            )
            _update_document_state(engine, doc_id=doc_id, status=DOCUMENT_STATUS_PROCESSED)
            sqs.delete_message(QueueUrl=queue_url, ReceiptHandle=envelope.receipt_handle)
            logger.info("Insufficient data doc=%s job=%s readings=%d", doc_id, job_id, inserted)
            return

        _update_job_state(engine, job_id=job_id, status=JOB_STATUS_SUCCEEDED)
        _update_document_state(engine, doc_id=doc_id, status=DOCUMENT_STATUS_PROCESSED)

        sqs.delete_message(QueueUrl=queue_url, ReceiptHandle=envelope.receipt_handle)
        logger.info("Processed doc=%s job=%s readings=%d", doc_id, job_id, inserted)

    except Exception as exc:  # noqa: BLE001
        _update_job_state(engine, job_id=job_id, status=JOB_STATUS_FAILED, error=str(exc))
        _update_document_state(engine, doc_id=doc_id, status=DOCUMENT_STATUS_FAILED)
        logger.exception("Failed doc=%s job=%s", doc_id, job_id)


def run() -> None:
    settings = load_settings()
    engine = _create_db_engine(settings)

    sqs_region = _infer_region_from_queue_url(settings.sqs_queue_url) or settings.aws_region
    sqs = boto3.client("sqs", region_name=sqs_region)
    s3 = boto3.client("s3", region_name=settings.aws_region)
    queue_url = settings.sqs_queue_url
    try:
        queue_url = _resolve_queue_url(sqs, queue_url)
    except ClientError as exc:
        code = exc.response.get("Error", {}).get("Code")
        message = exc.response.get("Error", {}).get("Message", str(exc))
        logger.error("SQS queue validation failed (%s): %s", code, message)
        if settings.run_once:
            return

    logger.info("OCR worker started; queue=%s sqs_region=%s s3_region=%s", queue_url, sqs_region, settings.aws_region)

    while True:
        try:
            envelope = _receive_latest_message(
                sqs=sqs,
                queue_url=queue_url,
                wait_seconds=settings.poll_wait_seconds,
            )
        except ClientError as exc:
            code = exc.response.get("Error", {}).get("Code")
            message = exc.response.get("Error", {}).get("Message", str(exc))
            logger.error("SQS access failed (%s): %s", code, message)
            if settings.run_once:
                return
            time.sleep(max(settings.loop_sleep_seconds, 10))
            continue

        if envelope is None:
            if settings.run_once:
                logger.info("No messages found; exiting once mode")
                return
            time.sleep(settings.loop_sleep_seconds)
            continue

        _process_message(settings, engine, sqs, s3, queue_url, envelope)

        if settings.run_once:
            logger.info("Processed one message; exiting once mode")
            return
