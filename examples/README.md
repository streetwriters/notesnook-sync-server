# Docker Compose Examples

Reference deployments that split infrastructure from the Notesnook stack. Each example overrides the root [`docker-compose.yml`](../docker-compose.yml) instead of duplicating it.

## Pattern

Run all commands from the **repository root**.

| Step | Compose files | Role |
|------|---------------|------|
| 1. Infra | `examples/<example>/infra.compose.yml` | MongoDB, MinIO, or Garage on `notesnook-shared` |
| 2. Notesnook | `docker-compose.yml` + `examples/<example>/notesnook.override.yml` | Disables embedded services; patches connection strings |

```bash
# 1. Merge example vars into root .env (see examples/<example>/.env.example)
# 2. Start infrastructure
docker compose -f examples/<example>/infra.compose.yml up -d

# 3. Start Notesnook (one-time setup for garage — see example README)
docker compose -f docker-compose.yml -f examples/<example>/notesnook.override.yml up -d

# Optional extras
docker compose -f docker-compose.yml -f examples/<example>/notesnook.override.yml --profile extras up -d

# Tear down (reverse order)
docker compose -f docker-compose.yml -f examples/<example>/notesnook.override.yml down
docker compose -f examples/<example>/infra.compose.yml down
```

## Environment

The root [`.env`](../.env) is loaded automatically when running from the repository root. Each example ships a **delta-only** [`.env.example`](external-mongodb/.env.example) listing only variables that differ — merge those lines into your root `.env`.

Infra-only variables (e.g. `GARAGE_RPC_SECRET`) are in `infra.env.example` where applicable; pass with `--env-file` or merge into root `.env`.

## Examples

| Example | External dependency | Notes |
|---------|---------------------|-------|
| [external-mongodb](external-mongodb/) | MongoDB | Embedded MinIO stays enabled |
| [external-minio](external-minio/) | MinIO | Embedded MongoDB stays enabled |
| [garage](garage/) | [GarageHQ](https://garagehq.deuxfleurs.fr/) S3 | Manual bucket setup via `setup-garage.sh` |

## Shared network

Infra compose files create **`notesnook-shared`**. Override files attach Notesnook services to it as an external network. Run only one infra example at a time on the same host.

## Root compose

The all-in-one stack: [`docker-compose.yml`](../docker-compose.yml) (no override file).
