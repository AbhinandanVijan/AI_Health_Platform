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

## 2026-03-04 - OCR parser missed category-structured lab report rows
- **Area:** Worker / OCR Parser
- **Symptom:** Worker completed with `readings=0` for `labreport1_user.pdf` despite valid tabular lab values.
- **Root Cause:** Report layout used `Category -> Test -> Value -> Unit -> Reference Range` ordering; parser only supported prior single-line and different multiline sequence patterns.
- **Resolution:** Extended parser to handle category-based multiline patterns, added additional non-measurement labels, and expanded unit token handling for superscript characters.
- **Validation:** Parser retest on the same S3 document now returns non-zero readings.
- **Files Changed:**
  - `src/OcrWorker/app/ocr_parser.py`
  - `src/OcrWorker/app/normalization.py`
  - `docs/PROBLEMS_AND_RESOLUTIONS.md`

## 2026-03-04 - Need Swagger-friendly reprocess request shape
- **Area:** API / Swagger / DX
- **Symptom:** Reprocess flow was available via path parameter endpoint only, making request shape less obvious in Swagger for users expecting JSON body requests.
- **Root Cause:** Existing endpoint exposed `reprocess/{docId}` route but no body-based variant with explicit media-type metadata.
- **Resolution:** Added `POST /api/uploads/reprocess` accepting `{ documentId }` JSON body and added explicit `Consumes/Produces/ProducesResponseType` metadata for upload endpoints.
- **Validation:** New endpoint appears in Swagger with request schema and can requeue by document id from request body.
- **Files Changed:**
  - `src/Api/Controllers/UploadControlller.cs`
  - `docs/PROJECT_DOCUMENTATION.md`
  - `docs/PROBLEMS_AND_RESOLUTIONS.md`

## 2026-03-04 - Mandatory WBC missed for pipe-delimited OCR row
- **Area:** Worker / OCR Parser / Validation
- **Symptom:** `GET /api/uploads/status/{docId}` returned `InsufficientData` with `missingMandatoryBiomarkers=["WhiteBloodCells"]` even though report text contained WBC.
- **Root Cause:** OCR output row format was pipe-delimited (`| Hematology WBC | 9.1 x109/uL | 4.0-11.0`) and parser did not normalize `|` separators before rule-based extraction.
- **Resolution:** Normalized pipe delimiters to spaces in parser line cleanup path so table rows are parsed by existing measurement rules.
- **Validation:** Reparse of failing document produced `WHITEBLOODCELLS` with value `9.1 x109/uL`; status moved from `InsufficientData` to `Succeeded` after reprocess.
- **Files Changed:**
  - `src/OcrWorker/app/ocr_parser.py`
  - `docs/PROBLEMS_AND_RESOLUTIONS.md`

## 2026-03-05 - UI register failed despite seemingly valid password
- **Area:** API / Auth / Frontend
- **Symptom:** Registration failed from UI while similar requests worked from API client tooling.
- **Root Cause:** ASP.NET Identity default password requirements were stricter than frontend assumptions.
- **Resolution:** Made password policy explicit in API startup config and surfaced backend validation messages in UI register flow.
- **Validation:** Registration succeeds with documented policy (`length >= 8` and `digit required`) and UI displays server-side validation details when invalid.
- **Files Changed:**
  - `src/Api/Program.cs`
  - `frontend/src/app/features/auth/login.component.ts`

## 2026-03-05 - Frontend API calls returned 404 during local dev
- **Area:** Frontend / Dev Proxy
- **Symptom:** Login from UI hit `/api/auth/login` and returned 404 while backend endpoint was healthy.
- **Root Cause:** Angular dev server was not consistently running with proxy configuration.
- **Resolution:** Set default `proxyConfig` in Angular serve options.
- **Validation:** UI requests route to API host through proxy with no 404 due to dev-server misrouting.
- **Files Changed:**
  - `frontend/angular.json`
  - `frontend/proxy.conf.json`

## 2026-03-05 - Browser upload blocked by S3 CORS preflight
- **Area:** Upload Flow / Frontend / API
- **Symptom:** Browser upload hit CORS `403` preflight errors on direct pre-signed S3 PUT flow.
- **Root Cause:** Browser-to-S3 CORS dependency introduced environment-sensitive preflight failures.
- **Resolution:** Added authenticated API multipart upload endpoint (`POST /api/uploads/direct`) and switched UI upload path to same-origin API call.
- **Validation:** Upload and finalize complete through API without browser CORS dependency on S3 bucket policy.
- **Files Changed:**
  - `src/Api/Controllers/UploadControlller.cs`
  - `frontend/src/app/core/services/api.service.ts`
  - `frontend/src/app/core/models/api.models.ts`
  - `frontend/src/app/features/uploads/uploads.component.ts`

## 2026-03-05 - Status values shown as numeric enums in UI
- **Area:** Frontend / UX
- **Symptom:** Upload and history pages displayed numeric status/type codes, reducing readability.
- **Root Cause:** UI rendered raw enum integers from API DTOs.
- **Resolution:** Added enum-to-label mappings for document status, job status, job type, and source type.
- **Validation:** Upload/history screens now show readable labels (`Succeeded`, `Processing`, `OcrLabPdf`, etc.).
- **Files Changed:**
  - `frontend/src/app/features/uploads/uploads.component.ts`
  - `frontend/src/app/features/history/history.component.ts`

## 2026-03-05 - OCR parsing produced noisy/non-biomarker captures
- **Area:** Worker / OCR Parser / Normalization
- **Symptom:** OCR output included noisy tokens (address/date/header fragments) and missed/corrupted biomarker names due to scan artifacts.
- **Root Cause:** Parser accepted broader text shapes and canonicalization lacked OCR typo tolerance for common report artifacts.
- **Resolution:** Strengthened row extraction patterns, added OCR typo corrections + fuzzy canonicalization fallback, expanded biomarker aliases, and filtered to known biomarker codes.
- **Validation:** Parser/compiler checks passed; runtime worker checks confirmed expected canonical mappings and presence of critical markers such as `WHITEBLOODCELLS` on previously problematic documents.
- **Files Changed:**
  - `src/OcrWorker/app/ocr_parser.py`
  - `src/OcrWorker/app/normalization.py`
  - `src/OcrWorker/app/data/biomarker.json`

## 2026-03-05 - Key Learnings from latest stabilization cycle
- Keep API-side auth/password rules explicitly configured; do not rely on framework defaults when frontend policy messaging is custom.
- Treat dev proxy configuration as part of baseline frontend bootstrapping to avoid false API routing failures.
- Prefer same-origin upload APIs for early-stage product stability; direct browser-to-S3 uploads require strict CORS governance.
- Always translate backend enum codes into user-facing labels in UI.
- Improve OCR accuracy through layered defenses: structural parsing, domain lexicon constraints, typo/fuzzy normalization, and explicit noise filtering.
- Validate OCR changes with both static checks (JSON/syntax compile) and document-level runtime reparse on real uploaded artifacts.

## 2026-03-05 - AWS compose env values not loaded in deployment commands
- **Area:** DevOps / AWS / Docker
- **Symptom:** `docker compose` emitted repeated warnings that critical variables (`JWT_KEY`, `CONNSTR_RDS`, `AWS_REGION`, etc.) were not set.
- **Root Cause:** Compose variable interpolation relied on default `.env`; deployment file used `.env.aws` but commands did not pass it explicitly.
- **Resolution:** Standardized deployment commands to always include `--env-file .env.aws`.
- **Validation:** Compose config and container startup no longer showed missing-variable warnings.
- **Files Changed:**
  - `docs/AWS_FREE_TIER_DEPLOYMENT.md`

## 2026-03-05 - API startup failure caused by RDS connectivity timeout
- **Area:** API / DB / AWS-RDS
- **Symptom:** API container restarted with `NpgsqlException: Failed to connect to <private-ip>:5432`, and gateway returned `502`.
- **Root Cause:** RDS was unreachable from EC2 at startup (security-group/VPC path issue), and API blocks startup while applying EF migrations.
- **Resolution:** Corrected RDS network access from EC2 security group and verified same-VPC connectivity; restarted stack.
- **Validation:** API health endpoint returned `200`, gateway proxied successfully, and containers stayed stable.
- **Files Changed:**
  - AWS Console security group rules
  - `docs/AWS_FREE_TIER_DEPLOYMENT.md`

## 2026-03-05 - Browser registration blocked by CORS preflight
- **Area:** API / Frontend / AWS
- **Symptom:** Browser showed `No 'Access-Control-Allow-Origin' header` for `/api/auth/register` preflight from S3 website origin.
- **Root Cause:** Malformed `CORS_ALLOWED_ORIGINS` value in `.env.aws` (extra spacing/concatenated host).
- **Resolution:** Set exact S3 website origin in `CORS_ALLOWED_ORIGINS` and force-recreated `api` + `api-gateway` containers.
- **Validation:** OPTIONS preflight returned `204` with expected `Access-Control-Allow-Origin`, `Allow-Methods`, and `Allow-Headers`; registration requests proceeded.
- **Files Changed:**
  - `.env.aws` (on EC2)
  - `docs/AWS_FREE_TIER_DEPLOYMENT.md`

---

## 2026-03-06 - Clinician UX overhaul: role-based nav, edit-before-approve, review history

- **Area:** Frontend / API / Clinician Workflow
- **Symptom:** Clinicians saw irrelevant pages (Dashboard, Uploads, History); no way to edit recommendation text before approving; no history of past approvals.
- **Root Cause:** Navigation was a single unified list with no role differentiation; approve endpoint accepted no content override; no approved-history endpoint existed.
- **Resolution:**
  - Added role-based navigation in shell: clinicians see only "Clinician Review" and "Review History"; users see Dashboard/Uploads/History.
  - Added `homeGuard` to redirect root path to `/clinician-review` (Clinician) or `/dashboard` (User).
  - Rewrote `ClinicianReviewComponent` with card-per-recommendation layout, type badges, inline edit textarea, and "Approve with Changes" flow.
  - Modified `POST /recommendations/{id}/approve` to accept optional `{ "content": "..." }` body, overriding recommendation text before publishing.
  - Added `GET /api/insights/recommendations/approved` endpoint returning all published+approved recommendations for Clinician role.
  - Created `PatientReviewHistoryComponent` at `/review-history` backed by new endpoint.
- **Validation:** Clinicians can edit recommendation text inline, approve with or without changes, then view all past approvals in Review History. Users see no clinician pages.
- **Files Changed:**
  - `src/Api/Controllers/InsightsController.cs`
  - `frontend/src/app/layout/shell.component.ts`
  - `frontend/src/app/core/guards/auth.guard.ts`
  - `frontend/src/app/app.routes.ts`
  - `frontend/src/app/features/clinician/clinician-review.component.ts`
  - `frontend/src/app/features/clinician/patient-review-history.component.ts` (new)
  - `frontend/src/app/core/services/api.service.ts`
  - `frontend/src/app/core/models/api.models.ts`

## 2026-03-06 - GitHub Actions backend deploy failing (Docker build context + health check)

- **Area:** DevOps / Docker / CI-CD
- **Symptom:** GitHub Actions `deploy-backend` job timed out or failed health check immediately after `docker compose up -d`.
- **Root Cause 1:** No `.dockerignore` — entire repo (including `frontend/dist/`, build artifacts) sent as Docker build context, causing very slow or hung builds.
- **Root Cause 2:** Health check `curl` ran immediately after `docker compose up -d` — API not ready yet (DB migrations + role seeding take several seconds).
- **Resolution:**
  - Created `.dockerignore` excluding `frontend/`, `.git/`, `docs/`, build outputs, IDE files.
  - Updated workflow health check to retry 24 × 5s (120s total) before failing; prints container logs on timeout.
- **Validation:** Build context reduced significantly; health check passes reliably after container warmup.
- **Files Changed:**
  - `.dockerignore` (new)
  - `.github/workflows/deploy-backend.yml`

## 2026-03-06 - mat-icon names rendering as raw text inside buttons

- **Area:** Frontend / Angular Material
- **Symptom:** Button labels showed "re", "ec", "ch" text fragments overlapping with button text (e.g., "Refresh", "Edit", "Check").
- **Root Cause:** Material Icons Google Font not loaded in `index.html`. Without the font, `<mat-icon>` renders the icon ligature name as plain text adjacent to the button label.
- **Resolution:**
  - Added `<link href="https://fonts.googleapis.com/icon?family=Material+Icons" rel="stylesheet">` to `frontend/src/index.html`.
  - Removed `<mat-icon>` elements from inside buttons in clinician components (text-only labels).
- **Validation:** Buttons display clean text-only labels with no raw icon name fragments.
- **Files Changed:**
  - `frontend/src/index.html`
  - `frontend/src/app/features/clinician/clinician-review.component.ts`
  - `frontend/src/app/features/clinician/patient-review-history.component.ts`

## 2026-03-06 - Docker build hung on `dotnet publish` on t2.micro (OOM)

- **Area:** DevOps / Docker / EC2
- **Symptom:** `docker compose build` hung indefinitely at `RUN dotnet publish ... -c Release` (240s+ with no progress past "Determining projects to restore...").
- **Root Cause:** EC2 `t2.micro` has 1GB RAM with 0 swap. `dotnet publish -c Release` (Roslyn compiler) requires ~1.5–2GB; OOM killer silently killed the compiler process causing an infinite hang.
- **Resolution:** Added 2GB swap file on EC2 before building:
  ```bash
  sudo fallocate -l 2G /swapfile
  sudo chmod 600 /swapfile
  sudo mkswap /swapfile
  sudo swapon /swapfile
  ```
- **Validation:** Build completed successfully in ~4 minutes after adding swap. No extra AWS cost (uses existing EBS disk space).
- **Files Changed:**
  - EC2 instance (swap added at OS level, not persisted across reboot)
