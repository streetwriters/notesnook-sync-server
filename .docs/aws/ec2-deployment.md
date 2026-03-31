# Deployment Plan: Notesnook Sync Server on AWS EC2

## Context
Deploy toàn bộ Notesnook self-hosted backend lên một EC2 instance duy nhất dùng Docker Compose. Stack gồm MongoDB, MinIO, 4 .NET services, 1 Bun/TypeScript service. Setup Nginx reverse proxy + Let's Encrypt SSL cho domain riêng. Backup định kỳ MongoDB + MinIO lên S3.

---

## Phase 1: Chuẩn bị AWS

### 1.1 Launch EC2 Instance
- **AMI:** Ubuntu 24.04 LTS
- **Instance type:** `t3.medium` (2 vCPU, 4GB RAM) — tối thiểu; `t3.large` nếu nhiều user
- **Storage:** 30GB root volume (gp3) + 1 EBS volume 50GB+ cho data (mount `/data`)
- **Security Group rules:**

  | Port | Protocol | Source      | Mục đích                      |
  |------|----------|-------------|-------------------------------|
  | 22   | TCP      | My IP       | SSH                           |
  | 80   | TCP      | 0.0.0.0/0   | HTTP (redirect to HTTPS)      |
  | 443  | TCP      | 0.0.0.0/0   | HTTPS                         |

> **KHÔNG mở** các port nội bộ (5264, 8264, 7264, 6264, 27017, 9000) ra internet — chỉ expose qua Nginx.

### 1.2 Elastic IP
- Cấp 1 Elastic IP, gắn vào instance để IP không đổi khi restart.

### 1.3 DNS
Tạo các A record trỏ về Elastic IP:

| Subdomain               | Service           | Internal Port |
|-------------------------|-------------------|---------------|
| `api.yourdomain.com`    | Notesnook API     | 5264          |
| `auth.yourdomain.com`   | Identity Server   | 8264          |
| `sse.yourdomain.com`    | SSE Server        | 7264          |
| `s3.yourdomain.com`     | MinIO API         | 9000          |
| `mono.yourdomain.com`   | Monograph         | 6264 *(optional)* |

---

## Phase 2: Setup EC2

### 2.1 Install dependencies
```bash
# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker ubuntu

# Docker Compose v2
sudo apt install docker-compose-plugin -y

# Nginx + Certbot
sudo apt install nginx certbot python3-certbot-nginx -y
```

### 2.2 Mount EBS data volume
```bash
sudo mkfs.ext4 /dev/xvdb
sudo mkdir /data
sudo mount /dev/xvdb /data

# Auto-mount khi reboot
echo '/dev/xvdb /data ext4 defaults,nofail 0 2' | sudo tee -a /etc/fstab
```

### 2.3 Clone repo
```bash
cd /data
git clone https://github.com/your-fork/notesnook-sync-server.git
cd notesnook-sync-server
```

---

## Phase 3: Cấu hình

### 3.1 Tạo file `.env`
```env
INSTANCE_NAME=my-notesnook
NOTESNOOK_API_SECRET=<random 32+ char string>
DISABLE_SIGNUPS=false

# SMTP
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USERNAME=your@email.com
SMTP_PASSWORD=app-password

# Public URLs
AUTH_SERVER_PUBLIC_URL=https://auth.yourdomain.com
NOTESNOOK_APP_PUBLIC_URL=https://app.notesnook.com
MONOGRAPH_PUBLIC_URL=https://mono.yourdomain.com
ATTACHMENTS_SERVER_PUBLIC_URL=https://s3.yourdomain.com

# MinIO
MINIO_ROOT_USER=admin
MINIO_ROOT_PASSWORD=<strong-password>
```

### 3.2 Bind Docker volumes lên EBS
Thêm vào cuối `docker-compose.yml`, thay thế phần `volumes:`:

```yaml
volumes:
  dbdata:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/mongodb
  s3data:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/minio
```

Tạo thư mục và cấp quyền:
```bash
sudo mkdir -p /data/mongodb /data/minio
sudo chown -R 1000:1000 /data/mongodb /data/minio
```

---

## Phase 4: Nginx Reverse Proxy + SSL

### 4.1 Tạo `/etc/nginx/sites-available/notesnook`

```nginx
# Notesnook API (SignalR — cần timeout cao)
server {
    server_name api.yourdomain.com;
    location / {
        proxy_pass http://localhost:5264;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
    }
}

# Identity Server
server {
    server_name auth.yourdomain.com;
    location / {
        proxy_pass http://localhost:8264;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# SSE Server (cần tắt buffering)
server {
    server_name sse.yourdomain.com;
    location / {
        proxy_pass http://localhost:7264;
        proxy_http_version 1.1;
        proxy_set_header Connection '';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 86400s;
    }
}

# MinIO S3 (cần client_max_body_size cho attachments)
server {
    server_name s3.yourdomain.com;
    client_max_body_size 100m;
    location / {
        proxy_pass http://localhost:9000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### 4.2 Enable + lấy SSL
```bash
sudo ln -s /etc/nginx/sites-available/notesnook /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx

sudo certbot --nginx \
  -d api.yourdomain.com \
  -d auth.yourdomain.com \
  -d sse.yourdomain.com \
  -d s3.yourdomain.com
```

---

## Phase 5: Start Stack

```bash
cd /data/notesnook-sync-server
docker compose up -d

# Theo dõi logs
docker compose logs -f --tail=50
```

---

## Phase 6: Backup

### 6.1 Script `/data/backup.sh`
```bash
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M%S)
S3_BUCKET=s3://your-backup-bucket/notesnook

# MongoDB dump
docker exec notesnook-sync-server-notesnook-db-1 \
  mongodump --out=/tmp/mongodump
docker cp notesnook-sync-server-notesnook-db-1:/tmp/mongodump /tmp/mongodump_$DATE
tar czf /tmp/mongo_$DATE.tar.gz /tmp/mongodump_$DATE
aws s3 cp /tmp/mongo_$DATE.tar.gz $S3_BUCKET/mongodb/
rm -rf /tmp/mongodump_$DATE /tmp/mongo_$DATE.tar.gz

# MinIO data sync
aws s3 sync /data/minio $S3_BUCKET/minio/

echo "Backup $DATE completed"
```

```bash
chmod +x /data/backup.sh
```

### 6.2 Cron job — 2AM hàng ngày
```bash
sudo crontab -e
# Thêm dòng:
0 2 * * * /data/backup.sh >> /var/log/notesnook-backup.log 2>&1
```

---

## Phase 7: Systemd Auto-start

```bash
sudo tee /etc/systemd/system/notesnook.service <<EOF
[Unit]
Description=Notesnook Sync Server
After=docker.service
Requires=docker.service

[Service]
WorkingDirectory=/data/notesnook-sync-server
ExecStart=/usr/bin/docker compose up
ExecStop=/usr/bin/docker compose down
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl enable notesnook
sudo systemctl start notesnook
```

---

## Verification Checklist

- [ ] `https://auth.yourdomain.com/health` → 200
- [ ] `https://api.yourdomain.com/health` → 200
- [ ] `https://sse.yourdomain.com/health` → 200
- [ ] `https://s3.yourdomain.com` accessible
- [ ] Đăng ký tài khoản mới trên Notesnook app (server URL: `https://auth.yourdomain.com`)
- [ ] Sync notes hoạt động
- [ ] Upload attachment hoạt động
- [ ] Reboot EC2 → services tự start lại
- [ ] Chạy backup script → file xuất hiện trên S3

---

## Chi phí ước tính (AWS ap-southeast-1 / Singapore)

| Resource          | Spec              | ~$/tháng  |
|-------------------|-------------------|-----------|
| EC2 t3.medium     | On-demand         | ~$35      |
| EC2 t3.medium     | Reserved 1 năm    | ~$22      |
| EBS gp3 50GB      | Data volume       | ~$4       |
| EBS gp3 30GB      | Root volume       | ~$2.40    |
| Elastic IP        | (free khi attach) | $0        |
| S3 backup ~10GB   |                   | ~$0.23    |
| **Total**         | On-demand         | **~$42**  |
| **Total**         | Reserved 1 năm    | **~$29**  |
