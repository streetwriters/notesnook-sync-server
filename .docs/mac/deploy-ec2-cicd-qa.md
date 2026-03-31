# Q&A: Deploy Notesnook lên EC2 với GitHub CI/CD

---

**Q: Hiện tại đây là source code backend C# tôi không có kinh nghiệm. Sếp yêu cầu tôi deploy với EC2 CI/CD với GitHub.**

A: Tổng quan project — Notesnook Sync Server là backend C# cho app ghi chú Notesnook. Bạn không cần viết thêm C# gì cả. Project đã có sẵn Dockerfile, chỉ cần biết cách deploy.

**Kiến trúc (5 services chạy bằng Docker):**
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

**Hiện tại đã có gì?** File `.github/workflows/publish.yml` đã tồn tại — nó trigger khi push tag `v*`, build Docker images cho tất cả services, push lên Docker Hub (`streetwriters/notesnook-sync`, v.v.). Nhưng chỉ build & push image, chưa có bước deploy lên EC2.

**Kế hoạch CI/CD:**
```
GitHub push to main
        │
        ▼
┌─────────────────────────────────┐
│  Job 1: build-and-push          │
│  • Build Docker images          │
│  • Push lên Docker Hub của bạn  │
└──────────────┬──────────────────┘
               │ (xong mới chạy)
               ▼
┌─────────────────────────────────┐
│  Job 2: deploy                  │
│  • SSH vào EC2                  │
│  • docker compose pull          │
│  • docker compose up -d         │
└─────────────────────────────────┘
```

---

**Q: Cần tạo/thêm gì?**

| File | Mục đích |
|---|---|
| `.github/workflows/deploy.yml` | Workflow CI/CD chính |
| `docker-compose.prod.yml` | Override image names sang Docker Hub của bạn |
| GitHub Secrets (5 cái) | Credentials an toàn |

**GitHub Secrets cần thêm:**
```
DOCKER_USERNAME     ← username Docker Hub
DOCKER_PASSWORD     ← password/token Docker Hub
EC2_HOST            ← IP public của EC2
EC2_USERNAME        ← thường là "ubuntu"
EC2_SSH_KEY         ← nội dung file .pem
```

---

**Q: Username Docker Hub xem ở đâu?**

Vào hub.docker.com → đăng nhập → nhìn góc trên phải. Hoặc URL profile: `hub.docker.com/u/YOUR_USERNAME`.

Username của project này: `vincentephan`

---

**Q: Sao push Docker chỉ có 3 service thôi vậy?**

So sánh đúng:

| Service | Dockerfile trong repo? | Cần build? |
|---|---|---|
| `notesnook-server` | ✅ `Notesnook.API/Dockerfile` | ✅ |
| `identity-server` | ✅ `Streetwriters.Identity/Dockerfile` | ✅ |
| `sse-server` | ✅ `Streetwriters.Messenger/Dockerfile` | ✅ |
| `notesnook-inbox` | ✅ `Notesnook.Inbox.API/Dockerfile` | ✅ (bị bỏ sót lần đầu) |
| `monograph-server` | ❌ không có | ❌ dùng upstream |

→ Đã fix: build 4 images (sync, identity, sse, inbox). Monograph dùng image upstream của streetwriters.

---

**Q: "Monograph dùng upstream" nghĩa là ntn?**

"Upstream" = image gốc do tác giả gốc của project build và publish.

```
Tác giả gốc (streetwriters)          Bạn (vincentephan)
        │                                    │
        │  viết code monograph               │  fork repo về
        │  build Docker image                │  sửa code khác
        │  push lên Docker Hub               │  build image riêng
        ▼                                    ▼
streetwriters/monograph:latest      vincentephan/notesnook-sync:latest
        │
        │ ← docker-compose.yml của bạn vẫn pull image NÀY
        ▼
  EC2 chạy monograph của streetwriters
```

Không có Dockerfile cho monograph trong repo → không build được → phải dùng image họ đã build sẵn. Đây là chuyện bình thường khi self-host project mã nguồn mở.

---

**Q: Nếu xóa notesnook-inbox thì có dùng được backend không?**

**Có, vẫn dùng được bình thường.** `notesnook-inbox` là tính năng phụ, hoàn toàn độc lập.

```
Client (app Notesnook)
        │
        ├── identity-server  ← đăng nhập, tạo tài khoản ✅
        ├── notesnook-server ← sync notes, attachments  ✅
        └── sse-server       ← real-time push           ✅

        (inbox-server)       ← email → note            ❌ bỏ cũng được
```

---

**Q: "Gửi mail thành note" là ntn?**

`Notesnook.Inbox.API` là tính năng cho phép gửi email vào app → email tự động lưu thành note.

```
Bạn có địa chỉ email đặc biệt: notes@yourserver.com
        │
        │  Forward email bất kỳ vào đây
        ▼
Notesnook.Inbox.API nhận email
        │
        ▼
Tự động tạo note mới trong app
```

**Ví dụ thực tế:**
```
Sếp gửi email: "Họp lúc 3h chiều, tầng 5, mang báo cáo Q1"

❌ Không có inbox:  copy email → mở app → tạo note → paste → lưu
✅ Có inbox:        forward tới notes@yourserver.com → app tự có note mới
```

Tính năng này khá niche và cần setup thêm domain, DNS email (MX record), email server. **Nên bỏ cho đơn giản.**

---

## Files đã tạo

### `.github/workflows/deploy.yml`
Trigger khi push lên `main`. Build 4 Docker images → push Docker Hub → SSH vào EC2 → pull images mới → restart containers.

### `docker-compose.prod.yml`
Override image names từ `streetwriters/*` sang `vincentephan/*` cho 3 service tự build.

---

## Setup EC2 (làm 1 lần)

```bash
# Cài Docker
sudo apt update && sudo apt install -y docker.io docker-compose-plugin
sudo usermod -aG docker ubuntu
newgrp docker

# Tạo thư mục
mkdir ~/notesnook

# Tạo file .env
nano ~/notesnook/.env
```

---

## GitHub Secrets cần thêm

Vào: **repo GitHub → Settings → Secrets and variables → Actions → New repository secret**

| Secret | Giá trị |
|---|---|
| `DOCKER_USERNAME` | `vincentephan` |
| `DOCKER_PASSWORD` | Access Token Docker Hub |
| `EC2_HOST` | IP public EC2 |
| `EC2_USERNAME` | `ubuntu` |
| `EC2_SSH_KEY` | Nội dung file `.pem` (kể cả `-----BEGIN...`) |
