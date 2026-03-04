from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    aws_region: str
    sqs_queue_url: str
    s3_bucket: str
    db_conn: str
    poll_wait_seconds: int = 20
    loop_sleep_seconds: int = 2
    run_once: bool = False


def load_settings() -> Settings:
    db_conn = os.getenv("CONNSTR_DOCKER") or os.getenv("CONNSTR_HOST")
    if not db_conn:
        raise ValueError("Missing DB connection string: CONNSTR_DOCKER or CONNSTR_HOST")

    queue_url = os.getenv("SQS_QUEUE_URL")
    if not queue_url:
        raise ValueError("Missing SQS_QUEUE_URL")

    bucket = os.getenv("S3_BUCKET")
    if not bucket:
        raise ValueError("Missing S3_BUCKET")

    region = os.getenv("AWS_REGION", "us-east-1")

    return Settings(
        aws_region=region,
        sqs_queue_url=queue_url,
        s3_bucket=bucket,
        db_conn=db_conn,
        poll_wait_seconds=int(os.getenv("OCR_POLL_WAIT_SECONDS", "20")),
        loop_sleep_seconds=int(os.getenv("OCR_LOOP_SLEEP_SECONDS", "2")),
        run_once=os.getenv("OCR_RUN_ONCE", "false").lower() == "true",
    )
