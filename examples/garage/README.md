# GarageHQ S3

[Garage](https://garagehq.deuxfleurs.fr/) replaces embedded MinIO. The Notesnook stack uses the root [`docker-compose.yml`](../../docker-compose.yml) with [`notesnook.override.yml`](notesnook.override.yml). MongoDB stays embedded.

Based on [PR #79](https://github.com/streetwriters/notesnook-sync-server/pull/79), with community review adjustments:

- Notesnook uses **presigned URLs** — no public bucket policy required.
- Do **not** use `aws s3api put-bucket-policy` for public read ([Garage S3 compatibility](https://garagehq.deuxfleurs.fr/documentation/reference-manual/s3-compatibility/)).
- Use port **3900** (S3 API) for `ATTACHMENTS_SERVER_PUBLIC_URL` when not behind a reverse proxy.

## Setup

From the **repository root**:

```bash
# 1. Generate RPC secret and add to root .env or use --env-file
openssl rand -hex 32
# add GARAGE_RPC_SECRET=... to .env (see infra.env.example)

# 2. Garage infra
docker compose -f examples/garage/infra.compose.yml up -d

# 3. One-time bucket/key setup (from repo root)
./examples/garage/setup-garage.sh

# 4. Merge keys into root .env (see .env.example)
# 5. Notesnook
docker compose -f docker-compose.yml -f examples/garage/notesnook.override.yml up -d
```

`notesnook-server` may stay unhealthy until valid `GARAGE_ACCESS_KEY_*` values are in root `.env`:

```bash
docker compose -f docker-compose.yml -f examples/garage/notesnook.override.yml up -d --force-recreate notesnook-server
```

## Environment

| File | Purpose |
|------|---------|
| [`infra.env.example`](infra.env.example) | `GARAGE_RPC_SECRET` for infra |
| [`.env.example`](.env.example) | `GARAGE_ACCESS_KEY_*` and `ATTACHMENTS_SERVER_PUBLIC_URL` (merge into root `.env`) |

## Stop

```bash
docker compose -f docker-compose.yml -f examples/garage/notesnook.override.yml down
docker compose -f examples/garage/infra.compose.yml down
```

## Manual setup

If `setup-garage.sh` fails, follow PR #79 layout/bucket/key steps manually. Skip public bucket policy. Optional bucket alias only for Garage web port 3902.

## MongoDB

This example only externalizes S3. For external MongoDB, combine with [external-mongodb](../external-mongodb/) or use the root all-in-one compose.
