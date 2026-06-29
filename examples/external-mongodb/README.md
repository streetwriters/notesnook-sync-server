# External MongoDB

MongoDB runs in a separate compose project. The Notesnook stack uses the root [`docker-compose.yml`](../../docker-compose.yml) with [`notesnook.override.yml`](notesnook.override.yml), which disables the embedded `notesnook-db` service and points at external Mongo. Embedded MinIO is unchanged.

## Setup

From the **repository root**:

```bash
# Merge into root .env
cat examples/external-mongodb/.env.example >> .env
# set NOTESNOOK_API_SECRET and other required root .env values

# 1. MongoDB
docker compose -f examples/external-mongodb/infra.compose.yml up -d

# 2. Notesnook
docker compose -f docker-compose.yml -f examples/external-mongodb/notesnook.override.yml up -d
```

## Environment (delta)

See [`.env.example`](.env.example) — only `MONGODB_CONNECTION_STRING*` overrides are needed beyond the root `.env`.

## Stop

```bash
docker compose -f docker-compose.yml -f examples/external-mongodb/notesnook.override.yml down
docker compose -f examples/external-mongodb/infra.compose.yml down
```

## Notes

- Replica set `rs0` is required; the infra healthcheck initializes it.
- Host port `27017` is exposed for debugging.
- If infra stops, `identity-server` and `notesnook-server` fail until MongoDB is back.
