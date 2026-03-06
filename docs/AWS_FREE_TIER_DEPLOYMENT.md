# AWS Free Tier Deployment Guide

Last updated: 2026-03-05 (validated against live EC2 deployment)

This runbook deploys:

- Angular frontend to S3 static website hosting.
- .NET API + Python OCR worker on one EC2 instance via Docker Compose.
- Nginx API gateway on EC2 (`:80` -> `api:8080`).
- PostgreSQL on RDS free tier.

Related architecture reference:

- `docs/PROJECT_DOCUMENTATION.md`

---

## 1) Prerequisites

1. Region alignment:
- Keep EC2, RDS, S3, and SQS in the same AWS region.

2. EC2 (free tier profile):
- Amazon Linux 2023, `t3.micro`.
- Security group with inbound:
  - `22` from your IP only.
  - `80` from your IP during testing (open wider only when ready).

3. RDS PostgreSQL:
- `db.t3.micro`.
- SG inbound `5432` from EC2 SG (SG-to-SG rule).
- Same VPC as EC2.

4. AWS resources:
- S3 upload bucket exists.
- SQS queue exists and URL is known.
- Frontend bucket exists (for website hosting).

---

## 2) Prepare EC2 host

Run on EC2:

```bash
sudo dnf update -y
sudo dnf install -y docker git
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user
newgrp docker
DOCKER_CONFIG=${DOCKER_CONFIG:-$HOME/.docker}
mkdir -p "$DOCKER_CONFIG/cli-plugins"
curl -SL https://github.com/docker/compose/releases/download/v2.29.7/docker-compose-linux-x86_64 -o "$DOCKER_CONFIG/cli-plugins/docker-compose"
chmod +x "$DOCKER_CONFIG/cli-plugins/docker-compose"
docker compose version
```

---

## 3) Deploy backend stack (API + gateway + worker)

```bash
git clone <your-repo-url>
cd AI_Health_Platform
cp .env.aws.example .env.aws
nano .env.aws
```

Populate `.env.aws` with real values.

Critical keys:

- `CONNSTR_RDS=Host=<rds-endpoint>;Port=5432;Database=<db>;Username=<user>;Password=<password>`
- `JWT_KEY=<strong-secret>`
- `JWT_ISSUER=aihealth.aws`
- `JWT_AUDIENCE=aihealth.aws`
- `AWS_REGION=<region>`
- `S3_BUCKET=<upload-bucket>`
- `SQS_QUEUE_URL=<full-queue-url>`
- `CORS_ALLOWED_ORIGINS=http://<frontend-s3-website-endpoint>`

Start stack (always pass env file):

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d --build
docker compose --env-file .env.aws -f docker-compose.aws.yml ps
```

Logs:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml logs -f api-gateway
docker compose --env-file .env.aws -f docker-compose.aws.yml logs -f api
docker compose --env-file .env.aws -f docker-compose.aws.yml logs -f ocr-worker
```

Health checks:

```bash
curl http://localhost/health
curl http://<ec2-public-host>/health
curl http://<ec2-public-host>/swagger/index.html
```

---

## 4) Deploy frontend to S3 website bucket

1. Set API base URL in `frontend/src/environments/environment.production.ts`.

Example:

```ts
apiBaseUrl: 'http://ec2-3-150-117-161.us-east-2.compute.amazonaws.com'
```

2. Build frontend (local machine):

```bash
cd frontend
npm ci
npm run build
```

3. Publish to S3 (either from local with AWS CLI or via EC2 copy + sync):

```bash
aws s3 sync dist/frontend/browser s3://<frontend-bucket-name> --delete
```

4. Open website endpoint:

```text
http://<frontend-bucket-name>.s3-website.<region>.amazonaws.com
```

---

## 5) Runtime topology reference

`docker-compose.aws.yml` services:

- `api-gateway` (`nginx:1.27-alpine`) on host port `80`.
- `api` (.NET 8) internal `8080`.
- `ocr-worker` (Python) polling SQS and writing to RDS.

Nginx config source:

- `deploy/nginx/api-gateway.conf`

---

## 6) Troubleshooting Playbook

### 6.1 Browser CORS failure on register/login/upload

Symptom:

- `No 'Access-Control-Allow-Origin' header ...`

Checks:

```bash
curl -i -X OPTIONS 'http://<api-host>/api/auth/register' \
  -H 'Origin: http://<frontend-endpoint>' \
  -H 'Access-Control-Request-Method: POST' \
  -H 'Access-Control-Request-Headers: content-type,authorization'
```

Fix:

- Set exact `CORS_ALLOWED_ORIGINS` in `.env.aws` (no extra spaces, no concatenated host strings).
- Recreate API and gateway:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d --force-recreate api api-gateway
```

### 6.2 `502 Bad Gateway` from API gateway

Symptom:

- Nginx returns `502`.

Root cause pattern:

- API container crashed or cannot bind/start.

Checks:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml ps
docker compose --env-file .env.aws -f docker-compose.aws.yml logs --tail=200 api
docker compose --env-file .env.aws -f docker-compose.aws.yml logs --tail=120 api-gateway
```

### 6.3 API startup fails with DB timeout

Symptom:

- `NpgsqlException: Failed to connect to <private-ip>:5432`

Fixes:

- Ensure RDS SG allows `5432` from EC2 SG.
- Ensure EC2 and RDS are in same VPC.
- Ensure `CONNSTR_RDS` is valid and single-line.
- Restart stack.

### 6.4 Compose warns about missing env variables

Symptom:

- `The "JWT_KEY" variable is not set. Defaulting to a blank string.`

Fix:

- Always run compose with `--env-file .env.aws`.

---

## 7) Security and Cost Notes

1. Keep `22` restricted to your IP at all times.
2. Keep `80` restricted during test; open deliberately when go-live is needed.
3. Prefer EC2 instance role over static AWS keys.
4. Add budget alarms (for example `$5`, `$10`).
5. Public IP endpoints are scanned by bots; monitor logs and harden incrementally.

---

## 8) Suggested next phase (optional)

For HTTPS and cleaner public experience:

1. Add CloudFront in front of frontend bucket.
2. Add HTTPS edge for API (CloudFront or ALB + ACM).
3. Switch frontend `apiBaseUrl` to HTTPS API domain.
