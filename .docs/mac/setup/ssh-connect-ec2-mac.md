# SSH Connect to EC2 Mac Instance (NotesnookMac)

> Date: 2026-03-31
> Instance: i-02672bfe496225644 (NotesnookMac) — ap-southeast-1

---

## Q1: Có file PEM key, connect terminal như thế nào?

**File key:** `~/Downloads/notesnook-mac-key.pem`

```bash
# Set permission trước
chmod 400 ~/Downloads/notesnook-mac-key.pem

# SSH vào instance
ssh -i ~/Downloads/notesnook-mac-key.pem <username>@<server-ip>
```

Username phổ biến:
- `ubuntu` — Ubuntu AMI
- `ec2-user` — Amazon Linux / macOS AMI

---

## Q2: AWS Console chỉ hiện Private IP (10.0.1.78), không có Public IP

**Nguyên nhân:** Instance nằm trong private subnet, không có Public IPv4 hay Public DNS.

**3 cách giải quyết:**

| Cách | Mô tả | Yêu cầu |
|------|--------|---------|
| **Elastic IP** | Gán public IP tĩnh cho instance | VPC phải có Internet Gateway |
| **EC2 Instance Connect** | Connect qua browser, không cần public IP | AWS console |
| **SSM Session Manager** | Connect không cần SSH key hay public IP | IAM role với `AmazonSSMManagedInstanceCore` |

---

## Q3: Lỗi "Network is not attached to any internet gateway" khi gán Elastic IP

**Lỗi:**
```
Elastic IP address could not be associated.
Elastic IP address 54.254.2.136: Network vpc-0a6a53c801c980d10
is not attached to any internet gateway
```

**Nguyên nhân:** VPC chưa có Internet Gateway → Elastic IP không route được ra internet.

### Fix: Tạo và gắn Internet Gateway

**Bước 1 — Tạo Internet Gateway:**
- VPC console → **Internet Gateways** → **Create internet gateway**
- Đặt tên: `notesnook-igw` → Create

**Bước 2 — Attach vào VPC:**
- Chọn IGW vừa tạo → **Actions** → **Attach to VPC**
- Chọn `vpc-0a6a53c801c980d10` → Attach

**Bước 3 — Cập nhật Route Table:**
- VPC console → **Route Tables** → chọn route table của subnet `subnet-01ab6ff7e805e964b`
- Tab **Routes** → **Edit routes** → **Add route**:
  - Destination: `0.0.0.0/0`
  - Target: Internet Gateway vừa tạo
- Save

**Bước 4 — Associate Elastic IP lại với instance**

**Bước 5 — SSH:**
```bash
chmod 400 ~/Downloads/notesnook-mac-key.pem
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136
```

---

## Kết quả

Connect thành công sau khi:
1. Tạo Internet Gateway
2. Attach IGW vào VPC `vpc-0a6a53c801c980d10`
3. Thêm route `0.0.0.0/0 → IGW` vào Route Table của subnet
4. Associate Elastic IP `54.254.2.136` với instance
5. SSH bằng lệnh:

```bash
chmod 400 ~/Downloads/notesnook-mac-key.pem
ssh -i ~/Downloads/notesnook-mac-key.pem ubuntu@54.254.2.136
```

---

## Lưu ý bảo mật

Sau khi attach IGW, instance exposed ra internet. Kiểm tra Security Group `sg-03b792f91c8684847`:
- Chỉ mở port **22 (SSH)** từ IP của bạn, không dùng `0.0.0.0/0`
- Ví dụ rule an toàn: `22 / TCP / <your-ip>/32`
