# Notesnook Inbox API

## Running locally

### Requirements

- Bun (v1.3.0 or higher)

### Commands

- `bun install` - Install dependencies
- `bun run dev` - Start the development server
- `bun run build` - Build the project for production
- `bun run start` - Start the production server

## Self-hosting

The easiest way to self-host is with Docker or Docker Compose.

Prerequisites:

- `docker` (Engine) installed
- `docker-compose` (optional, for multi-service setups)

Build and run with Docker:

```bash
# build the image from the current folder
docker build -t notesnook-inbox-api .

# run the container (example)
docker run --rm -p 3000:3000 \
	-e PORT=3000 \
	-e NOTESNOOK_API_SERVER_URL="https://api.notesnook.com" \
	notesnook-inbox-api
```

Docker Compose (example):

```yaml
services:
	inbox-api:
		image: notesnook-inbox-api
		build: .
		ports:
			- "3000:3000"
		environment:
			PORT: 3000
			NOTESNOOK_API_SERVER_URL: "https://api.notesnook.com"
		restart: unless-stopped
```

Environment variables:

- `PORT` — port the service listens on (default: `5181`)
- `NOTESNOOK_API_SERVER_URL` — base URL of the Notesnook API used to fetch public inbox keys

_If you prefer running without Docker, use `bun install` and `bun run start` with the environment variables set._

## Writing from scratch

The inbox API server is pretty simple to write from scratch in any programming language and/or framework. There's only one endpoint that needs to be implemented, which does these three steps:

1. Fetch the user's public inbox API key from the Notesnook API.
2. Encrypt the payload using `openpgp` or any other `openpgp` compatible library.
3. Post the encrypted payload to the Notesnook API.

You can refer to the [source code](./src/index.ts) for implementation details.
