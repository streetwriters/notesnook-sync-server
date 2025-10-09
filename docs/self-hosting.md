# Self-hosting the Notesnook Sync Server

The Notesnook Sync Server can be easily self-hosted using Docker. This guide will walk you through the setup process.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and [Docker Compose](https://docs.docker.com/compose/install/)
- A domain name or public IP address for external access
- SMTP email server credentials

## Quick Start

### 1. Download the docker-compose.yml file

```bash
wget https://raw.githubusercontent.com/streetwriters/notesnook-sync-server/master/docker-compose.yml
```

### 2. Create your environment configuration

```bash
# Download the example environment file
wget https://raw.githubusercontent.com/streetwriters/notesnook-sync-server/master/.env.example

# Copy it to create your configuration
cp .env.example .env

# Edit the .env file with your settings
nano .env  # or use your preferred editor
```

### 3. Configure the required environment variables

Edit the `.env` file and configure the following required variables:

- `INSTANCE_NAME` - Your server instance name
- `NOTESNOOK_API_SECRET` - Generate a strong random secret
- `DISABLE_SIGNUPS` - Set to `true` to disable new registrations
- `SMTP_*` - Your email server settings for notifications
- Update all `*_PUBLIC_URL` variables with your domain/IP

### 4. Start the services

```bash
docker compose up -d
```

## Service Ports

The following ports will be exposed:

- **5264** - Main Notesnook sync server
- **8264** - Identity/authentication server  
- **7264** - SSE (Server-Sent Events) server
- **6264** - Monograph publishing server
- **9000** - MinIO S3 storage (for file attachments)

## Verification

### Check service health

Check that all services are healthy:

```bash
docker compose ps
```

### Access health endpoints

You can verify that all services are running correctly by accessing their health endpoints:

- Sync Server: `http://localhost:5264/health`
- Identity Server: `http://localhost:8264/health`  
- SSE Server: `http://localhost:7264/health`
- Monograph Server: `http://localhost:6264/api/health`

## Volume Configuration

By default, the Docker Compose setup uses named Docker volumes for persistent data storage. However, you may want to bind these volumes to specific directories on your host system for easier backup and management.

### Default Volumes

The setup creates two Docker volumes:
- `dbdata` - MongoDB database files
- `s3data` - MinIO S3 storage for file attachments

### Binding Volumes to Host Directories

To bind volumes to specific host directories, modify your `docker-compose.yml` file:

```yaml
volumes:
  dbdata:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /path/to/your/mongodb/data
  s3data:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /path/to/your/s3/data
```

Or use the simpler bind mount syntax by modifying the services directly:

```yaml
services:
  notesnook-db:
    # ... other configuration
    volumes:
      - /path/to/your/mongodb/data:/data/db

  notesnook-s3:
    # ... other configuration
    volumes:
      - /path/to/your/s3/data:/data/s3
```

### Recommended Directory Structure

We recommend creating a dedicated directory structure for your Notesnook data:

```bash
mkdir -p /opt/notesnook/{mongodb,s3}
chown -R 999:999 /opt/notesnook/mongodb  # MongoDB user
chown -R 1000:1000 /opt/notesnook/s3     # MinIO user
```

Then use these paths in your volume configuration:
- MongoDB data: `/opt/notesnook/mongodb`
- S3 data: `/opt/notesnook/s3`

### Backup Considerations

When using bind mounts, you can easily backup your data by creating snapshots or backups of these directories:

```bash
# Example backup script
tar -czf notesnook-backup-$(date +%Y%m%d).tar.gz -C /opt notesnook/
```

## What's included

This setup takes care of everything including:
- MongoDB database
- MinIO S3 storage for file attachments
- All required Notesnook services (API, Identity, Messenger, Monograph)

## Self-hosting Status

**Note: Self-hosting the Notesnook Sync Server is now possible, but without support. We are working to enable full on-premise self-hosting, so stay tuned!**

### Progress Checklist

- [x] Open source the Sync server
- [x] Open source the Identity server
- [x] Open source the SSE Messaging infrastructure
- [x] Fully Dockerize all services
- [x] Use self-hosted Minio for S3 storage
- [x] Publish on DockerHub
- [x] Add settings to change server URLs in Notesnook client apps (starting from v3.0.18)
- [ ] Write comprehensive self-hosting documentation