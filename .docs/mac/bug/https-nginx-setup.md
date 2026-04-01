# HTTPS + Nginx Setup — Q&A Log

> EC2: `54.254.2.136`, domain: `*.54-254-2-136.sslip.io`

---

## 1. Đóng HTTP sau khi bật HTTPS không?

- **Port 80**: GIỮ mở — cần cho certbot auto-renew và redirect HTTP → HTTPS.
- **Port 5264, 8264, 7264**: ĐÓNG — Nginx proxy nội bộ rồi, không cần expose.
- Lý do vẫn kết nối được sau khi đóng: Nginx nói chuyện nội bộ qua `localhost`, Security Group không chặn traffic nội bộ.

---

## 2. Setup Nginx reverse proxy

Dùng [sslip.io](https://sslip.io) thay domain (không cần mua domain):

| Subdomain | Service | Port nội bộ |
|---|---|---|
| `sync.54-254-2-136.sslip.io` | Notesnook API | 5264 |
| `auth.54-254-2-136.sslip.io` | Identity | 8264 |
| `events.54-254-2-136.sslip.io` | SSE Messenger | 7264 |
| `mono.54-254-2-136.sslip.io` | Monograph | 6264 |
| `minio.54-254-2-136.sslip.io` | MinIO | 9000 |

Cài Nginx + certbot:

```bash
sudo apt install -y nginx certbot python3-certbot-nginx
```

Lấy cert:

```bash
sudo certbot --nginx -d sync.54-254-2-136.sslip.io \
                     -d auth.54-254-2-136.sslip.io \
                     -d events.54-254-2-136.sslip.io \
                     -d mono.54-254-2-136.sslip.io

sudo certbot --nginx -d minio.54-254-2-136.sslip.io
```

> **Lưu ý:** `minio` cần cert riêng vì certbot tạo cert separate. Phải update Nginx block minio dùng đúng cert path:
> `/etc/letsencrypt/live/minio.54-254-2-136.sslip.io/fullchain.pem`

---

## 3. MinIO không truy cập được

**Nguyên nhân:** Nginx block minio đang dùng cert của `sync`, nhưng certbot tạo cert riêng cho `minio`.

**Fix:** Sửa cert path trong Nginx block minio:

```nginx
ssl_certificate /etc/letsencrypt/live/minio.54-254-2-136.sslip.io/fullchain.pem;
ssl_certificate_key /etc/letsencrypt/live/minio.54-254-2-136.sslip.io/privkey.pem;
```

```bash
sudo nginx -t && sudo systemctl reload nginx
```

Cập nhật `.env`:

```env
ATTACHMENTS_SERVER_PUBLIC_URL=https://minio.54-254-2-136.sslip.io
```

---

## 4. Events (port 7264) — Connection refused

**Phát hiện từ log:** `connect() failed (111: Connection refused) ... upstream: "http://127.0.0.1:7264/sse"`

SSE server không chạy. Fix: restart docker compose.

---

## 5. MongoDB (27017) và Redis (6379) có nguy hiểm không?

Docker bind `0.0.0.0:27017` và `0.0.0.0:6379` nhưng **Security Group không mở 2 port này** → an toàn, không cần làm gì thêm.

---

## 6. Monograph server — tắt vì không dùng

**Monograph** là tính năng publish note ra public URL. Không dùng → tắt.

**Cách tắt** (không sửa file gốc): thêm vào `docker-compose.prod.yml`:

```yaml
  monograph-server:
    profiles:
      - disabled
```

**Lưu ý:** `profiles` chỉ ngăn start mới, không tự stop container cũ. Phải stop thủ công 1 lần:

```bash
docker stop notesnook-monograph-server-1
docker rm notesnook-monograph-server-1
```

Sau đó không bao giờ start lại nữa kể cả sau deploy.

---

## 7. Cert coverage — kiểm tra

```bash
sudo certbot certificates
```

Kết quả đúng:
- `minio.54-254-2-136.sslip.io` — cert riêng
- `sync.54-254-2-136.sslip.io` — cover: sync, auth, events, mono

---

## 8. Ports cần mở trong Security Group

| Port | Mục đích | Trạng thái |
|---|---|---|
| 22 | SSH | Mở |
| 80 | HTTP redirect + certbot renew | Mở |
| 443 | HTTPS (Nginx) | Mở |
| 5264, 8264, 7264, 6264, 9000 | Direct service | **ĐÓNG** |
| 27017, 6379 | MongoDB, Redis | Không mở (an toàn) |
