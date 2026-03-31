# Kết nối MongoDB Compass

## Vấn đề

Replica set được cấu hình với hostname nội bộ Docker `notesnook-db:27017`.
Khi connect từ bên ngoài Docker, máy tính không resolve được hostname này → lỗi `getaddrinfo ENOTFOUND notesnook-db`.

## Điều kiện tiên quyết

Stack phải đang chạy:

```bash
docker compose up -d
```

Kiểm tra MongoDB healthy:

```bash
docker compose ps
# notesnook-sync-server-notesnook-db-1 phải Up (healthy) và port 0.0.0.0:27017->27017/tcp
```

## Cách 1: Direct Connection (đơn giản, không cần quyền admin)

Connection string trong Compass:

```
mongodb://localhost:27017/?directConnection=true
```

Chọn option **"Direct Connection"** trong Compass khi connect.

## Cách 2: Sửa hosts file (full replica set support)

Chạy PowerShell với quyền **Administrator**:

```powershell
Add-Content -Path "C:\Windows\System32\drivers\etc\hosts" -Value "127.0.0.1 notesnook-db"
```

Sau đó connect Compass với:

```
mongodb://localhost:27017/?replicaSet=rs0
```

## Databases

| Database | Nội dung |
|---|---|
| `notesnook` | Notes, attachments, sync devices, user data |
| `identity` | OAuth2 users, tokens, sessions |
