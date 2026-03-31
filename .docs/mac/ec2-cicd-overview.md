# Deploy Notesnook Sync Server lên EC2 với GitHub CI/CD

## Tổng quan project

Đây là **Notesnook Sync Server** — backend C# cho app ghi chú Notesnook.
Project đã có sẵn Dockerfile, chỉ cần biết cách deploy (không cần viết thêm C#).

## Kiến trúc (5 services chạy bằng Docker)

```
EC2 Instance
└── Docker Compose
    ├── identity-server   (port 8264) ← Đăng nhập/Auth
    ├── notesnook-server  (port 5264) ← Sync notes chính
    ├── sse-server        (port 7264) ← Real-time events
    ├── monograph-server  (port 6264) ← Published notes
    ├── notesnook-db      (port 27017) ← MongoDB
    ├── notesnook-s3      (port 9000) ← MinIO (lưu file đính kèm)
    └── notesnook-redis   (port 6379) ← Redis cache
```

## Hiện tại đã có gì?

File `.github/workflows/publish.yml` đã tồn tại — nó:
- Trigger khi push tag `v*` (vd: `v1.0.0`)
- Build Docker images cho tất cả services
- Push lên **Docker Hub** (`streetwriters/notesnook-sync`, v.v.)

**Vấn đề:** Workflow hiện tại chỉ build & push image, **chưa có bước deploy lên EC2**.

## Kế hoạch CI/CD hoàn chỉnh

```
Developer push code
        ↓
GitHub Actions
  [1] Build Docker images
  [2] Push lên registry (Docker Hub / ECR)
  [3] SSH vào EC2
  [4] Pull images mới + restart containers
        ↓
EC2 chạy docker compose up -d
        ↓
App live!
```

## Checklist cần xác nhận trước khi làm

- [ ] **Registry**: Docker Hub hay AWS ECR?
- [ ] **Trigger**: Push `main`/`master` hay push tag `v*`?
- [ ] **EC2**: Đã tạo chưa? Instance type? (khuyến nghị tối thiểu `t3.medium`)
- [ ] **Docker trên EC2**: Đã cài Docker + Docker Compose chưa?
- [ ] **Domain**: Có domain/subdomain chưa? Hay dùng IP public tạm?

## Các biến môi trường cần thiết (`.env` trên EC2)

| Biến | Mô tả |
|---|---|
| `INSTANCE_NAME` | Tên instance |
| `NOTESNOOK_API_SECRET` | Secret key (>32 ký tự) |
| `DISABLE_SIGNUPS` | `true`/`false` |
| `SMTP_USERNAME` | Email gửi thông báo |
| `SMTP_PASSWORD` | Mật khẩu email |
| `SMTP_HOST` | SMTP host |
| `SMTP_PORT` | SMTP port |
| `AUTH_SERVER_PUBLIC_URL` | URL public của identity-server |
| `NOTESNOOK_APP_PUBLIC_URL` | URL public của app |
| `MONOGRAPH_PUBLIC_URL` | URL public của monograph |
| `ATTACHMENTS_SERVER_PUBLIC_URL` | URL public của MinIO |
| `MINIO_ROOT_USER` | MinIO username |
| `MINIO_ROOT_PASSWORD` | MinIO password |
