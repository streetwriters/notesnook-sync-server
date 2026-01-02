# Migrating Notesnook from MinIO to GarageHQ on Unraid

This guide walks you through replacing MinIO with GarageHQ (S3 Object Storage) in your Notesnook Docker stack.

## Phase 1: Preparation

1.  **Backup Data**:
    *   Open your Notesnook Desktop/Web App.
    *   Go to **Settings** -> **Backups**.
    *   Create a full backup and download it to your computer.
    *   *Note: This backup usually contains your notes and encryption keys. Attachments might need to be re-synced if not embedded in the backup. Ensure you have synced all data.*

2.  **Generate RPC Secret**:
    *   You need a random 32-byte hex string for `GARAGE_RPC_SECRET`.
    *   Run this in a terminal (or use an online generator):
        ```bash
        openssl rand -hex 32
        ```
    *   Copy this value.

3.  **Update `.env`**:
    *   Add the following variables to your `.env` file:
        ```env
        # Garage Configuration
        GARAGE_RPC_SECRET=your_generated_hex_string_here

        # Placeholders (will be filled after initialization)
        GARAGE_ACCESS_KEY_ID=placeholder
        GARAGE_ACCESS_KEY_SECRET=placeholder
        ```

## Phase 2: Deployment

1.  **Replace Docker Compose**:
    *   Replace your existing `docker-compose.yml` with the content of `docker-compose.garage.yml` (provided in this repo).
    *   *Key changes*: Removed `notesnook-s3` (MinIO) and `setup-s3`. Added `garage` service on port 3900. Updated `notesnook-server` env vars.

2.  **Start the Stack**:
    *   Deploy the stack via Portainer or `docker compose up -d`.
    *   **Note**: The `notesnook-server` will fail or be unhealthy initially because the access keys (`placeholder`) are invalid. This is expected. `garage` should start and be healthy.

## Phase 3: Initialization (The "Single-Line" Commands)

You must run these commands via SSH on your Synology/Unraid server.

**1. Initialize Garage Layout**
This sets up your single node as the storage cluster.
```bash
# Verify the container name (adjust 'notesnook-garage-1' if different)
CONTAINER_NAME=$(docker ps --format "{{.Names}}" | grep garage | head -n 1)

docker exec $CONTAINER_NAME /garage layout assign -z us-east-1 -c 1G localhost
echo "yes" | docker exec -i $CONTAINER_NAME /garage layout apply --version 1
```

**2. Create Bucket & API Key**
This creates the `attachments` bucket and a key with Owner permissions.
```bash
# Create bucket
docker exec $CONTAINER_NAME /garage bucket create attachments

# Create key and save output
KEY_INFO=$(docker exec $CONTAINER_NAME /garage key create notesnook-key)
echo "$KEY_INFO"

# Extract ID and Secret (Visually copy these from the output above!)
# Key ID looks like: GK...
# Secret looks like: ...
```

**3. Configure Permissions & Website Access**
Replace `GK_YOUR_KEY_ID` with the Key ID from the previous step.
```bash
# Allow the key to read/write the bucket
docker exec $CONTAINER_NAME /garage bucket allow attachments --read --write --owner --key GK_YOUR_KEY_ID

# Enable public website access (allow public reads for images)
docker exec $CONTAINER_NAME /garage bucket website --allow attachments
```

## Phase 4: CORS Configuration

Garage is strict about CORS. Run this command to allow your Notesnook app to fetch images.
*Replace `GK_YOUR_KEY_ID` and `YOUR_SECRET_KEY` with the values generated above.*
*Replace `http://localhost:3900` with your actual Garage S3 URL if different (e.g. `https://attach.clayauld.com` if using public endpoint, or keep localhost if running from the server).*

```bash
docker run --rm \
  --env AWS_ACCESS_KEY_ID=GK_YOUR_KEY_ID \
  --env AWS_SECRET_ACCESS_KEY=YOUR_SECRET_KEY \
  amazon/aws-cli \
  --endpoint-url http://172.17.0.1:3900 \
  s3api put-bucket-cors --bucket attachments --cors-configuration '{
    "CORSRules": [
      {
        "AllowedHeaders": ["*"],
        "AllowedMethods": ["GET", "PUT", "POST", "DELETE", "HEAD"],
        "AllowedOrigins": ["*"],
        "ExposeHeaders": ["ETag"]
      }
    ]
  }'
```
*Note: `172.17.0.1` is usually the Docker host IP. If that fails, use your Unraid server's LAN IP.*

## Phase 5: Final Configuration

1.  **Update `.env` again**:
    *   Update `GARAGE_ACCESS_KEY_ID` and `GARAGE_ACCESS_KEY_SECRET` with the new values.
    *   Ensure `ATTACHMENTS_SERVER_PUBLIC_URL` points to `https://attach.clayauld.com`.

2.  **Restart Notesnook Server**:
    ```bash
    docker compose up -d --force-recreate notesnook-server
    ```

3.  **Verify**:
    *   Check logs: `docker logs notesnook-server`
    *   Open Notesnook. Images should now upload/download correctly.

## Migration (Data Restore)

Since we started with a fresh `garage` volume:
1.  **If you had the full backup**: Restore it in the Notesnook app.
2.  **If you need to migrate files from old MinIO**:
    *   Start MinIO on a different port (e.g., 9005).
    *   Use `rclone` to copy from MinIO to Garage.
    *   Command example:
        ```bash
        rclone sync minio:attachments garage:attachments --progress
        ```
