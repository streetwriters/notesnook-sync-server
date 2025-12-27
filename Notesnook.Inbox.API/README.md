# Notesnook Inbox API

## Running locally

### Requirements

- Bun (v1.0.0 or higher) 

### Environment variables

- `PORT`
- `NOTESNOOK_API_SERVER_URL`

### Commands

- `bun install` - Install dependencies
- `bun run dev` - Start the development server
- `bun run build` - Build the project for production
- `bun run start` - Start the production server

## Self-hosting

...

## Writing from scratch

The inbox API server is pretty simple to write from scratch in any programming language and/or framework. There's only one endpoint that needs to be implemented, which does these three steps:

1. Fetch the user's public inbox API key from the Notesnook API.
2. Encrypt the payload (via libsodium).
3. Post the encrypted payload to the Notesnook API. 

You can refer to the [source code](./src/index.ts) for implementation details.
