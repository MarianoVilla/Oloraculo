---
name: oloraculo-aws-r2-ops
description: Operate Oloraculo AWS deployment and R2/S3-compatible archive work. Use for Docker/AWS targets, SSM runbooks, service topology, health checks, disk guards, R2 object layout, compressed raw batches, manifests, upload verification, retention, and Parquet/ZSTD materializers.
---

# Oloraculo AWS/R2 Ops

## Purpose

Use this skill when Oloraculo needs to run outside the laptop, archive durable
market history, or prove runtime/storage behavior is production-shaped.

## Required Reads

- `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md`
- `docs/source-of-truth/DATA_AND_SECRETS.md`
- `docs/source-of-truth/COMMANDS.md`
- `Dockerfile`
- `deploy/aws/` if present
- `Oloraculo.Web/Archive/` if present

## AWS Procedure

1. Confirm Oloraculo-owned service names, directories, users, logs, and ports.
2. Prefer SSM-managed runbooks; do not make SSH the normal operator path.
3. Ensure `/healthz` and `/snapshot.json` are documented and testable.
4. Add disk and inode guards for any local hot cache.
5. Measure latency before claiming AWS is faster: p50, p90, p99, sample size,
   source, and timestamp.

## R2 Procedure

1. Define env var names with placeholder-only docs.
2. Write immutable raw batches as `.ndjson.zst` or `.jsonl.zst`, targeting
   roughly 100-500 MB batches.
3. Record manifest rows with stream, object path, byte count, row count, receive
   range, SHA256, upload status, and prune status.
4. Verify uploaded object size and SHA256 before marking local data prunable.
5. Keep local disk as hot cache only; permanent history belongs in R2/S3.
6. Materialize bronze/silver/gold Parquet with ZSTD for research only after raw
   replay is reliable.

## Evidence

- Service list and runbook path.
- Health output shape.
- Archive layout example.
- Manifest schema or migration.
- Tests for upload failure, hash mismatch, retry, and prune prevention.
- `dotnet test Oloraculo.sln` when application code changes.

## Vetoes

- Polytrade runtime dependency.
- Secret values in committed files or logs.
- Local permanent raw archive.
- Prune before verified upload.
- Any order placement or wallet operation.
