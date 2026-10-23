#!/bin/bash
set -e

# Colors for better readability
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}--- Notesnook Garage Setup Assistant ---${NC}"
echo "This script will initialize your Garage storage cluster and configure it for Notesnook."

# 1. Detect Garage Container
echo -e "\n${YELLOW}[1/6] Detecting Garage container...${NC}"
CONTAINER_NAME=$(docker ps --format "{{.Names}}" | grep garage | head -n 1)

if [ -z "$CONTAINER_NAME" ]; then
    echo -e "${RED}Error: Could not find a running Garage container.${NC}"
    echo "Please ensure your Docker stack is running (docker compose up -d)."
    exit 1
fi
echo -e "Found container: ${GREEN}$CONTAINER_NAME${NC}"

# 2. Initialize Layout
echo -e "\n${YELLOW}[2/6] Initializing Layout...${NC}"

# Retry loop for Node ID (Garage might take a second to generate the key)
MAX_RETRIES=5
RETRY_COUNT=0
NODE_ID=""

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    NODE_ID=$(docker exec $CONTAINER_NAME /garage node id -q | cut -d '@' -f 1)
    if [ -n "$NODE_ID" ]; then
        break
    fi
    echo "Waiting for Garage to generate node key (attempt $((RETRY_COUNT+1))/$MAX_RETRIES)..."
    sleep 3
    RETRY_COUNT=$((RETRY_COUNT+1))
done

if [ -z "$NODE_ID" ]; then
    echo -e "${RED}Error: Failed to obtain Node ID after $MAX_RETRIES attempts.${NC}"
    exit 1
fi

echo -e "Node ID: ${GREEN}$NODE_ID${NC}"

read -p "Enter Zone (default: us-east-1): " ZONE
ZONE=${ZONE:-us-east-1}

read -p "Enter Capacity (e.g., 10G, 100G) (default: 10G): " CAPACITY
CAPACITY=${CAPACITY:-10G}

echo -e "\nAssigning layout: Zone=${ZONE}, Capacity=${CAPACITY}"
docker exec $CONTAINER_NAME /garage layout assign -z "$ZONE" -c "$CAPACITY" "$NODE_ID"

echo -e "\n${YELLOW}Current Layout Status:${NC}"
docker exec $CONTAINER_NAME /garage layout show

read -p "Apply this layout? (y/n): " confirm
if [[ $confirm != [yY] ]]; then
    echo "Aborting layout application."
    exit 1
fi

echo "yes" | docker exec -i $CONTAINER_NAME /garage layout apply --version 1
echo -e "${GREEN}Layout applied successfully!${NC}"

# Wait for layout to be ready
echo -e "\n${YELLOW}Waiting for Garage layout to be ready...${NC}"
MAX_READY_RETRIES=10
READY_RETRY_COUNT=0
while [ $READY_RETRY_COUNT -lt $MAX_READY_RETRIES ]; do
    if docker exec $CONTAINER_NAME /garage bucket list >/dev/null 2>&1; then
        echo -e "${GREEN}Garage layout is ready!${NC}"
        break
    fi
    echo "Layout not ready yet, waiting... (attempt $((READY_RETRY_COUNT+1))/$MAX_READY_RETRIES)"
    sleep 3
    READY_RETRY_COUNT=$((READY_RETRY_COUNT+1))
done

if [ $READY_RETRY_COUNT -eq $MAX_READY_RETRIES ]; then
    echo -e "${RED}Warning: Layout still not ready after waiting. Some operations might fail.${NC}"
fi

# 3. Create Bucket and Alias
echo -e "\n${YELLOW}[3/6] Creating Bucket and Alias...${NC}"
read -p "Enter bucket name (default: attachments): " BUCKET
BUCKET=${BUCKET:-attachments}

read -p "Enter public domain alias (e.g., attach.example.com): " ALIAS
if [ -z "$ALIAS" ]; then
    echo -e "${RED}Error: Public domain alias is required.${NC}"
    exit 1
fi

docker exec $CONTAINER_NAME /garage bucket create "$BUCKET"
docker exec $CONTAINER_NAME /garage bucket alias "$BUCKET" "$ALIAS"
echo -e "${GREEN}Bucket '$BUCKET' created and aliased to '$ALIAS'.${NC}"

# 4. Create API Key
echo -e "\n${YELLOW}[4/6] Creating API Key...${NC}"
KEY_INFO=$(docker exec $CONTAINER_NAME /garage key create notesnook-key)
echo -e "${GREEN}Key created:${NC}"
echo "$KEY_INFO"

KEY_ID=$(echo "$KEY_INFO" | grep "Key ID:" | awk '{print $3}')
SECRET_KEY=$(echo "$KEY_INFO" | grep "Secret key:" | awk '{print $3}')

if [ -z "$KEY_ID" ] || [ -z "$SECRET_KEY" ]; then
    echo -e "${RED}Error: Failed to extract Key ID or Secret Key.${NC}"
    exit 1
fi

# 5. Update .env file
echo -e "\n${YELLOW}[5/6] Updating .env file...${NC}"
if [ -f ".env" ]; then
    # Backup .env
    cp .env .env.bak
    
    # Update variables
    sed -i "s/^GARAGE_ACCESS_KEY_ID=.*/GARAGE_ACCESS_KEY_ID=$KEY_ID/" .env
    sed -i "s/^GARAGE_ACCESS_KEY_SECRET=.*/GARAGE_ACCESS_KEY_SECRET=$SECRET_KEY/" .env
    sed -i "s|^ATTACHMENTS_SERVER_PUBLIC_URL=.*|ATTACHMENTS_SERVER_PUBLIC_URL=https://$ALIAS|" .env
    
    echo -e "${GREEN}.env file updated (backup created as .env.bak).${NC}"
else
    echo -e "${RED}Warning: .env file not found. Please update it manually:${NC}"
    echo "GARAGE_ACCESS_KEY_ID=$KEY_ID"
    echo "GARAGE_ACCESS_KEY_SECRET=$SECRET_KEY"
    echo "ATTACHMENTS_SERVER_PUBLIC_URL=https://$ALIAS"
fi

# 6. Configure Permissions and Policies
echo -e "\n${YELLOW}[6/6] Configuring Permissions and Policies...${NC}"
echo "Applying bucket permissions..."
docker exec $CONTAINER_NAME /garage bucket allow "$BUCKET" --read --write --owner --key "$KEY_ID"
echo "Enabling bucket website access (enables public read)..."
docker exec $CONTAINER_NAME /garage bucket website --allow "$BUCKET"

echo -e "\nApplying CORS policy via AWS CLI container..."
# Try to detect host IP
HOST_IP=$(hostname -I | awk '{print $1}')
if [ -z "$HOST_IP" ]; then
    HOST_IP="172.17.0.1"
fi

read -p "Enter Garage S3 API Endpoint for policy application (default: http://$HOST_IP:3900): " ENDPOINT
ENDPOINT=${ENDPOINT:-http://$HOST_IP:3900}

docker run --rm \
  --env AWS_ACCESS_KEY_ID="$KEY_ID" \
  --env AWS_SECRET_ACCESS_KEY="$SECRET_KEY" \
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

echo "Applying Public Read Policy (native Garage command)..."
docker exec $CONTAINER_NAME /garage bucket allow "$BUCKET" --read --key "$KEY_ID" # Already granted above

echo -e "\n${GREEN}--- Setup Complete! ---${NC}"
echo "1. Your .env file has been updated."
echo "2. Restart your Notesnook server: docker compose up -d --force-recreate notesnook-server"
echo "3. Verify your setup in the Notesnook app."
