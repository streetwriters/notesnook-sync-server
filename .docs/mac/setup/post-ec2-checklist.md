# Post EC2 — Checklist Việc Cần Làm

> EC2 đã có (ip: 54.254.2.136), SSH key: `~/Downloads/notesnook-mac-key.pem`

---

## Trên EC2 (SSH vào làm 1 lần)

### Bước 1 — Mount data disk (nvme1n1, 60GB) và move Docker data-root

```bash
# Format và mount EBS
sudo mkfs.ext4 /dev/nvme1n1
sudo mkdir /data
sudo mount /dev/nvme1n1 /data
echo '/dev/nvme1n1 /data ext4 defaults,nofail 0 2' | sudo tee -a /etc/fstab
sudo chown -R ubuntu:ubuntu /data
```

> **Lưu ý:** Bước move Docker data-root sang `/data` phải làm SAU khi cài Docker (Bước 3).

### Bước 2 — Thêm swap (2GB)

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### Bước 3 — Cài Docker

> `docker-compose-plugin` không có trong Ubuntu default repos — phải thêm Docker official repo.

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

# Thêm user vào group docker
sudo usermod -aG docker ubuntu
# Logout rồi SSH lại để group có hiệu lực
```

Verify sau khi SSH lại:

```bash
docker --version
docker compose version
```

### Bước 3b — Move Docker data-root sang EBS

> Làm ngay sau khi cài Docker, TRƯỚC khi pull bất kỳ image nào.

```bash
sudo systemctl stop docker
sudo mkdir -p /data/docker
echo '{"data-root": "/data/docker"}' | sudo tee /etc/docker/daemon.json
sudo rsync -aP /var/lib/docker/ /data/docker/
sudo systemctl start docker

# Verify Docker đang dùng EBS
docker info | grep "Docker Root Dir"
# Kết quả phải là: Docker Root Dir: /data/docker
```

Từ giờ toàn bộ images, containers, volumes đều lưu trên EBS 60GB.

### Bước 4 — Tạo file `.env`

```bash
mkdir ~/notesnook
nano ~/notesnook/.env
```

Nội dung `.env`:

```env
INSTANCE_NAME=self-hosted-notesnook-instance
NOTESNOOK_API_SECRET=9e3f4047804e4a2a90623eec54f90013396d401bee73e626312423576307f355
DISABLE_SIGNUPS=false

SMTP_USERNAME=edricphan.dev@gmail.com
SMTP_PASSWORD=roqsgxxcdymxqyjd
SMTP_HOST=smtp.gmail.com
SMTP_PORT=465

TWILIO_ACCOUNT_SID=
TWILIO_AUTH_TOKEN=
TWILIO_SERVICE_SID=

NOTESNOOK_CORS_ORIGINS=
KNOWN_PROXIES=

NOTESNOOK_APP_PUBLIC_URL=http://54.254.2.136:5264
MONOGRAPH_PUBLIC_URL=http://54.254.2.136:6264
AUTH_SERVER_PUBLIC_URL=http://54.254.2.136:8264
ATTACHMENTS_SERVER_PUBLIC_URL=http://54.254.2.136:9000

MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin123
```

Lưu: `Ctrl+O` → Enter → `Ctrl+X`.

> **MONOGRAPH_PUBLIC_URL bắt buộc phải có** — service `validate` trong docker-compose sẽ fail nếu thiếu biến này.

---

## Trên AWS Console (làm 1 lần)

### Bước 5 — Mở ports cho app trong Security Group

Vào: **AWS Console → EC2 → Security Groups → `sg-03b792f91c8684847` → Inbound rules → Edit inbound rules → Add rule**

| Type | Port | Source | Service |
|---|---|---|---|
| Custom TCP | 5264 | 0.0.0.0/0 | notesnook-server |
| Custom TCP | 8264 | 0.0.0.0/0 | identity-server |
| Custom TCP | 7264 | 0.0.0.0/0 | sse-server |
| Custom TCP | 6264 | 0.0.0.0/0 | monograph-server |
| Custom TCP | 9000 | 0.0.0.0/0 | MinIO |

> Không mở ports này thì app chạy trên EC2 nhưng client bên ngoài không kết nối được.

---

## Trên GitHub (làm 1 lần)

### Bước 5 — Thêm 5 GitHub Secrets

Vào: **repo → Settings → Secrets and variables → Actions → New repository secret**

| Secret | Giá trị |
|---|---|
| `DOCKER_USERNAME` | `vincentephan` |
| `DOCKER_PASSWORD` | Access Token Docker Hub |
| `EC2_HOST` | `54.254.2.136` |
| `EC2_USERNAME` | `ubuntu` |
| `EC2_SSH_KEY` | Nội dung file `notesnook-mac-key.pem` (kể cả `-----BEGIN...`) |

---

## Trigger deploy lần đầu

### Bước 6 — Commit 2 files + push lên `main`

```bash
# Đổi branch local sang main (làm 1 lần)
git branch -m master main

git add .github/workflows/deploy.yml docker-compose.prod.yml
git commit -m "ci: add deploy workflow and prod compose override"
git push origin main
```

→ GitHub Actions tự chạy: build images → push Docker Hub → SSH EC2 → up containers.

---

## Kiểm tra sau deploy

```bash
# SSH vào EC2
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136

# Xem containers đang chạy
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml ps

# Xem logs nếu có lỗi
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs --tail=50
```
