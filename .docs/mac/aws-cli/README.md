# AWS CLI — Hướng Dẫn Cài Đặt & Cấu Hình trên macOS

## AWS CLI là gì?

AWS CLI (Command Line Interface) là công cụ dòng lệnh cho phép bạn tương tác với các dịch vụ AWS trực tiếp từ terminal, thay vì phải click thủ công trên AWS Console.

**Dùng để làm gì:**

| Mục đích | Ví dụ |
|---|---|
| Tự động hóa | Script deploy app lên server mỗi khi push code |
| Quản lý hàng loạt | Xóa 100 file S3 cùng lúc |
| DevOps / CI/CD | GitHub Actions gọi AWS CLI để deploy |
| Làm việc nhanh | Upload file, tạo server, check logs không cần browser |

---

## 1. Cài đặt AWS CLI

### Cách 1: Homebrew (Khuyên dùng)

```bash
# Kiểm tra Homebrew đã cài chưa
brew --version

# Cài AWS CLI
brew install awscli
```

### Cách 2: Installer chính thức

```bash
# Tải file cài đặt
curl "https://awscli.amazonaws.com/AWSCLIV2.pkg" -o "AWSCLIV2.pkg"

# Cài đặt
sudo installer -pkg AWSCLIV2.pkg -target /
```

### Kiểm tra cài thành công

```bash
aws --version
# Kết quả: aws-cli/2.x.x Python/3.x.x Darwin/...
```

---

## 2. Tạo IAM User trên AWS Console

### Bước 1 — Đăng nhập AWS Console

Truy cập: https://console.aws.amazon.com

### Bước 2 — Vào IAM

```
Thanh tìm kiếm → gõ "IAM" → chọn IAM
```

### Bước 3 — Tạo User mới

```
IAM Dashboard → Users (bên trái) → [Create user] (góc trên phải)
```

- **User name:** đặt tên tùy ý (ví dụ: `mac-cli-user`)
- **Console access:** bỏ tick nếu chỉ dùng CLI
- Nhấn **Next**

### Bước 4 — Gán quyền (Permissions)

Chọn **"Attach policies directly"** → tick policy phù hợp:

| Mục đích | Policy |
|---|---|
| Học / test (toàn quyền) | `AdministratorAccess` |
| Chỉ dùng S3 | `AmazonS3FullAccess` |
| Chỉ dùng EC2 | `AmazonEC2FullAccess` |

Nhấn **Next** → **Create user**

---

## 3. Tạo Access Key

```
IAM → Users → [tên user] → Security credentials
→ Access keys → [Create access key]
→ Chọn "Command Line Interface (CLI)"
→ Tick checkbox đồng ý → Next → Create access key
→ Download .csv file (CHỈ HIỆN 1 LẦN — lưu ngay!)
```

> **CẢNH BÁO:** Secret Access Key chỉ hiển thị 1 lần duy nhất.
> Bắt buộc phải tải file CSV hoặc copy ngay lúc này.

---

## 4. Cấu hình AWS CLI

```bash
aws configure
```

Điền vào các trường:

```
AWS Access Key ID:      AKIA...........
AWS Secret Access Key:  wJalr...........
Default region name:    ap-southeast-1
Default output format:  json
```

**Các region phổ biến:**

| Region | Vị trí |
|---|---|
| `ap-southeast-1` | Singapore (gần VN nhất) |
| `ap-southeast-2` | Sydney |
| `us-east-1` | N. Virginia (mặc định nhiều dịch vụ) |

**Hoặc cấu hình từng giá trị riêng:**

```bash
aws configure set aws_access_key_id YOUR_ACCESS_KEY
aws configure set aws_secret_access_key YOUR_SECRET_KEY
aws configure set region ap-southeast-1
aws configure set output json
```

---

## 5. Kiểm tra kết nối

```bash
aws sts get-caller-identity
```

Kết quả mong đợi:

```json
{
    "UserId": "AIDA...",
    "Account": "123456789012",
    "Arn": "arn:aws:iam::123456789012:user/mac-cli-user"
}
```

---

## 6. Các lệnh hay dùng

### Xem cấu hình hiện tại

```bash
aws configure list
```

### Xem danh sách S3 buckets

```bash
aws s3 ls
```

### Upload file lên S3

```bash
aws s3 cp ./file.txt s3://bucket-name/
```

### Xem danh sách EC2 instances

```bash
aws ec2 describe-instances --query "Reservations[*].Instances[*].[InstanceId,State.Name,PublicIpAddress]" --output table
```

### Xem logs Lambda

```bash
aws logs tail /aws/lambda/function-name --follow
```

---

## 7. Bảo mật Access Key

> **Access Key = Mật khẩu ngân hàng — Không chia sẻ với bất kỳ ai**

- Không commit lên Git (thêm vào `.gitignore`)
- Không paste lên chat, email, Slack
- Nếu lỡ lộ key → revoke ngay lập tức:

```
AWS Console → IAM → Users → [tên user]
→ Security credentials → Access keys
→ Deactivate → Delete → Tạo key mới
```

### Kiểm tra file credentials được lưu ở đâu

```bash
cat ~/.aws/credentials
cat ~/.aws/config
```

---

## 8. Xử lý lỗi thường gặp

### Lỗi: `Unable to locate credentials`

```bash
# Kiểm tra credentials đã được cấu hình chưa
aws configure list

# Cấu hình lại
aws configure
```

### Lỗi: `InvalidClientTokenId`

- Access Key ID sai hoặc đã bị xóa/deactivate
- Vào IAM Console kiểm tra lại trạng thái key

### Lỗi: `AccessDenied`

- User không có đủ quyền cho action đó
- Vào IAM → Users → [tên user] → Permissions → thêm policy phù hợp
