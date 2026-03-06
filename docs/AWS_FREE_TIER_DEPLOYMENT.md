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

---

## 9) CI/CD with GitHub Actions

This repository includes two workflows:

- `.github/workflows/deploy-backend.yml`
- `.github/workflows/deploy-frontend.yml`

Both workflows target GitHub environment: `production`.
They are implemented with shell steps only (no marketplace actions), which supports repositories configured to allow actions only from the current owner.

### 9.1 Backend deploy workflow

Trigger:

- `push` to `main` when backend/infra files change.
- manual `workflow_dispatch`.

Behavior:

1. Connect to EC2 over SSH from GitHub runner.
2. Pull latest `main` on instance.
3. Run:

```bash
docker compose --env-file .env.aws -f docker-compose.aws.yml up -d --build
```

4. Run health check (`curl -fsS http://localhost/health`).

Required GitHub variables:

- `EC2_HOST` (hostname or IP only, no `http://`)
- Optional `EC2_USER` (defaults to `ec2-user`)
- Optional `EC2_APP_PATH` (defaults to `/home/ec2-user/AI_Health_Platform`)

Required GitHub secret:

- `EC2_SSH_KEY` (full private key content for the deploy key pair)

### 9.2 Frontend deploy workflow

Trigger:

- `push` to `main` when `frontend/**` changes.
- manual `workflow_dispatch`.

Behavior:

1. Build Angular app.
2. Assume AWS role via OIDC.
3. Sync `frontend/dist/frontend/browser` to S3 bucket.

Required GitHub variables:

- `AWS_OIDC_ROLE_ARN`
- `AWS_REGION`
- `FRONTEND_BUCKET`

### 9.3 OIDC role notes

Use GitHub OIDC (recommended) instead of static AWS keys.

Typical S3 permissions for the frontend bucket:

- `s3:ListBucket`
- `s3:GetObject`
- `s3:PutObject`
- `s3:DeleteObject`

Scope permissions to the target frontend bucket only.

### 9.4 IAM trust policy (GitHub OIDC)

Replace placeholders before saving:

- `<ACCOUNT_ID>`
- `<GITHUB_ORG>`
- `<GITHUB_REPO>`

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:<GITHUB_ORG>/<GITHUB_REPO>:ref:refs/heads/main"
        }
      }
    }
  ]
}
```

### 9.5 IAM inline policy (frontend S3 deploy)

Replace `<FRONTEND_BUCKET>`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ListFrontendBucket",
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket"
      ],
      "Resource": "arn:aws:s3:::<FRONTEND_BUCKET>"
    },
    {
      "Sid": "ManageFrontendObjects",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::<FRONTEND_BUCKET>/*"
    }
  ]
}
```

### 9.6 AWS Console setup steps

1. IAM -> Identity providers -> Add provider
- Provider type: `OpenID Connect`
- Provider URL: `https://token.actions.githubusercontent.com`
- Audience: `sts.amazonaws.com`

2. IAM -> Roles -> Create role
- Trusted entity type: `Web identity`
- Identity provider: `token.actions.githubusercontent.com`
- Audience: `sts.amazonaws.com`
- Attach no broad policy yet.

3. In role `Trust relationships`, paste policy from section 9.4.

4. In role `Permissions`, add inline policy from section 9.5.

5. Copy role ARN and set GitHub repo variable for frontend workflow:
- `AWS_OIDC_ROLE_ARN=<role-arn>`

6. Set remaining GitHub repo variables:
- `AWS_REGION=us-east-2`
- `FRONTEND_BUCKET=aihealth-frontend-abhinandan`

7. Set GitHub repo variables/secrets for backend deploy:

- `EC2_HOST=3.150.117.161` (example)
- Optional `EC2_USER=ec2-user`
- Optional `EC2_APP_PATH=/home/ec2-user/AI_Health_Platform`
- `EC2_SSH_KEY` as repository secret (entire PEM private key)

8. Create GitHub environment:
- Repo -> Settings -> Environments -> New environment -> `production`

9. Trigger workflows manually as needed:
- Frontend: Actions -> `Deploy Frontend to S3` -> `Run workflow`
- Backend: Actions -> `Deploy Backend to EC2` -> `Run workflow`

### 9.7 GitHub Actions permissions setting

In GitHub repo settings:

1. `Settings -> Actions -> General`
2. Under `Workflow permissions`, select `Read and write permissions`
3. Save

### 9.8 Backend SSH network prerequisites

1. EC2 security group must allow inbound `22` from GitHub Actions runner IPs or from an IP range/pattern you control.
2. EC2 user home/app path must be readable and writable by the deploy user.
3. SSH private key in `EC2_SSH_KEY` must match the public key accepted by the target EC2 instance.

If backend workflow still fails with SSH timeout:

1. Verify local connectivity to the same host: `nc -zv <EC2_HOST> 22`.
2. In EC2 security group, temporarily allow `22` from `0.0.0.0/0` for a short test window.
3. Re-run `Deploy Backend to EC2`; if it succeeds, tighten `22` again and move to a stable model.
4. For long-term stability, prefer a self-hosted runner on EC2 so SSH stays internal.
