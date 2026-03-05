# AWS Free Tier Deployment Guide

This guide deploys:
- Angular frontend to S3 static website hosting
- .NET API + Python OCR worker on one EC2 instance via Docker Compose
- PostgreSQL on RDS free tier

## 1. AWS prerequisites

1. Create RDS PostgreSQL (`db.t3.micro`, 20 GB max for free tier).
2. Create/confirm EC2 (`t3.micro`) in same region as S3/SQS.
3. Security groups:
- EC2 SG: allow inbound `22` from your IP and `80` from your test IP range.
- RDS SG: allow inbound `5432` from EC2 SG only.

## 2. Prepare EC2 host

Run on EC2 (Amazon Linux 2023):

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

## 3. Deploy API + worker

```bash
git clone <your-repo-url>
cd AiHealthPlatform
cp .env.aws.example .env.aws
# edit .env.aws with real values
nano .env.aws

# set your frontend origin in .env.aws
# CORS_ALLOWED_ORIGINS=http://<s3-website-endpoint>

docker compose -f docker-compose.aws.yml up -d --build
docker compose -f docker-compose.aws.yml ps
```

Check logs:

```bash
docker compose -f docker-compose.aws.yml logs -f api-gateway
docker compose -f docker-compose.aws.yml logs -f api
docker compose -f docker-compose.aws.yml logs -f ocr-worker
```

## 4. Deploy frontend to S3

From your local machine:

1. Set API host in `frontend/src/environments/environment.production.ts`.
2. Build frontend:

```bash
cd frontend
npm ci
npm run build
```

3. Upload build output to S3 website bucket:

```bash
aws s3 sync dist/frontend/browser s3://<frontend-bucket-name> --delete
```

4. Enable static website hosting on that bucket and open the website endpoint URL.

## 5. Validate end-to-end

1. API health smoke test:

```bash
curl http://<ec2-public-host>/swagger/index.html
curl http://<ec2-public-host>/health
```

2. Open S3 website URL and test:
- login/register
- upload flow
- S3 object creation
- SQS message enqueue
- OCR worker consumption

## 6. Recommended next hardening steps

1. Attach EC2 IAM role and stop using long-lived AWS access keys.
2. Move API to `80/443` behind reverse proxy or ALB.
3. Add cost alarms in AWS Billing.
4. Restrict `CORS_ALLOWED_ORIGINS` to your real frontend URL only.
