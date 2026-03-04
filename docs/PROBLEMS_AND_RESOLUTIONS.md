# Problems Faced and Resolutions

> This file is a running incident and fix log for the project.  
> Update this file **after every meaningful fix/change**.

## Entry Template

- **Date/Time (UTC):**
- **Area:** (API / Worker / DB / AWS-S3 / AWS-SQS / Auth / DevOps)
- **Symptom:**
- **Root Cause:**
- **Resolution:**
- **Validation:**
- **Files Changed:**

---

## 2026-03-03 - OCR worker crashed with QueueDoesNotExist
- **Area:** AWS-SQS / Worker
- **Symptom:** Worker failed at startup with `AWS.SimpleQueueService.NonExistentQueue`.
- **Root Cause:** Queue region/URL mismatch and hard failure path in worker.
- **Resolution:** Updated queue URL to correct region and added SQS queue resolution/error handling retry logic in worker.
- **Validation:** Worker stayed alive and retried with explicit logs instead of immediate crash.
- **Files Changed:**
  - `src/OcrWorker/app/worker.py`
  - `.env`

## 2026-03-03 - API could not send SQS messages
- **Area:** AWS-SQS / API
- **Symptom:** `finalize` created DB rows but queue had no new messages.
- **Root Cause:** IAM user lacked `sqs:SendMessage` permission.
- **Resolution:** Added identity-based IAM policy for send/get queue actions on `ai-health-queue`.
- **Validation:** SQS messages appeared and were consumed by worker.
- **Files Changed:**
  - IAM policy (AWS Console change)

## 2026-03-03 - S3 upload failed with PermanentRedirect
- **Area:** AWS-S3 / API
- **Symptom:** S3 presigned upload returned `PermanentRedirect` with required endpoint `s3.us-east-2.amazonaws.com`.
- **Root Cause:** `AWS_REGION` was set to `us-east-1` while bucket was in `us-east-2`.
- **Resolution:** Set `AWS_REGION=us-east-2`.
- **Validation:** Presigned uploads succeeded to correct endpoint.
- **Files Changed:**
  - `.env`

## 2026-03-03 - S3 upload failed with AccessDenied (PutObject)
- **Area:** AWS-S3
- **Symptom:** `s3:PutObject` denied for `appuser` during upload.
- **Root Cause:** Missing identity-based S3 permissions.
- **Resolution:** Added IAM permissions for `s3:PutObject` / `s3:GetObject` (and bucket list as needed).
- **Validation:** Upload to presigned URL worked.
- **Files Changed:**
  - IAM policy (AWS Console change)

## 2026-03-03 - Worker failed DB connection due to special chars in password
- **Area:** Worker / DB
- **Symptom:** Worker error `connection to server on socket "@db/.s.PGSQL.5432" failed`.
- **Root Cause:** Password contained `@` and connection string conversion to SQLAlchemy URL did not URL-encode credentials.
- **Resolution:** URL-encoded DB username/password when building SQLAlchemy URL.
- **Validation:** Worker processed messages successfully and updated statuses.
- **Files Changed:**
  - `src/OcrWorker/app/worker.py`

## 2026-03-03 - OCR processed document with `readings=0`
- **Area:** Worker / OCR Parser
- **Symptom:** Job completed but no biomarker rows were inserted.
- **Root Cause:** Parser only matched single-line patterns; lab report had table layout with values/units on separate lines.
- **Resolution:** Added multi-line table parsing logic and improved unit normalization for unicode/superscript symbols.
- **Validation:** Same sample document produced 10 readings in parser test.
- **Files Changed:**
  - `src/OcrWorker/app/ocr_parser.py`
  - `src/OcrWorker/app/normalization.py`

## 2026-03-03 - Need to reprocess without re-uploading file
- **Area:** API / Workflow
- **Symptom:** Repeated finalize on same object key returned `alreadyExists=true`, no new queue message.
- **Root Cause:** Idempotency logic intentionally blocks duplicate object-key finalization.
- **Resolution:** Added `POST /api/uploads/reprocess/{docId}` to enqueue a fresh job for an existing document.
- **Validation:** Endpoint creates new `ProcessingJob` and publishes SQS message for same document.
- **Files Changed:**
  - `src/Api/Controllers/UploadControlller.cs`

## 2026-03-03 - Prevent accidental credential commits
- **Area:** DevOps / Git
- **Symptom:** Risk of pushing local secrets/credentials to git.
- **Root Cause:** Base ignore rules covered `.env` only and missed several common secret-bearing files.
- **Resolution:** Hardened `.gitignore` to ignore `.env.*` (except `.env.example`), local secret files, key/cert formats, local AWS config folder, and local appsettings variants.
- **Validation:** Sensitive local files remain untracked in `git status` by default.
- **Files Changed:**
  - `.gitignore`
  - `docs/PROBLEMS_AND_RESOLUTIONS.md`
