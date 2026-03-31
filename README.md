# Notesnook Sync Server

Backend tự host cho ứng dụng ghi chú Notesnook, được cấp phép theo AGPLv3.

## Kiến trúc

```
Client
  ├── Streetwriters.Identity  (OAuth2/OpenID Connect, port 8264)
  ├── Notesnook.API           (sync + attachments, port 5264)
  │     ├── MongoDB           (dữ liệu note, users, devices)
  │     ├── MinIO             (S3 lưu trữ file đính kèm)
  │     └── SignalR Hub       (real-time sync, in-memory mặc định)
  ├── Streetwriters.Messenger (SSE real-time events, port 7264)
  └── Notesnook.Inbox.API     (email inbox với OpenPGP, port 3000, Bun)
```

## Cách chạy (Khuyến nghị: Docker)

### Bước 1 — Chuẩn bị file `.env`

Sao chép file mẫu và chỉnh sửa:

```bash
cp .env.example .env
```

Mở `.env` và điền các biến **bắt buộc**:

| Biến | Mô tả | Ví dụ |
|---|---|---|
| `INSTANCE_NAME` | Tên instance của bạn | `my-notesnook` |
| `NOTESNOOK_API_SECRET` | Token bí mật >32 ký tự | chuỗi ngẫu nhiên |
| `DISABLE_SIGNUPS` | Tắt đăng ký mới | `false` |
| `SMTP_USERNAME` | Email SMTP | `you@gmail.com` |
| `SMTP_PASSWORD` | Mật khẩu SMTP | app password |
| `SMTP_HOST` | Host SMTP | `smtp.gmail.com` |
| `SMTP_PORT` | Port SMTP | `465` |
| `AUTH_SERVER_PUBLIC_URL` | URL public của Identity server | `http://localhost:8264` |
| `NOTESNOOK_APP_PUBLIC_URL` | URL public của web app | `https://app.notesnook.com` |
| `MONOGRAPH_PUBLIC_URL` | URL public của Monograph | `http://localhost:6264` |
| `ATTACHMENTS_SERVER_PUBLIC_URL` | URL public của MinIO | `http://localhost:9000` |

> **Lưu ý Gmail:** Dùng [App Password](https://myaccount.google.com/apppasswords) thay vì mật khẩu thường, và bật 2FA trên tài khoản Google.

### Bước 2 — Khởi chạy toàn bộ stack

```bash
docker compose up -d
```

Docker Compose sẽ tự động khởi động:
- MongoDB (replica set, port 27017)
- MinIO S3 (port 9000, console port 9090)
- Identity Server (port 8264)
- Notesnook API (port 5264)
- SSE Server (port 7264)
- Monograph Server (port 6264)

### Bước 3 — Kiểm tra hoạt động

```bash
# Xem log các service
docker compose logs -f

# Kiểm tra health check từng service
curl http://localhost:5264/health   # Notesnook API
curl http://localhost:8264/health   # Identity Server
curl http://localhost:7264/health   # SSE Server
```

### Dừng và xóa stack

```bash
# Dừng (giữ data)
docker compose down

# Dừng và xóa toàn bộ data (volume)
docker compose down -v
```

---

## Chạy từ source code

### Yêu cầu

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Bun](https://bun.sh/) (cho Inbox API)
- MongoDB, MinIO đang chạy (có thể dùng Docker cho phần này)
- Redis (tùy chọn, chỉ cần khi chạy nhiều instance)

### Clone và build

```bash
git clone https://github.com/streetwriters/notesnook-sync-server.git
cd notesnook-sync-server

# Restore packages
dotnet restore Notesnook.sln

# Build tất cả project
dotnet build Notesnook.sln
```

### Chạy từng service

Mở các terminal riêng biệt cho từng service:

```bash
# Terminal 1 — Identity Server (port 8264)
dotnet run --project Streetwriters.Identity/Streetwriters.Identity.csproj

# Terminal 2 — Notesnook API (port 5264)
dotnet run --project Notesnook.API/Notesnook.API.csproj

# Terminal 3 — SSE Messenger (port 7264)
dotnet run --project Streetwriters.Messenger/Streetwriters.Messenger.csproj

# Terminal 4 — Inbox API (port 3000)
cd Notesnook.Inbox.API
bun install
bun run src/index.ts
```

> **Lưu ý khi chạy từ source:** MongoDB không cần replica set ở môi trường `DEBUG`/`STAGING` (transactions bị tắt tự động).

---

## Cấu hình Notesnook Client

Sau khi server chạy, mở app Notesnook (từ v3.0.18 trở lên) và vào **Settings > Notesnook Sync Server** để trỏ về server của bạn:

- **Sync Server:** `http://localhost:5264`
- **Auth Server:** `http://localhost:8264`
- **SSE Server:** `http://localhost:7264`

---

## Ports mặc định

| Service | Port |
|---|---|
| Notesnook API | 5264 |
| Identity Server | 8264 |
| SSE Messenger | 7264 |
| Inbox API | 3000 |
| Monograph | 6264 |
| MongoDB | 27017 |
| MinIO API | 9000 |
| MinIO Console | 9090 |

---

## License

```
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
```
