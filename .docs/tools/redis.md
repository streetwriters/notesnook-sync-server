# Redis

## Redis là gì? 🤔

Redis là một **in-memory data store** — lưu dữ liệu trên RAM thay vì ổ cứng, nên cực kỳ nhanh.

Dùng phổ biến cho:
- Cache
- Session storage
- Message broker (Pub/Sub)
- Queue

---

## Redis trong Notesnook dùng để làm gì?

Redis được dùng làm **SignalR backplane** — cầu nối giữa nhiều server instance khi scale ngang.

### Vấn đề khi không có Redis (multi-instance)

```
📱 Mobile ---connect---> 🖥️ Server 1
💻 Web    ---connect---> 🖥️ Server 2

📱 gõ note → Server 1 muốn push tới 💻
❌ Server 1 không biết 💻 đang ở Server 2!
```

### Giải pháp với Redis backplane

```
📱 Mobile --note mới--> 🖥️ Server 1
                              |
                              v
                          📮 Redis  ← Server 1 publish vào đây
                              |
                              v
                         🖥️ Server 2 → push tới 💻 Web
✅ Thông suốt!
```

Redis dùng cơ chế **Pub/Sub**: các server subscribe vào Redis, khi một server publish message thì tất cả server còn lại nhận được và forward đến client của mình.

---

## Cấu hình trong project

### Mặc định (không cần Redis)

SignalR chạy **in-memory** — phù hợp khi chỉ có 1 server instance.

### Bật Redis backplane

**Code** (`Notesnook.API/Startup.cs:220`):

```csharp
if (!string.IsNullOrEmpty(Constants.SIGNALR_REDIS_CONNECTION_STRING))
    signalR.AddStackExchangeRedis(Constants.SIGNALR_REDIS_CONNECTION_STRING);
```

**Env var** (`Streetwriters.Common/Constants.cs:82`):

```csharp
public static string? SIGNALR_REDIS_CONNECTION_STRING => ReadSecret("SIGNALR_REDIS_CONNECTION_STRING");
```

---

## Setup trong docker-compose.yml

Redis service đã được thêm vào `docker-compose.yml`:

```yaml
notesnook-redis:
  image: redis:7-alpine
  hostname: notesnook-redis
  networks:
    - notesnook
  healthcheck:
    test: redis-cli ping || exit 1
    interval: 10s
    timeout: 5s
    retries: 3
```

Và `notesnook-server` được set env var:

```yaml
environment:
  SIGNALR_REDIS_CONNECTION_STRING: "notesnook-redis:6379"
```

---

## Tóm lại

| Scenario | Redis cần? |
|---|---|
| 1 server instance | Không |
| Nhiều server instance (scale ngang) | Bắt buộc |

- **Port mặc định:** 6379
- **Image:** `redis:7-alpine`
- **Internal hostname:** `notesnook-redis`
