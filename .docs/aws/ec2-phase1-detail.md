# Phase 1: Chuẩn bị AWS — Hướng dẫn chi tiết

---

## 1.1 Launch EC2 Instance

### Bước 1: Đăng nhập AWS Console
1. Vào [console.aws.amazon.com](https://console.aws.amazon.com)
2. Chọn region **ap-southeast-1 (Singapore)** ở góc trên phải (hoặc region gần bạn nhất)

### Bước 2: Tạo EC2 Instance
1. Vào **EC2 → Instances → Launch instances**
2. Điền các thông số:

**Name:** `notesnook-sync-server`

**AMI:** Tìm `Ubuntu Server 24.04 LTS (HVM)` → chọn **64-bit (x86)**

**Instance type:**
- `t3.medium` — 2 vCPU, 4GB RAM (~$35/tháng) — đủ cho cá nhân/gia đình
- `t3.large` — 2 vCPU, 8GB RAM (~$67/tháng) — nếu nhiều user

**Key pair:**
- Click **Create new key pair**
- Name: `notesnook-key`
- Type: RSA, Format: `.pem` (Linux/Mac) hoặc `.ppk` (PuTTY/Windows)
- Download và lưu cẩn thận — **mất là không SSH được nữa**

### Bước 3: Cấu hình Storage
Trong phần **Configure storage**:

- **Root volume:** 30 GiB, gp3 (xóa khi terminate: Yes)
- Click **Add new volume:**
  - Size: **50 GiB**
  - Volume type: **gp3**
  - Delete on termination: **No** ← quan trọng, giữ data khi tắt instance

### Bước 4: Network settings — Tạo Security Group
Click **Edit** ở phần Network settings:

- VPC: default
- Auto-assign public IP: **Enable**
- **Create security group**, name: `notesnook-sg`

Thêm các inbound rules:

| Type        | Port | Source        | Description              |
|-------------|------|---------------|--------------------------|
| SSH         | 22   | My IP         | SSH access               |
| HTTP        | 80   | 0.0.0.0/0     | HTTP (redirect to HTTPS) |
| HTTPS       | 443  | 0.0.0.0/0     | HTTPS traffic            |

> **Không thêm** port 5264, 8264, 7264, 27017, 9000 — các port này chỉ dùng nội bộ, expose qua Nginx.

### Bước 5: Launch
Click **Launch instance** → chờ status `2/2 checks passed` (~2-3 phút).

---

## 1.2 Cấp Elastic IP

> Mặc định, public IP của EC2 thay đổi mỗi lần restart. Elastic IP giữ IP cố định.

1. Vào **EC2 → Elastic IPs → Allocate Elastic IP address**
2. Network border group: chọn cùng region → **Allocate**
3. Chọn IP vừa cấp → **Actions → Associate Elastic IP address**
4. Resource type: Instance → chọn instance `notesnook-sync-server` → **Associate**

Ghi lại IP này (ví dụ: `54.123.45.67`) — dùng cho bước DNS.

---

## 1.3 Tạo DNS Records

Đăng nhập vào DNS provider của bạn (Cloudflare, Route 53, Namecheap...) và tạo 4 A records:

| Subdomain               | Type | Value (Elastic IP) | TTL  |
|-------------------------|------|--------------------|------|
| `api.yourdomain.com`    | A    | `54.123.45.67`     | 300  |
| `auth.yourdomain.com`   | A    | `54.123.45.67`     | 300  |
| `sse.yourdomain.com`    | A    | `54.123.45.67`     | 300  |
| `s3.yourdomain.com`     | A    | `54.123.45.67`     | 300  |

> Nếu dùng **Cloudflare**: tắt proxy (cloud icon → **DNS only**, màu xám) để tránh conflict với SSL cert sau này.

Sau khi tạo, kiểm tra DNS đã propagate chưa:
```bash
nslookup api.yourdomain.com
# Phải trả về Elastic IP của bạn
```

---

## 1.4 Test SSH vào Instance

```bash
# Linux/Mac
chmod 400 notesnook-key.pem
ssh -i notesnook-key.pem ubuntu@54.123.45.67

# Windows (PowerShell)
ssh -i notesnook-key.pem ubuntu@54.123.45.67
```

Nếu connect thành công → Phase 1 hoàn tất, sẵn sàng cho Phase 2.

---

## Checklist Phase 1

- [ ] EC2 instance `Running`, status checks passed
- [ ] EBS 50GB volume attached (kiểm tra: `lsblk` sau khi SSH)
- [ ] Security Group chỉ mở port 22, 80, 443
- [ ] Elastic IP đã gắn vào instance
- [ ] 4 DNS A records trỏ về Elastic IP
- [ ] SSH vào được instance
