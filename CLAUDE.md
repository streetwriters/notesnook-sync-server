# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Notesnook Sync Server is a self-hosted backend for the Notesnook note-taking app. It is a .NET 9.0 microservices solution with MongoDB, MinIO (S3-compatible storage), and SignalR for real-time sync. Redis is optional and only required when scaling horizontally (multiple server instances).

## Common Commands

### Running the stack

```bash
# Full stack via Docker Compose (recommended)
docker compose up

# Individual .NET services
dotnet run --project Notesnook.API/Notesnook.API.csproj
dotnet run --project Streetwriters.Identity/Streetwriters.Identity.csproj
dotnet run --project Streetwriters.Messenger/Streetwriters.Messenger.csproj
```

### Building

```bash
dotnet restore Notesnook.sln
dotnet build Notesnook.sln

# Build a single project
dotnet build Notesnook.API/Notesnook.API.csproj
```

### Inbox API (TypeScript/Bun)

```bash
cd Notesnook.Inbox.API
bun install
bun run src/index.ts
```

## Architecture

Five services communicate as follows:

```
Client
  ├── Streetwriters.Identity  (OAuth2/OpenID Connect, port 8264)
  ├── Notesnook.API           (sync + attachments, port 5264)
  │     ├── MongoDB           (note data, users, devices)
  │     ├── MinIO             (S3 attachment blobs)
  │     └── SignalR Hub       (real-time device push, in-memory by default)
  ├── Streetwriters.Messenger (SSE real-time events, port 7264)
  └── Notesnook.Inbox.API     (email inbox with OpenPGP, port 3000, Bun)
```

**Streetwriters.Common** and **Streetwriters.Data** are shared libraries (not services).

### Key design patterns

- **Repository + Unit of Work** (`Streetwriters.Data/`) wraps MongoDB.Driver. All data access goes through `UnitOfWork`.
- **MongoDB transactions** are disabled in `DEBUG`/`STAGING` build configs (replica set not required locally).
- **OAuth2 Introspection** — Notesnook.API validates tokens against Identity's introspection endpoint; it does not parse JWTs itself.
- **SignalR** uses MessagePack protocol. Runs in-memory by default (single instance). Set `SIGNALR_REDIS_CONNECTION_STRING` env var to enable Redis backplane for multi-instance horizontal scaling.
- **Quartz.NET** handles background jobs in Notesnook.API (e.g., cleanup, device-chunk maintenance).

### Collection names

Defined in `Notesnook.API/Models/Constants.cs`. Key collections: `content`, `notes`, `notebooks`, `attachments`, `settingsv2`, `sync_devices`, `inbox_items`, `inbox_api_keys`.

## Configuration

All runtime config is environment-variable driven via `.env`. Key variables:

| Variable | Purpose |
|---|---|
| `NOTESNOOK_API_SECRET` | Token signing secret (must be >32 chars) |
| `DISABLE_SIGNUPS` | Set `true` to block new registrations |
| `SMTP_*` | Required for password reset and email OTP 2FA |
| `TWILIO_*` | Optional SMS 2FA |
| `CORS_ORIGINS` | Allowed origins |
| `*_PUBLIC_URL` | Public-facing URLs for each service |
| `MINIO_ROOT_USER/PASSWORD` | S3 credentials |
| `MONGODB_CONNECTION_STRING` | MongoDB connection |
| `SIGNALR_REDIS_CONNECTION_STRING` | Optional Redis for SignalR backplane (multi-instance only) |

The `docker-compose.yml` validates required env vars at startup and initializes the MongoDB replica set automatically on first run.

## Service Ports (default)

| Service | Port |
|---|---|
| Notesnook.API | 5264 |
| Streetwriters.Identity | 8264 |
| Streetwriters.Messenger | 7264 |
| Notesnook.Inbox.API | 3000 |
| MongoDB | 27017 |
| MinIO API | 9000 |
| MinIO Console | 9001 |
