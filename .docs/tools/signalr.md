# SignalR

## SignalR là gì? 🤔

### Vấn đề với HTTP thường

Bình thường, web hoạt động kiểu này:

```
👦 Client                    🖥️ Server
   |                            |
   |---"Hey, có gì mới không?"->|
   |<-------"Không có gì"-------|
   |                            |
   (5 giây sau...)
   |                            |
   |---"Hey, có gì mới không?"->|
   |<-------"Vẫn không có gì"---|
   |                            |
   (5 giây sau...)
   |---"Hey, có gì mới không?"->|
   |<-------"Có rồi! Đây này"---|
```

Client phải **hỏi đi hỏi lại** (polling). Rất tốn tài nguyên! 😫

---

### SignalR giải quyết bằng cách nào?

SignalR mở một **đường kết nối 2 chiều** (WebSocket) và giữ nó mãi:

```
👦 Client                    🖥️ Server
   |                            |
   |===== KẾT NỐI MỞ =========>|
   |                            |
   |        (im lặng...)        |
   |                            |
   |<==== "Có note mới rồi!" ===|   ← Server TỰ PUSH, không cần hỏi!
   |                            |
   |        (im lặng...)        |
   |                            |
   |<==== "Có note mới rồi!" ===|
   |                            |
```

Server **chủ động gửi** khi có gì đó thay đổi. Client chỉ ngồi nghe! 👂

---

### Trong Notesnook dùng như thế nào?

```
📱 Điện thoại        🖥️ Server        💻 Máy tính
     |                   |                 |
     |===== connect =====>|<==== connect ===|
     |                   |                 |
     | -- Lưu note A --> |                 |
     |                   |                 |
     |                   |==== "Note A" ==>|  ← Máy tính nhận ngay!
     |                   |                 |
```

Bạn gõ note trên điện thoại → server nhận → **push ngay** sang máy tính. Không cần F5! ⚡

Tương tự với **Web ↔ Mobile**:

**✅ Web → Mobile: hoạt động tốt**

```
🌐 Web               🖥️ Server           📱 Mobile
     |                    |                    |
     |==== connect ======>|<==== connect =======|
     |                    |                    |
     |--- gõ note A ----->|                    |
     |                    |---- push "Note A" ->|  ✅ Mobile nhận được!
     |                    |                    |
```

**❌ Mobile → Web: KHÔNG hoạt động**

```
🌐 Web               🖥️ Server           📱 Mobile
     |                    |                    |
     |==== connect ======>|<==== connect =======|
     |                    |                    |
     |                    |<--- gõ note B ------|
     |                    |                    |
     |                    |---- push "Note B" ->|  ← Server push về đúng
     |                    |         ???         |     connection của Web...
     |                    |                    |
     X (không nhận được)  |                    |  ❌ Web im lặng!
```

**Tại sao bị vậy?** Một trong các lý do sau:

```
Lý do 1: Web mất kết nối (tab sleep, timeout...)
────────────────────────────────────────────────
🌐 Web               🖥️ Server           📱 Mobile
     |                    |                    |
     |==== connect ======>|<==== connect =======|
     |                    |                    |
     X ← Web DISCONNECT   |                    |
     |                    |<--- gõ note B ------|
     |                    |                    |
     |                    |--X  push thất bại   |  ❌ Không ai nhận!


Lý do 2: Nhiều server instance, thiếu Redis
────────────────────────────────────────────
🌐 Web          🖥️ Server 1    🖥️ Server 2    📱 Mobile
     |                |               |               |
     |== connect ====>|               |<== connect ====|
     |                |               |               |
     |                |               |<-- gõ note B --|
     |                |               |               |
     |                |     X push?   |               |
     |                |  Server 2 không biết           |
     |                |  Web đang ở Server 1!          |
     X (không nhận)   |               |               |  ❌
```

Fix lý do 2: set `SIGNALR_REDIS_CONNECTION_STRING` → Redis làm cầu nối giữa các server.

Điều kiện để **cả hai chiều** hoạt động: cả hai đang online, cùng tài khoản, và web không bị sleep/disconnect. ✅

---

### Redis vào đây làm gì? 🤔

Khi chỉ có **1 server**:

```
📱 ---connect---> 🖥️ Server 1
💻 ---connect---> 🖥️ Server 1
✅ Cùng 1 server → push được ngay!
```

Khi có **nhiều server** (scale ngang):

```
📱 ---connect---> 🖥️ Server 1
💻 ---connect---> 🖥️ Server 2
❌ Khác server → Server 1 không biết laptop đang ở Server 2!
```

Redis làm **bưu điện trung gian** 📮:

```
📱 --note mới--> 🖥️ Server 1
                      |
                      v
                  📮 Redis  ← Server 1 gửi thông báo vào đây
                      |
                      v
                 🖥️ Server 2 → push tới 💻 Máy tính
✅ Thông suốt!
```

---

### Tóm lại 🎯

| | HTTP thường | SignalR |
|---|---|---|
| Ai hỏi? | Client hỏi liên tục | Không cần hỏi |
| Ai gửi? | Server chỉ trả lời | Server tự push |
| Tốc độ | Chậm ⏳ | Gần như real-time ⚡ |
| Redis cần? | Không | Chỉ khi nhiều instance |

---

### Cấu hình trong project

- **Mặc định:** SignalR chạy in-memory (không cần Redis)
- **Multi-instance:** Set env var `SIGNALR_REDIS_CONNECTION_STRING` để bật Redis backplane
- **Code:** `Notesnook.API/Startup.cs:220`

```csharp
if (!string.IsNullOrEmpty(Constants.SIGNALR_REDIS_CONNECTION_STRING))
    signalR.AddStackExchangeRedis(Constants.SIGNALR_REDIS_CONNECTION_STRING);
```
