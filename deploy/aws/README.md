# Oloraculo AWS Deployment Target

This target is container-first and read-only. It runs the Blazor cockpit/API on
AWS while keeping market-data history in R2/S3-compatible object storage.

## Container

Build from the repository root:

```bash
docker build -t oloraculo-cockpit-api:latest .
```

Runtime port:

```text
8080
```

Health and status endpoints:

```text
/healthz
/snapshot.json
```

`/snapshot.json` is read-only and returns sanitized feed/archive status. It does
not expose secrets, wallet material, order endpoints, approvals, or live-control
paths.

## AWS Targets

Supported targets:

- AWS App Runner: simplest web target for the `Dockerfile`.
- ECS/Fargate: use port `8080`, a persistent volume only if SQLite hot state is
  needed, and Secrets Manager or SSM Parameter Store for env values.
- EC2 via SSM: run the same container and manage it through SSM, not routine SSH.

Initial service names from the production architecture:

- `oloraculo-cockpit-api`
- `oloraculo-feed-status`
- `oloraculo-r2-archiver`
- `oloraculo-clob-hotpath`
- `oloraculo-sports-scalp-scanner`

Only `oloraculo-cockpit-api` is configured here. The other services remain
separate future deploy units.

## Required Environment

Start from `oloraculo.aws.env.example`. Keep real values in AWS-managed secrets
or environment variables. Do not commit real credentials.

Archive modes:

- Cloudflare R2: set `Provider=R2`, `Endpoint`, `Bucket`, `AWS_REGION=auto`, and
  `ForcePathStyle=true`.
- AWS S3: set `Provider=S3`, omit `Endpoint`, set `Bucket`, `AWS_REGION`, and
  AWS credentials through the task role or configured env variables.

The object archive writer only mirrors explicitly saved checkpoint payloads for
now. It verifies object size/hash metadata and writes a manifest sidecar beside
the object. It does not prune local files yet.

## Local Disk Rule

`/var/oloraculo` is hot state only. Do not use the AWS instance/container disk as
the permanent raw market-data archive.
