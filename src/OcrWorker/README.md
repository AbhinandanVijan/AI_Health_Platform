# OCR Worker (Python)

This service polls SQS, fetches the latest lab document from S3, runs OCR + parsing, normalizes biomarker units, and writes data into:

- `RawDocuments` (status updates)
- `ProcessingJobs` (status and errors)
- `BiomarkerReadings` (parsed and normalized rows)

## Message contract
Expected SQS payload shape (same as API finalize step):

```json
{
  "jobId": "GUID",
  "docId": "GUID",
  "userId": "string",
  "type": "OcrLabPdf"
}
```

## Environment
Uses existing root `.env` values:

- `CONNSTR_DOCKER` / `CONNSTR_HOST`
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `AWS_REGION`
- `S3_BUCKET`
- `SQS_QUEUE_URL`

Optional:

- `OCR_RUN_ONCE` (`true`/`false`)
- `OCR_POLL_WAIT_SECONDS` (default `20`)
- `OCR_LOOP_SLEEP_SECONDS` (default `2`)

## Local run

```bash
cd src/OcrWorker
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python main.py
```

## Notes

- Worker receives up to 10 messages and processes the one with the highest `SentTimestamp` as "latest".
- Non-selected messages are immediately made visible again.
- On success, message is deleted.
- On failure, message is left for retry and DB status is marked failed.
