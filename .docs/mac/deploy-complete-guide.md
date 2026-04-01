# Hướng Dẫn Deploy Notesnook Self-Hosted — Toàn Bộ Quy Trình

> EC2: `54.254.2.136` | Instance: `i-02672bfe496225644` | Ubuntu 24.04 LTS
> Cập nhật: 2026-04-01

---

## Tổng quan kiến trúc cuối cùng

```
Internet
    │
    ├── :22  SSH (My IP only)
    ├── :80  HTTP → redirect HTTPS (certbot renew)
    └── :443 HTTPS
              │
           Nginx (reverse proxy)
              │
    ┌─────────┼──────────────────┬─────────────────┐
    │         │                  │                  │
sync.*      auth.*           events.*           minio.*
.sslip.io   .sslip.io       .sslip.io          .sslip.io
    │         │                  │                  │
 :5264      :8264            :7264              :9000
Notesnook   Identity        SSE Messenger      MinIO (S3)
    │         │                  │
    └─────────┴── Docker Network ─┘
                      │         │
                   MongoDB    Redis
                   :27017     :6379
                 (internal)  (internal)
```

---

## Stage 0 — Chuẩn bị (Làm 1 lần trên máy local)

### 0.1 Accounts cần có

- [ ] AWS Account (Free Tier đủ để test, t3.medium = ~$30/tháng)
- [ ] Docker Hub Account (để push images) — tạo tại hub.docker.com
- [ ] GitHub Account + repo đã fork từ notesnook-sync-server
- [ ] Gmail Account (để dùng SMTP gửi email)

### 0.2 Chuẩn bị Gmail App Password (cho SMTP)

1. Vào **myaccount.google.com → Security → 2-Step Verification** → Bật lên
2. Vào **myaccount.google.com → Security → App passwords**
   - App name: `notesnook` → Click **Create**
   - **Copy ngay mật khẩu 16 ký tự** (chỉ hiện 1 lần, không có dấu cách)

---

## Stage 1 — Tạo EC2 Instance trên AWS Console

### 1.1 Launch Instance

1. Đăng nhập **console.aws.amazon.com** → tìm **EC2** → **Launch instance**

2. Cấu hình:
   ```
   Name: notesnook-server
   AMI: Ubuntu Server 24.04 LTS (64-bit x86)
   Instance type: t3.medium (2 vCPU, 4 GiB RAM)
   ```

3. **Key pair** → "Create new key pair":
   ```
   Name: notesnook-mac-key
   Type: RSA
   Format: .pem
   ```
   → Download về `~/Downloads/notesnook-mac-key.pem` — **GIỮ KỸ, MẤT LÀ KHÔNG SSH ĐƯỢC**

4. **Network settings — Security Group** (tạo mới):
   | Type  | Port | Source    | Mục đích |
   |-------|------|-----------|----------|
   | SSH   | 22   | My IP     | SSH access |
   | HTTP  | 80   | 0.0.0.0/0 | HTTP redirect + certbot |
   | HTTPS | 443  | 0.0.0.0/0 | Nginx HTTPS |

   > **Không mở** 5264, 8264, 7264, 9000 ở bước này — sẽ đóng lại sau khi có Nginx

5. **Configure storage**:
   ```
   Root volume:    30 GiB gp3   Delete on termination: YES
   + Add volume:   60 GiB gp3   Delete on termination: NO   ← quan trọng
   ```

6. Click **Launch instance** → Chờ ~2 phút

### 1.2 Gán Elastic IP (IP tĩnh)

1. AWS Console → **EC2 → Elastic IPs → Allocate Elastic IP address** → Allocate
2. Chọn Elastic IP vừa tạo → **Actions → Associate Elastic IP address**
3. Chọn instance vừa tạo → Associate

**Nếu lỗi "Network is not attached to any internet gateway":**
```
VPC Console → Internet Gateways → Create internet gateway
  Name: notesnook-igw → Create

Chọn IGW vừa tạo → Actions → Attach to VPC → chọn VPC của instance

VPC Console → Route Tables → chọn route table của subnet
  → Routes → Edit routes → Add route:
    Destination: 0.0.0.0/0
    Target: Internet Gateway vừa tạo
  → Save

→ Associate Elastic IP lại với instance
```

### 1.3 Verify kết nối

```bash
# Set permission cho key file
chmod 400 ~/Downloads/notesnook-mac-key.pem

# SSH vào instance
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136
```

✅ **Kiểm tra thành công:** thấy dòng `ubuntu@ip-10-0-1-78:~$`

---

## Stage 2 — Setup Server (Chạy trên EC2 qua SSH)

### 2.1 Update hệ thống

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y git curl wget unzip build-essential
```

### 2.2 Kiểm tra disk hiện tại

```bash
lsblk
```

Kết quả mong đợi:
```
NAME         SIZE TYPE MOUNTPOINTS
nvme0n1       30G disk           ← root EBS
├─nvme0n1p1   29G part /
...
nvme1n1       60G disk           ← data EBS, chưa mount
```

> **Nếu thấy `xvdb` thay vì `nvme1n1`:** thay thế `nvme1n1` bằng `xvdb` trong các lệnh dưới

### 2.3 Format và mount EBS data disk

```bash
# Format ext4 (chỉ làm 1 lần - sẽ xóa toàn bộ dữ liệu trên disk)
sudo mkfs.ext4 /dev/nvme1n1

# Tạo mount point
sudo mkdir /data

# Mount
sudo mount /dev/nvme1n1 /data

# Auto-mount khi reboot
echo '/dev/nvme1n1 /data ext4 defaults,nofail 0 2' | sudo tee -a /etc/fstab

# Set ownership
sudo chown -R ubuntu:ubuntu /data
```

✅ **Verify:**
```bash
df -h /data
# Kết quả: /dev/nvme1n1     59G   24K   56G   1%   /data
```

### 2.4 Thêm swap (2GB — bắt buộc với t3.medium)

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

✅ **Verify:**
```bash
free -h
# Swap: phải thấy "2.0Gi" thay vì "0B"
```

### 2.5 Cài Docker CE (KHÔNG dùng Ubuntu default repo)

> Ubuntu 24.04 không có `docker-compose-plugin` trong default repo → phải thêm Docker official repo

```bash
# Thêm Docker official repo
sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update

# Cài Docker CE + compose plugin
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Thêm user ubuntu vào group docker (không cần sudo mỗi lần)
sudo usermod -aG docker ubuntu
```

**Logout và SSH lại** để group có hiệu lực:
```bash
exit
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136
```

✅ **Verify:**
```bash
docker --version
# Docker version 27.x.x, build ...

docker compose version
# Docker Compose version v2.x.x
```

### 2.6 Move Docker data-root sang EBS (TRƯỚC khi pull image)

> Quan trọng: Nếu không làm bước này, Docker lưu toàn bộ images/volumes trên root disk 30GB

```bash
sudo systemctl stop docker

sudo mkdir -p /data/docker
echo '{"data-root": "/data/docker"}' | sudo tee /etc/docker/daemon.json

# Sync data cũ sang EBS
sudo rsync -aP /var/lib/docker/ /data/docker/

sudo systemctl start docker
```

✅ **Verify:**
```bash
docker info | grep "Docker Root Dir"
# Phải thấy: Docker Root Dir: /data/docker
```

---

## Stage 3 — Cấu hình ứng dụng

### 3.1 Tạo thư mục và file `.env`

```bash
mkdir -p ~/notesnook
nano ~/notesnook/.env
```

Nội dung `.env` (thay các giá trị theo môi trường của bạn):

```env
INSTANCE_NAME=self-hosted-notesnook-instance

# Secret >= 32 ký tự ngẫu nhiên
NOTESNOOK_API_SECRET=9e3f4047804e4a2a90623eec54f90013396d401bee73e626312423576307f355

DISABLE_SIGNUPS=false

# SMTP — Gmail App Password (16 ký tự, không có dấu cách)
SMTP_USERNAME=your.gmail@gmail.com
SMTP_PASSWORD=abcdefghijklmnop
SMTP_HOST=smtp.gmail.com
SMTP_PORT=465
# 465 = SMTPS implicit TLS (đang dùng trên production)
# 587 = STARTTLS — cũng hợp lệ với Gmail nếu app dùng STARTTLS

# Twilio (tùy chọn — SMS 2FA)
TWILIO_ACCOUNT_SID=
TWILIO_AUTH_TOKEN=
TWILIO_SERVICE_SID=

NOTESNOOK_CORS_ORIGINS=
KNOWN_PROXIES=

# URLs — dùng sau khi Stage 4 (Nginx + HTTPS) hoàn thành
NOTESNOOK_APP_PUBLIC_URL=https://sync.54-254-2-136.sslip.io
MONOGRAPH_PUBLIC_URL=https://mono.54-254-2-136.sslip.io
AUTH_SERVER_PUBLIC_URL=https://auth.54-254-2-136.sslip.io
ATTACHMENTS_SERVER_PUBLIC_URL=https://minio.54-254-2-136.sslip.io

# BẮT BUỘC — thiếu sẽ fail validate service
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# MinIO credentials
MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin123
```

Lưu: `Ctrl+O` → Enter → `Ctrl+X`

> **Lưu ý:** `MONOGRAPH_PUBLIC_URL` BẮT BUỘC phải có — docker-compose validate sẽ exit 1 nếu thiếu

### 3.2 Clone docker-compose files

```bash
cd ~/notesnook

# Clone từ repo (hoặc scp từ máy local)
# Option 1: Clone repo
git clone https://github.com/your-username/notesnook-sync-server.git /tmp/notesnook-src
cp /tmp/notesnook-src/docker-compose.yml ~/notesnook/
cp /tmp/notesnook-src/docker-compose.prod.yml ~/notesnook/

# Option 2: scp từ Mac (chạy trên Mac)
# scp -i ~/Downloads/notesnook-mac-key.pem docker-compose.yml docker-compose.prod.yml ubuntu@54.254.2.136:~/notesnook/
```

✅ **Verify:**
```bash
ls ~/notesnook/
# phải thấy: docker-compose.yml  docker-compose.prod.yml  .env
```

---

## Stage 4 — Nginx + HTTPS Setup

> **Thứ tự quan trọng:** Phải để nginx chạy được trên HTTP trước, sau đó certbot mới lấy cert và tự thêm SSL vào config. KHÔNG viết SSL config trước khi có cert — nginx sẽ crash vì cert files chưa tồn tại.

### 4.1 Cài Nginx và Certbot

```bash
sudo apt install -y nginx certbot python3-certbot-nginx
```

### 4.2 Tạo cấu hình Nginx tạm thời (HTTP-only)

> Bước này chỉ dùng HTTP — certbot sẽ tự thêm SSL vào bước 4.4.

```bash
sudo nano /etc/nginx/sites-enabled/default
```

Thay toàn bộ nội dung bằng:

```nginx
# Notesnook API (sync)
server {
    listen 80;
    server_name sync.54-254-2-136.sslip.io;
    location / {
        proxy_pass http://localhost:5264;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# Identity Server (auth)
server {
    listen 80;
    server_name auth.54-254-2-136.sslip.io;
    location / {
        proxy_pass http://localhost:8264;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# SSE Messenger (events)
server {
    listen 80;
    server_name events.54-254-2-136.sslip.io;
    location / {
        proxy_pass http://localhost:7264;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# Monograph (mono) — disabled
server {
    listen 80;
    server_name mono.54-254-2-136.sslip.io;
    location / { return 503; }
}

# MinIO S3
server {
    listen 80;
    server_name minio.54-254-2-136.sslip.io;
    client_max_body_size 100M;
    location / {
        proxy_pass http://localhost:9000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Lưu: `Ctrl+O` → Enter → `Ctrl+X`

### 4.3 Verify nginx chạy được với HTTP config

```bash
sudo nginx -t && sudo systemctl reload nginx
```

✅ **Kết quả phải thấy:**
```
nginx: the configuration file /etc/nginx/nginx.conf syntax is ok
nginx: configuration file /etc/nginx/nginx.conf test is successful
```

> Nếu thấy lỗi syntax → kiểm tra lại file config ở bước 4.2 trước khi tiếp tục.

### 4.4 Lấy SSL Certificate (certbot tự thêm SSL vào nginx)

> Certbot đọc `server_name` từ nginx config, xác thực qua port 80, lấy cert, rồi tự sửa nginx config thêm `listen 443 ssl` và các SSL directives.

```bash
# Cert cho sync, auth, events, mono (1 cert chung — multi-domain)
sudo certbot --nginx \
  -d sync.54-254-2-136.sslip.io \
  -d auth.54-254-2-136.sslip.io \
  -d events.54-254-2-136.sslip.io \
  -d mono.54-254-2-136.sslip.io \
  --non-interactive --agree-tos -m your.gmail@gmail.com

# Cert riêng cho minio (cần cert separate)
sudo certbot --nginx \
  -d minio.54-254-2-136.sslip.io \
  --non-interactive --agree-tos -m your.gmail@gmail.com
```

✅ **Verify cert đã có:**
```bash
sudo certbot certificates
```

Kết quả mong đợi:
```
Certificate Name: sync.54-254-2-136.sslip.io
  Domains: sync.54-254-2-136.sslip.io auth.54-254-2-136.sslip.io events.54-254-2-136.sslip.io mono.54-254-2-136.sslip.io
  Expiry Date: 2026-XX-XX (VALID: 89 days)

Certificate Name: minio.54-254-2-136.sslip.io
  Domains: minio.54-254-2-136.sslip.io
  Expiry Date: 2026-XX-XX (VALID: 89 days)
```

### 4.5 Thêm SSE headers vào events block

Sau khi certbot chạy, nó đã tự sửa nginx config thêm SSL. Bây giờ cần thêm SSE-specific headers vào block `events` (certbot không tự thêm):

```bash
sudo nano /etc/nginx/sites-enabled/default
```

Tìm block có `server_name events.54-254-2-136.sslip.io;` → vào bên trong `location /` → thêm 4 dòng sau vào cuối location block:

```nginx
        # SSE SPECIFIC — bắt buộc, không có sẽ pending mãi
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 86400s;
        proxy_set_header X-Accel-Buffering no;
```

Kết quả events block sau khi thêm:
```nginx
server {
    server_name events.54-254-2-136.sslip.io;
    location / {
        proxy_pass http://localhost:7264;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # SSE SPECIFIC — bắt buộc, không có sẽ pending mãi
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 86400s;
        proxy_set_header X-Accel-Buffering no;
    }
    listen 443 ssl; # managed by Certbot
    ...
}
```

Lưu và reload:
```bash
sudo nginx -t && sudo systemctl reload nginx
```

### 4.6 Đóng ports trực tiếp trong Security Group

Vào **AWS Console → EC2 → Security Groups → sg-xxxxxxxx → Inbound rules → Edit**

Xóa các rules nếu đã thêm trước đó:
- ~~5264, 8264, 7264, 6264, 9000~~ (không cần — Nginx proxy nội bộ)

Giữ lại:
| Port | Source | Mục đích |
|------|--------|----------|
| 22   | My IP/32 | SSH |
| 80   | 0.0.0.0/0 | certbot renew + redirect |
| 443  | 0.0.0.0/0 | HTTPS |

---

## Stage 5 — GitHub CI/CD Setup

### 5.1 Tạo Docker Hub Access Token

1. Đăng nhập **hub.docker.com** → Account Settings → Security → **New Access Token**
2. Token name: `github-actions-notesnook`
3. Copy token (chỉ hiện 1 lần)

### 5.2 Thêm GitHub Secrets

Vào: **GitHub repo → Settings → Secrets and variables → Actions → New repository secret**

| Secret | Giá trị |
|--------|---------|
| `DOCKER_USERNAME` | Docker Hub username (vd: `vincentephan`) |
| `DOCKER_PASSWORD` | Docker Hub Access Token vừa tạo |
| `EC2_HOST` | `54.254.2.136` |
| `EC2_USERNAME` | `ubuntu` |
| `EC2_SSH_KEY` | Toàn bộ nội dung file `notesnook-mac-key.pem` (kể cả dòng `-----BEGIN RSA PRIVATE KEY-----`) |

✅ **Verify:** 5 secrets đều hiện status "Updated X minutes ago"

### 5.3 Kiểm tra GitHub Actions workflow

File `.github/workflows/deploy.yml` phải có:

```yaml
on:
  push:
    branches: [main]    # ← phải là "main", không phải "master"
```

Nếu local branch là `master`, đổi sang `main`:
```bash
# Trên máy local
git branch -m master main
git push origin main
```

---

## Stage 6 — Trigger Deploy Lần Đầu

### 6.1 Push code lên main

```bash
# Trên máy local
git add .
git commit -m "ci: trigger first deploy"
git push origin main
```

### 6.2 Theo dõi GitHub Actions

GitHub repo → **Actions** → xem workflow đang chạy

Quy trình tự động:
```
Push main
  ↓ (~30 giây)
Build 3 Docker images (linux/amd64):
  Notesnook.API/Dockerfile        → vincentephan/notesnook-sync:latest
  Streetwriters.Identity/Dockerfile → vincentephan/identity:latest
  Streetwriters.Messenger/Dockerfile → vincentephan/sse:latest
  ↓ (~5-10 phút, tùy kích thước image)
Push lên Docker Hub
  ↓ (~2 phút)
SSH EC2:
  docker compose pull
  docker compose up -d --remove-orphans
  ↓ (~2 phút)
Done ✅
```

### 6.3 SSH vào EC2 kiểm tra containers

```bash
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136

# Xem tất cả containers
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml ps
```

Kết quả mong đợi:
```
NAME                               STATUS
notesnook-notesnook-server-1       Up (healthy)
notesnook-identity-server-1        Up (healthy)
notesnook-sse-server-1             Up (healthy)
notesnook-notesnook-s3-1           Up (healthy)
notesnook-notesnook-db-1           Up (healthy)
notesnook-notesnook-redis-1        Up (healthy)
notesnook-autoheal-1               Up (healthy)
```

> Docker đặt tên container theo format `{project}-{service}-{index}`. Project name = `notesnook` (tên thư mục `~/notesnook/`). Ví dụ service `notesnook-server` → container `notesnook-notesnook-server-1`.

> `monograph-server` sẽ KHÔNG xuất hiện (đã disabled trong `docker-compose.prod.yml`)

---

## Stage 7 — Verification Đầy Đủ

### 7.1 Health check tất cả endpoints

```bash
# Notesnook API
curl -s https://sync.54-254-2-136.sslip.io/health
# Mong đợi: {"status":"Healthy"} hoặc 200 OK

# Identity Server
curl -s https://auth.54-254-2-136.sslip.io/health
# Mong đợi: 200 OK

# SSE Messenger
curl -s https://events.54-254-2-136.sslip.io/health
# Mong đợi: 200 OK

# MinIO (S3)
curl -s https://minio.54-254-2-136.sslip.io/minio/health/live
# Mong đợi: 200 OK (empty body)
```

### 7.2 Kiểm tra OIDC — BẮT BUỘC

```bash
# Kiểm tra issuer phải là HTTPS (không phải http://)
curl -s https://auth.54-254-2-136.sslip.io/.well-known/openid-configuration | grep -o '"issuer":"[^"]*"'
# Mong đợi: "issuer":"https://auth.54-254-2-136.sslip.io"
#           ← PHẢI là https://, không được là http://

# Kiểm tra jwks_uri cũng phải HTTPS
curl -s https://auth.54-254-2-136.sslip.io/.well-known/openid-configuration | grep -o '"jwks_uri":"[^"]*"'
# Mong đợi: "jwks_uri":"https://auth.54-254-2-136.sslip.io/.well-known/openid-configuration/jwks"
```

> **Nếu thấy `http://`:** thiếu `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` trong `.env`
> Fix: thêm biến vào `.env` rồi `docker compose up -d --force-recreate identity-server notesnook-server`

### 7.3 Kiểm tra logs không có lỗi

```bash
# Logs ngắn gọn (50 dòng cuối)
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs --tail=50

# Logs của service cụ thể
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs identity-server --tail=50
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs notesnook-server --tail=50
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs sse-server --tail=50
```

### 7.4 Kiểm tra server stats tổng hợp

```bash
echo "=== CONTAINERS ===" && \
  docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml ps && \
  echo "=== DISK ===" && df -h && \
  echo "=== MEMORY ===" && free -h && \
  echo "=== UPTIME ===" && uptime
```

Ngưỡng bình thường:
| Metric | Ngưỡng OK | Cảnh báo |
|--------|-----------|----------|
| Disk (root `/`) | < 70% | > 80% |
| Disk (data `/data`) | < 70% | > 80% |
| RAM used | < 80% | > 90% |
| Load average | < 2.0 (2 vCPU) | > 2.0 |

### 7.5 Kiểm tra certbot auto-renew

```bash
sudo certbot renew --dry-run
```

✅ **Kết quả phải thấy:**
```
Congratulations, all simulated renewals succeeded:
  /etc/letsencrypt/live/sync.54-254-2-136.sslip.io/fullchain.pem (success)
  /etc/letsencrypt/live/minio.54-254-2-136.sslip.io/fullchain.pem (success)
```

> `--dry-run` chỉ test, không renew thật. Nếu fail ở đây thì auto-renew (chạy 2 lần/ngày qua systemd timer) cũng sẽ fail → cert hết hạn sau 90 ngày.

### 7.6 Test kết nối từ Notesnook Desktop App

1. Mở **Notesnook Desktop** (app.notesnook.com hoặc desktop app)
2. Vào Settings → Self-host
3. Điền:
   ```
   Sync Server:  https://sync.54-254-2-136.sslip.io
   Auth Server:  https://auth.54-254-2-136.sslip.io
   SSE Server:   https://events.54-254-2-136.sslip.io
   ```
4. Click "Connect" → Đăng ký tài khoản mới

---

## Stage 8 — Operations (Vận hành thường ngày)

### Deploy khi có code mới

```bash
# Chỉ cần push lên main — GitHub Actions tự lo
git push origin main

# Theo dõi trên GitHub Actions tab
```

### Restart service khi cần

```bash
# SSH vào EC2 trước
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136

# Restart 1 service cụ thể
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d notesnook-server

# Restart toàn bộ stack (downtime ~30s)
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d --force-recreate

# Pull images mới nhất và restart
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml pull
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d --remove-orphans
```

### Sửa `.env` và apply

```bash
nano ~/notesnook/.env
# Sửa xong → Ctrl+O → Enter → Ctrl+X

# Recreate containers để đọc env mới
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d --force-recreate
```

### Reload Nginx sau khi sửa config

```bash
sudo nano /etc/nginx/sites-enabled/default
# Sửa xong →

sudo nginx -t && sudo systemctl reload nginx
# nginx -t kiểm tra syntax trước, nếu OK mới reload
```

### Kiểm tra SSL cert còn hạn

```bash
sudo certbot certificates
# Auto-renew chạy tự động qua systemd timer
# Renew thủ công (nếu cần):
# sudo certbot renew --dry-run   ← test
# sudo certbot renew             ← thật
```

---

## Troubleshooting nhanh

| Triệu chứng | Lệnh kiểm tra | Fix |
|-------------|---------------|-----|
| Container không healthy | `docker compose logs <name> --tail=50` | Xem lỗi cụ thể |
| HTTPS không hoạt động | `sudo nginx -t` | Fix nginx config |
| App không kết nối được | `curl https://sync.*/health` | Kiểm tra health endpoints |
| `issuer` trả về `http://` | xem `.env` | Thêm `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` |
| SSE pending mãi | xem nginx events block | Thêm `proxy_buffering off` |
| Disk đầy | `df -h` | Dọn docker: `docker system prune -f` |
| RAM đầy | `free -h` | Kiểm tra container nào dùng nhiều: `docker stats` |
| GitHub Actions fail | Actions tab → xem log | Kiểm tra secrets đúng chưa |
| `exec format error` | container logs | Build lại với `--platform linux/amd64` |

---

## Checklist Deploy Từ Đầu

```
Stage 1 — AWS Infrastructure
[ ] EC2 t3.medium Ubuntu 24.04 đã tạo
[ ] Key pair .pem đã download về ~/Downloads/
[ ] Elastic IP đã gán, SSH được vào server
[ ] Security Group: chỉ mở 22/80/443

Stage 2 — Server Setup (trên EC2)
[ ] EBS 60GB đã format và mount /data
[ ] Swap 2GB đã thêm
[ ] Docker CE đã cài từ official repo
[ ] Docker data-root đã move sang /data/docker

Stage 3 — App Config
[ ] ~/notesnook/.env đã tạo với đủ biến
[ ] ASPNETCORE_FORWARDEDHEADERS_ENABLED=true có trong .env
[ ] MONOGRAPH_PUBLIC_URL có trong .env
[ ] docker-compose.yml và docker-compose.prod.yml đã có

Stage 4 — Nginx + HTTPS
[ ] Nginx đã cài
[ ] HTTP-only config (5 server blocks) đã viết và nginx -t OK
[ ] certbot --nginx đã chạy thành công (sync cert + minio cert riêng)
[ ] SSE block đã thêm proxy_buffering off (sau certbot)
[ ] nginx -t OK, systemctl reload OK
[ ] certbot renew --dry-run thành công
[ ] Ports 5264/8264/7264/9000 đã đóng trong Security Group

Stage 5 — CI/CD
[ ] Docker Hub Access Token đã tạo
[ ] 5 GitHub Secrets đã thêm
[ ] deploy.yml trigger branch: [main]
[ ] Local branch đã đổi sang main

Stage 6 — Deploy
[ ] git push origin main → GitHub Actions chạy OK
[ ] Tất cả containers status: Up (healthy)

Stage 7 — Verification
[ ] curl /health cho sync, auth, events, minio → 200
[ ] OIDC issuer trả về https:// (không phải http://)
[ ] jwks_uri trả về https://
[ ] Logs không có error
[ ] certbot renew --dry-run thành công
[ ] Notesnook desktop app kết nối và login được
```