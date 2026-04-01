# EC2 Architecture — Notesnook Self-Hosted

> IP: `54.254.2.136` | Instance: `i-02672bfe496225644` | Updated: 2026-04-01

---

## Tổng quan

```
Internet
    │
    ├── :22  SSH (My IP only)
    ├── :80  HTTP → redirect HTTPS (certbot renew)
    └── :443 HTTPS
              │
           Nginx (reverse proxy)
              │
    ┌─────────┼─────────────────────┐────────────────┐
    │         │                     │                │
sync.*      auth.*              events.*          minio.*
.sslip.io   .sslip.io          .sslip.io         .sslip.io
    │         │                     │                │
 :5264      :8264               :7264            :9000
Notesnook   Identity           SSE Messenger     MinIO (S3)
    │         │                     │
    └─────────┴── Docker Network ───┘
                      │         │
                   MongoDB    Redis
                   :27017     :6379
                 (internal)  (internal)
```

---

## Instance

| Thông số | Giá trị |
|---|---|
| Type | t3.medium (2 vCPU, 4 GiB RAM) |
| OS | Ubuntu 24.04 LTS |
| Internal IP | `10.0.1.78` |
| VPC | `10.0.0.0/16` |
| Subnet | `10.0.1.0/24` |
| Security Group | `sg-03b792f91c8684847` |

---

## Storage

| Disk | Size | Mount | Dùng cho |
|---|---|---|---|
| Root gp3 | 30 GiB | `/` | OS, swap |
| EBS gp3 | 60 GiB | `/data` | Docker data-root |

Docker data-root: `/data/docker` (toàn bộ images, containers, volumes lưu trên EBS)

---

## Security Group — Inbound Rules

| Port | Protocol | Source | Mục đích |
|---|---|---|---|
| 22 | TCP | My IP | SSH |
| 80 | TCP | 0.0.0.0/0 | HTTP redirect + certbot renew |
| 443 | TCP | 0.0.0.0/0 | HTTPS (Nginx) |

> Các port 5264, 8264, 7264, 6264, 9000 **ĐÓNG** — Nginx proxy nội bộ.
> MongoDB (27017) và Redis (6379) **không mở** — an toàn.

---

## Nginx — Reverse Proxy

Config: `/etc/nginx/sites-enabled/default`

| Subdomain | Proxy đến | Service |
|---|---|---|
| `sync.54-254-2-136.sslip.io` | `localhost:5264` | Notesnook API |
| `auth.54-254-2-136.sslip.io` | `localhost:8264` | Identity Server |
| `events.54-254-2-136.sslip.io` | `localhost:7264` | SSE Messenger |
| `mono.54-254-2-136.sslip.io` | `localhost:6264` | Monograph (disabled) |
| `minio.54-254-2-136.sslip.io` | `localhost:9000` | MinIO S3 |

**Lưu ý SSE:** `events` block cần thêm:
```nginx
proxy_buffering off;
proxy_cache off;
proxy_read_timeout 86400s;
proxy_set_header X-Accel-Buffering no;
```

---

## SSL Certificates (Let's Encrypt)

| Cert | Domains | Expiry |
|---|---|---|
| `sync.54-254-2-136.sslip.io` | sync, auth, events, mono | 2026-06-29 |
| `minio.54-254-2-136.sslip.io` | minio | 2026-06-30 |

Auto-renew qua certbot systemd timer.

---

## Docker Compose Stack

Files: `~/notesnook/docker-compose.yml` + `~/notesnook/docker-compose.prod.yml`

| Container | Image | Port | Status |
|---|---|---|---|
| `notesnook-server` | `vincentephan/notesnook-sync:latest` | 5264 | ✅ healthy |
| `identity-server` | `vincentephan/identity:latest` | 8264 | ✅ healthy |
| `sse-server` | `vincentephan/sse:latest` | 7264 | ✅ healthy |
| `monograph-server` | `streetwriters/monograph:latest` | 6264 | ❌ disabled |
| `notesnook-s3` | `minio/minio` | 9000, 9090 | ✅ healthy |
| `notesnook-db` | `mongo:7.0.12` | 27017 | ✅ healthy |
| `notesnook-redis` | `redis:7-alpine` | 6379 | ✅ healthy |
| `autoheal` | `willfarrell/autoheal` | — | ✅ healthy |

---

## CI/CD (GitHub Actions)

Trigger: push lên branch `main`

```
git push origin main
    ↓
Build 3 Docker images (ubuntu-latest / AMD64):
  - Notesnook.API/Dockerfile       → vincentephan/notesnook-sync
  - Streetwriters.Identity/Dockerfile → vincentephan/identity
  - Streetwriters.Messenger/Dockerfile → vincentephan/sse
    ↓
Push lên Docker Hub
    ↓
SSH EC2 → docker compose pull → up -d --remove-orphans
```

---

## Environment — `.env` key variables

| Biến | Giá trị |
|---|---|
| `NOTESNOOK_APP_PUBLIC_URL` | `https://sync.54-254-2-136.sslip.io` |
| `AUTH_SERVER_PUBLIC_URL` | `https://auth.54-254-2-136.sslip.io` |
| `MONOGRAPH_PUBLIC_URL` | `https://mono.54-254-2-136.sslip.io` |
| `ATTACHMENTS_SERVER_PUBLIC_URL` | `https://minio.54-254-2-136.sslip.io` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` |
| `DISABLE_SIGNUPS` | `false` |

---

## Security Assessment

| Layer | Status | Ghi chú |
|---|---|---|
| External attack surface | ✅ Tốt | Chỉ 22/80/443 |
| HTTPS | ✅ Tốt | Toàn bộ services |
| MongoDB/Redis public | ✅ Tốt | Không expose |
| MinIO credentials | ⚠️ Yếu | `minioadmin/minioadmin123` cần đổi |
| MongoDB auth | ⚠️ Thiếu | Không có password nội bộ |
| Redis auth | ⚠️ Thiếu | Không có password nội bộ |
| DISABLE_SIGNUPS | ⚠️ | `false` — ai cũng đăng ký được |
| fail2ban | ⚠️ Thiếu | SSH brute-force không được bảo vệ |
