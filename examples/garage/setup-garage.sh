#!/bin/bash
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

BUCKET="${GARAGE_BUCKET:-attachments}"
ZONE="${GARAGE_ZONE:-us-east-1}"
CAPACITY="${GARAGE_CAPACITY:-10G}"
KEY_NAME="${GARAGE_KEY_NAME:-notesnook-key}"
ENDPOINT="${GARAGE_S3_ENDPOINT:-http://127.0.0.1:3900}"

echo -e "${GREEN}--- Notesnook Garage setup ---${NC}"

CONTAINER_NAME="${GARAGE_CONTAINER_NAME:-}"
if [ -z "$CONTAINER_NAME" ]; then
  CONTAINER_NAME=$(docker ps --format '{{.Names}}' | grep -E 'notesnook-garage|garage' | head -n 1 || true)
fi

if [ -z "$CONTAINER_NAME" ]; then
  echo -e "${RED}Error: no running Garage container found.${NC}"
  echo "Start infra first (from repo root): docker compose -f examples/garage/infra.compose.yml up -d"
  exit 1
fi

echo -e "Using container: ${GREEN}${CONTAINER_NAME}${NC}"

echo -e "\n${YELLOW}[1/5] Layout${NC}"
NODE_ID=""
for _ in $(seq 1 10); do
  NODE_ID=$(docker exec "$CONTAINER_NAME" /garage node id -q | cut -d '@' -f 1 || true)
  [ -n "$NODE_ID" ] && break
  sleep 2
done

if [ -z "$NODE_ID" ]; then
  echo -e "${RED}Error: could not read Garage node id.${NC}"
  exit 1
fi

echo "Node ID: $NODE_ID"
docker exec "$CONTAINER_NAME" /garage layout assign -z "$ZONE" -c "$CAPACITY" "$NODE_ID"
echo "yes" | docker exec -i "$CONTAINER_NAME" /garage layout apply --version 1

echo -e "\n${YELLOW}[2/5] Bucket${NC}"
docker exec "$CONTAINER_NAME" /garage bucket create "$BUCKET" || true

if [ -n "${GARAGE_BUCKET_ALIAS:-}" ]; then
  echo "Aliasing bucket to ${GARAGE_BUCKET_ALIAS} (optional website access)"
  docker exec "$CONTAINER_NAME" /garage bucket alias "$BUCKET" "$GARAGE_BUCKET_ALIAS"
fi

echo -e "\n${YELLOW}[3/5] API key${NC}"
KEY_INFO=$(docker exec "$CONTAINER_NAME" /garage key create "$KEY_NAME")
echo "$KEY_INFO"

KEY_ID=$(echo "$KEY_INFO" | awk '/Key ID:/ {print $3}')
SECRET_KEY=$(echo "$KEY_INFO" | awk '/Secret key:/ {print $3}')

if [ -z "$KEY_ID" ] || [ -z "$SECRET_KEY" ]; then
  echo -e "${RED}Error: failed to parse key id/secret from garage output.${NC}"
  exit 1
fi

echo -e "\n${YELLOW}[4/5] Bucket permissions${NC}"
docker exec "$CONTAINER_NAME" /garage bucket allow "$BUCKET" --read --write --owner --key "$KEY_ID"

echo -e "\n${YELLOW}[5/5] CORS (aws-cli)${NC}"
docker run --rm \
  --env AWS_ACCESS_KEY_ID="$KEY_ID" \
  --env AWS_SECRET_ACCESS_KEY="$SECRET_KEY" \
  --add-host=host.docker.internal:host-gateway \
  amazon/aws-cli \
  --endpoint-url "$ENDPOINT" \
  s3api put-bucket-cors --bucket "$BUCKET" --cors-configuration '{
    "CORSRules": [
      {
        "AllowedHeaders": ["*"],
        "AllowedMethods": ["GET", "PUT", "POST", "DELETE", "HEAD"],
        "AllowedOrigins": ["*"],
        "ExposeHeaders": ["ETag"]
      }
    ]
  }'

cat <<EOF

${GREEN}--- Setup complete ---${NC}

Merge these into the repository root .env (see examples/garage/.env.example):

  GARAGE_ACCESS_KEY_ID=$KEY_ID
  GARAGE_ACCESS_KEY_SECRET=$SECRET_KEY
  ATTACHMENTS_SERVER_PUBLIC_URL=$ENDPOINT

Notes:
  - Notesnook uses presigned URLs for attachments; public bucket policies are not required.
  - Use the S3 API URL (port 3900) for ATTACHMENTS_SERVER_PUBLIC_URL unless you proxy it.
  - Do not use aws s3api put-bucket-policy for public read; Garage does not support this like MinIO.

Then restart the sync server (from repo root):

  docker compose -f docker-compose.yml -f examples/garage/notesnook.override.yml up -d --force-recreate notesnook-server

EOF
