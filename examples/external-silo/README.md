# External Silo

[Silo](https://silo.pgsty.com/) (MinIO-compatible S3) runs in a separate compose project. The Notesnook stack uses the root [`docker-compose.yml`](../../docker-compose.yml) with [`notesnook.override.yml`](notesnook.override.yml), which disables embedded `notesnook-s3` / `setup-s3` and points at external Silo. Embedded MongoDB is unchanged.

## Setup

From the **repository root**:

```bash
# Ensure MINIO_* in root .env match infra.env.example
# Merge notesnook delta if needed (see .env.example)

# 1. Silo + bucket setup
docker compose -f examples/external-silo/infra.compose.yml \
  --env-file examples/external-silo/infra.env.example up -d

# Wait for notesnook-silo-setup to complete
docker compose -f examples/external-silo/infra.compose.yml ps -a

# 2. Notesnook
docker compose -f docker-compose.yml -f examples/external-silo/notesnook.override.yml up -d
```

## Environment

| File | Purpose |
|------|---------|
| [`infra.env.example`](infra.env.example) | `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` for infra stack |
| [`.env.example`](.env.example) | Notesnook delta (credentials must match infra) |

Merge `MINIO_*` into root [`.env`](../../.env) so the sync server uses the same credentials. Silo still reads the `MINIO_*` variable names for compatibility.

## Stop

```bash
docker compose -f docker-compose.yml -f examples/external-silo/notesnook.override.yml down
docker compose -f examples/external-silo/infra.compose.yml down
```

## Notes

- Bucket `attachments` is created by `notesnook-silo-setup` on first infra start.
- `S3_INTERNAL_SERVICE_URL` is overridden to `http://notesnook-silo:9000`.
