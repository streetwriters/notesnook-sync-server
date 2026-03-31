# Deploy Bugs & Fixes — Notesnook Self-Hosted EC2

> Session: 2026-03-31 → 2026-04-01
> EC2: Ubuntu 24.04, ip 54.254.2.136, instance i-02672bfe496225644

---

## Bug 1 — `docker-compose-plugin` không có trong Ubuntu default repos

**Lỗi:**
```
E: Unable to locate package docker-compose-plugin
```

**Nguyên nhân:** Package này chỉ có trong Docker official repo, không có trong Ubuntu 24.04 default.

**Fix:** Thêm Docker official repo trước khi cài:
```bash
sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

---

## Bug 2 — EBS volume không được Docker dùng

**Nguyên nhân:** `docker-compose.yml` dùng named volumes → Docker lưu tại `/var/lib/docker/volumes/` trên root disk (50GB), không phải EBS 60GB.

**Fix:** Move Docker data-root sang EBS sau khi cài Docker, TRƯỚC khi pull image:
```bash
sudo systemctl stop docker
sudo mkdir -p /data/docker
echo '{"data-root": "/data/docker"}' | sudo tee /etc/docker/daemon.json
sudo rsync -aP /var/lib/docker/ /data/docker/
sudo systemctl start docker
docker info | grep "Docker Root Dir"  # phải là /data/docker
```

---

## Bug 3 — `MONOGRAPH_PUBLIC_URL` thiếu trong `.env`

**Lỗi:** Stack không khởi động, validate service exit 1.

**Nguyên nhân:** `docker-compose.yml` validate service check biến này là required nhưng `.env` template không có.

**Fix:** Thêm vào `.env`:
```env
MONOGRAPH_PUBLIC_URL=http://54.254.2.136:6264
```

---

## Bug 4 — CI/CD workflow trigger sai branch

**Nguyên nhân:** `deploy.yml` trigger `branches: [master]` nhưng repo dùng `main`.

**Fix:** Sửa `deploy.yml`:
```yaml
on:
  push:
    branches: [main]
```
Và đổi local branch:
```bash
git branch -m master main
git push origin main
```

---

## Bug 5 — Security Group chưa mở ports cho app

**Nguyên nhân:** Mặc định Security Group chỉ mở port 22, 80, 443.

**Fix:** Mở thêm trong AWS Console → EC2 → Security Groups:

| Port | Service |
|---|---|
| 5264 | notesnook-server |
| 8264 | identity-server |
| 7264 | sse-server |
| 6264 | monograph-server |
| 9000 | MinIO |
| 80 | HTTP (Let's Encrypt ACME challenge) |
| 443 | HTTPS |

---

## Bug 6 — Notesnook desktop app không kết nối được dù server healthy

**Lỗi:** "Could not connect to Sync server."

**Nguyên nhân:** Notesnook desktop app thực ra load `https://app.notesnook.com` bên trong → Mixed Content policy block HTTP requests từ HTTPS page.

**Fix:** Setup HTTPS qua sslip.io + nginx + Let's Encrypt:
```bash
# Lấy SSL cert (port 80 phải mở trước)
sudo certbot --nginx \
  -d sync.54-254-2-136.sslip.io \
  -d auth.54-254-2-136.sslip.io \
  -d events.54-254-2-136.sslip.io \
  -d mono.54-254-2-136.sslip.io \
  --non-interactive --agree-tos -m edricphan.dev@gmail.com
```

Nginx config (`/etc/nginx/sites-enabled/default`):
```nginx
server {
    listen 443 ssl;
    server_name sync.54-254-2-136.sslip.io;
    ssl_certificate /etc/letsencrypt/live/sync.54-254-2-136.sslip.io/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/sync.54-254-2-136.sslip.io/privkey.pem;
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
# (tương tự cho auth:8264, events:7264, mono:6264)
```

URLs trong `.env` sau khi setup HTTPS:
```env
AUTH_SERVER_PUBLIC_URL=https://auth.54-254-2-136.sslip.io
NOTESNOOK_APP_PUBLIC_URL=https://sync.54-254-2-136.sslip.io
MONOGRAPH_PUBLIC_URL=https://mono.54-254-2-136.sslip.io
ATTACHMENTS_SERVER_PUBLIC_URL=https://sync.54-254-2-136.sslip.io/s3
```

---

## Bug 7 — IssuerUri dùng internal Docker hostname

**Lỗi:**
```
Policy error: Issuer name does not match authority: https://auth.54-254-2-136.sslip.io/
```

**Nguyên nhân:** `Streetwriters.Identity/Startup.cs` dòng 119:
```csharp
options.IssuerUri = Servers.IdentityServer.ToString();
// ToString() → "http://identity-server:8264" (Docker internal)
```

**Fix:** Sửa thành dùng `PublicURL`:
```csharp
options.IssuerUri = Servers.IdentityServer.PublicURL?.ToString() ?? Servers.IdentityServer.ToString();
```

**File:** `Streetwriters.Identity/Startup.cs` line 119

---

## Bug 8 — exec format error (wrong architecture)

**Lỗi:**
```
exec ./Streetwriters.Identity: exec format error
```

**Nguyên nhân:** Build Docker image trên Mac Apple Silicon (ARM64) nhưng EC2 chạy AMD64.

**Fix:** Build với `--platform linux/amd64`:
```bash
docker buildx build --platform linux/amd64 \
  -f Streetwriters.Identity/Dockerfile \
  -t vincentephan/identity:latest \
  --push .
```

> Lý do cần CI/CD: GitHub Actions chạy `ubuntu-latest` (AMD64) tự động đúng architecture.

---

## Bug 9 — OAuth2 Introspection authority dùng internal hostname

**Lỗi:**
```
Policy error while contacting http://identity-server:8264:
Issuer name does not match authority: https://auth.54-254-2-136.sslip.io/
```

**Nguyên nhân:** `Notesnook.API/Startup.cs` dòng 130:
```csharp
options.Authority = Servers.IdentityServer.ToString();
// → "http://identity-server:8264"
```

**Fix:**
```csharp
options.Authority = Servers.IdentityServer.PublicURL?.ToString() ?? Servers.IdentityServer.ToString();
```

**File:** `Notesnook.API/Startup.cs` line 130

---

## Bug 10 — `jwks_uri` generate HTTP thay vì HTTPS

**Lỗi:**
```
Endpoint belongs to different authority:
http://auth.54-254-2-136.sslip.io/.well-known/openid-configuration/jwks
```

**Nguyên nhân:** Identity server chạy sau nginx (HTTPS termination) nhưng không biết scheme thực là HTTPS → generate `jwks_uri` với `http://`.

**Fix:** Thêm vào `.env` trên EC2 (không cần rebuild):
```env
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

Sau đó restart:
```bash
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d --force-recreate identity-server notesnook-server
```

---

## Lệnh hay dùng

### Kiểm tra containers
```bash
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml ps
```

### Xem logs service cụ thể
```bash
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml logs <service-name> --tail=50
```

### Restart 1 service
```bash
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d <service-name>
```

### Restart toàn bộ stack
```bash
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d --force-recreate
```

### Test health endpoints
```bash
curl https://sync.54-254-2-136.sslip.io/health
curl https://auth.54-254-2-136.sslip.io/health
curl https://events.54-254-2-136.sslip.io/health
```

### Kiểm tra OIDC issuer
```bash
curl -s https://auth.54-254-2-136.sslip.io/.well-known/openid-configuration | grep -o '"issuer":"[^"]*"'
curl -s https://auth.54-254-2-136.sslip.io/.well-known/openid-configuration | grep -o '"jwks_uri":"[^"]*"'
```

### SSH vào EC2
```bash
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136
```

### Build image đúng architecture cho EC2
```bash
docker buildx build --platform linux/amd64 \
  -f <Dockerfile-path> \
  -t vincentephan/<image-name>:latest \
  --push .
```

### Reload nginx
```bash
sudo nginx -t && sudo systemctl reload nginx
```

---

## Bug 11 — SSE server cùng lỗi OAuth2 Introspection authority

**Lỗi:**
```
Policy error while contacting http://identity-server:8264:
Issuer name does not match authority: https://auth.54-254-2-136.sslip.io/
```

**Nguyên nhân:** `Streetwriters.Messenger/Startup.cs` dòng 75 có cùng pattern với Bug 9:
```csharp
options.Authority = Servers.IdentityServer.ToString();
// → "http://identity-server:8264"
```

**Fix:**
```csharp
options.Authority = Servers.IdentityServer.PublicURL?.ToString() ?? Servers.IdentityServer.ToString();
```

**File:** `Streetwriters.Messenger/Startup.cs` line 75

Rebuild và redeploy:
```bash
# Trên Mac
docker buildx build --platform linux/amd64 \
  -f Streetwriters.Messenger/Dockerfile \
  -t vincentephan/sse:latest \
  --push .

# Trên EC2
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml pull sse-server
docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml up -d sse-server
```

---

## Bug 12 — SSE connection bị nginx buffer → pending mãi không nhận được events

**Lỗi:** SSE connection ở trạng thái pending, không nhận được events real-time.

**Nguyên nhân:** Nginx mặc định buffer response → SSE events bị giữ lại, không stream được đến client.

**Fix:** Thêm SSE-specific settings vào nginx block của `events.54-254-2-136.sslip.io`:

```nginx
location / {
    proxy_pass http://localhost:7264;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    # SSE specific
    proxy_buffering off;
    proxy_cache off;
    proxy_read_timeout 86400s;
    proxy_set_header X-Accel-Buffering no;
}
```

```bash
sudo nginx -t && sudo systemctl reload nginx
```

> **Lưu ý:** SSE connection hiện "pending" trong Network tab là **bình thường** — đây là long-lived connection chờ events. Chỉ lo khi thấy status code lỗi (4xx/5xx).

---


```bash
echo "=== CONTAINERS ===" && docker compose -f ~/notesnook/docker-compose.yml -f ~/notesnook/docker-compose.prod.yml ps && \
  echo "=== DISK ===" && df -h && \
  echo "=== MEMORY ===" && free -h && \
  echo "=== UPTIME ===" && uptime
```