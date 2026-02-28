# Migrating Notesnook from MinIO to GarageHQ on Unraid

This guide walks you through replacing MinIO with GarageHQ (S3 Object Storage) in your Notesnook Docker stack.

## Phase 1: Preparation

1.  **Backup Data**:

    - Open your Notesnook Desktop/Web App.
    - Go to **Settings** -> **Backups**.
    - Create a full backup and download it to your computer.
    - _Note: This backup usually contains your notes and encryption keys. Attachments might need to be re-synced if not embedded in the backup. Ensure you have synced all data._

2.  **Generate RPC Secret**:

    - You need a random 32-byte hex string for `GARAGE_RPC_SECRET`.
    - Run this in a terminal (or use an online generator):
      ```bash
      openssl rand -hex 32
      ```
    - Copy this value.

3.  **Update `.env`**:

    - Add the following variables to your `.env` file:

      ```env
      # Garage Configuration
      GARAGE_RPC_SECRET=your_generated_hex_string_here

      # Placeholders (will be filled after initialization)
      GARAGE_ACCESS_KEY_ID=placeholder
      GARAGE_ACCESS_KEY_SECRET=placeholder
      ```

## Phase 2: Deployment

1.  **Replace Docker Compose**:

    - Replace your existing `docker-compose.yml` with the content of `docker-compose.garage.yml` (provided in this repo).
    - _Key changes_: Removed `notesnook-s3` (MinIO) and `setup-s3`. Added `garage` service on port 3900. Updated `notesnook-server` env vars.

2.  **Start the Stack**:
    - **Warning (Existing Data)**: The provided `docker-compose.garage.yml` uses `mongo:8.0`. If you are migrating an existing MongoDB database (version 7.0), you **must** edit the file to use `mongo:7.0` first, or follow the guide in `MONGO_UPGRADE.md` before starting.
    - Deploy the stack via Portainer or `docker compose up -d`.
    - **Note**: The `notesnook-server` will fail or be unhealthy initially because the access keys (`placeholder`) are invalid. This is expected. `garage` should start and be healthy.

## Phase 3: Initialization

You must run these commands via SSH on your server.

### Option A: Automated Setup (Recommended)

Run the provided setup script to automatically initialize the layout, create buckets, and generate keys.

```bash
chmod +x setup-garage.sh
./setup-garage.sh
```

---

### Option B: Manual Initialization (Fallback)

If the automated script fails, follow these steps manually.

**1. Initialize Garage Layout**
This sets up your single node as the storage cluster.

```bash
# Verify the container name (adjust 'notesnook-garage-1' if different)
CONTAINER_NAME=$(docker ps --format "{{.Names}}" | grep garage | head -n 1)

# Get the Node ID (required for assignment)
NODE_ID=$(docker exec $CONTAINER_NAME /garage node id -q | cut -d '@' -f 1)

# Assign the layout to this node (Zone: us-east-1, Capacity: 10GB - adjust if needed)
docker exec $CONTAINER_NAME /garage layout assign -z us-east-1 -c 10G $NODE_ID

# Apply changes
echo "yes" | docker exec -i $CONTAINER_NAME /garage layout apply --version 1
```

**2. Create Bucket & API Key**
This creates the `attachments` bucket, sets the domain alias, and creates an API key.

```bash
# Create bucket
docker exec $CONTAINER_NAME /garage bucket create attachments

# ALIAS the bucket to your public domain (Crucial for website access)
docker exec $CONTAINER_NAME /garage bucket alias attachments attach.clayauld.com

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

## Phase 4: Cloudflare & Permissions

1.  **Cloudflare Tunnel Configuration**:

    - Ensure your public hostname `yourhost.com` points to the **Garage Service** on port **3900** (S3 API).
    - _Do not point it to 3902 (Website Port) as this will break file uploads._

2.  **CORS & Public Access Policy**:
    Garage is strict about CORS and permissions. Run this command to:

    - Allow "Permissive" CORS (so the web app can fetch images).
    - Set the bucket to **Public Read** (so images load without "Failed to fetch" errors).

    _Replace `GK_YOUR_KEY_ID` and `YOUR_SECRET_KEY` with the values generated above._
    _Replace `http://172.17.0.1:3900` with your Unraid LAN IP if needed._

```bash
docker run --rm \
  --env AWS_ACCESS_KEY_ID=GK_YOUR_KEY_ID \
  --env AWS_SECRET_ACCESS_KEY=YOUR_SECRET_KEY \
  amazon/aws-cli \
  --endpoint-url http://172.17.0.1:3900 \
  bash -c "
    # 1. Apply CORS Policy
    aws s3api put-bucket-cors --bucket attachments --cors-configuration '{
      \"CORSRules\": [
        {
          \"AllowedHeaders\": [\"*\"],
          \"AllowedMethods\": [\"GET\", \"PUT\", \"POST\", \"DELETE\", \"HEAD\"],
          \"AllowedOrigins\": [\"*\"],
          \"ExposeHeaders\": [\"ETag\"]
        }
      ]
    }'

    # 2. Apply Public Read Policy (Standard S3 Policy)
    aws s3api put-bucket-policy --bucket attachments --policy '{
      \"Version\": \"2012-10-17\",
      \"Statement\": [
        {
          \"Sid\": \"PublicReadGetObject\",
          \"Effect\": \"Allow\",
          \"Principal\": \"*\",
          \"Action\": \"s3:GetObject\",
          \"Resource\": \"arn:aws:s3:::attachments/*\"
        }
      ]
    }'
  "
```

_Note: `172.17.0.1` is usually the Docker host IP. If that fails, use your Unraid server's LAN IP._

## Phase 5: Final Configuration

1.  **Update `.env` again**:

    - Update `GARAGE_ACCESS_KEY_ID` and `GARAGE_ACCESS_KEY_SECRET` with the new values.
    - Ensure `ATTACHMENTS_SERVER_PUBLIC_URL` points to `https://attach.clayauld.com`.

2.  **Restart Notesnook Server**:

    ```bash
    docker compose up -d --force-recreate notesnook-server
    ```

3.  **Verify**:
    - Check logs: `docker logs notesnook-server`
    - Open Notesnook. Images should now upload/download correctly.

## Migration (Data Restore)

Since we started with a fresh `garage` volume:

1.  **If you had the full backup**: Restore it in the Notesnook app.
2.  **If you need to migrate files from old MinIO**:
    - Start MinIO on a different port (e.g., 9005).
    - Use `rclone` to copy from MinIO to Garage.
    - Command example:
      ```bash
      rclone sync minio:attachments garage:attachments --progress
      ```
