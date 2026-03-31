# SMTP Setup

## Dùng Gmail SMTP (miễn phí)

### Bước 1 — Bật 2FA
Vào **myaccount.google.com → Security → 2-Step Verification** → bật lên

### Bước 2 — Tạo App Password
**myaccount.google.com → Security → App passwords**
- App name: `notesnook`
- Click **Create**
- Copy mật khẩu 16 ký tự (chỉ hiện 1 lần)

### Bước 3 — Điền vào `.env` trên EC2

```env
SMTP_USERNAME=your.gmail@gmail.com
SMTP_PASSWORD=abcdefghijklmnop    # 16 ký tự, bỏ dấu cách
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
```

## Lưu ý
- Gửi được cho **bất kỳ email nào** (Gmail, Outlook, Yahoo, email công ty...)
- Giới hạn: **500 email/ngày** — đủ dùng cho self-host cá nhân
- App Password chỉ hiển thị **1 lần** → copy ngay
