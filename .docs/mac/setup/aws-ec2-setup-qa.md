# AWS EC2 Setup — Q&A

---

## Q: Hướng dẫn setup EC2 t3.medium trên AWS

### Bước 1: Đăng nhập AWS Console

Truy cập console.aws.amazon.com → Đăng nhập tài khoản AWS.

### Bước 2: Tạo EC2 Instance

1. Tìm **EC2** trong thanh tìm kiếm → Click **EC2**
2. Click **"Launch instance"**

### Bước 3: Cấu hình instance

**Name & AMI:**
```
Name: my-server
AMI: Ubuntu Server 24.04 LTS
Architecture: 64-bit (x86)
```

**Instance type:**
```
t3.medium (2 vCPU, 4 GiB RAM)
```

**Key pair:**
- Click "Create new key pair"
- Chọn RSA + .pem → Download về máy (giữ kỹ, mất là không SSH được)

**Network settings — Security Group:**
| Type  | Port | Source    |
|-------|------|-----------|
| SSH   | 22   | My IP     |
| HTTP  | 80   | 0.0.0.0/0 |
| HTTPS | 443  | 0.0.0.0/0 |

**Storage:**
```
Root volume: 30 GiB gp3
```

### Bước 4: Launch

Click "Launch instance" → Chờ ~1-2 phút.

### Bước 5: SSH vào server

```bash
chmod 400 ~/Downloads/your-key.pem
ssh -i ~/Downloads/your-key.pem ubuntu@<PUBLIC_IP>
```

### Bước 6: Setup cơ bản

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y git curl wget unzip build-essential
```

---

## Q: Root volume bao nhiêu là đủ?

| Volume          | Size       | Loại | Dùng cho                      |
|-----------------|------------|------|-------------------------------|
| Root volume     | 30 GiB gp3 | `/`  | OS + Docker + app images      |
| EBS data volume | 50 GiB gp3 | `/data` | Database, notes data, user files |

**Chi phí (~us-east-1):**
- Root 30GB gp3: ~$2.40/tháng
- EBS 50GB gp3: ~$4/tháng

---

## Q: EBS data volume 50 GiB — setup như thế nào?

### Phần 1: Thêm EBS khi tạo EC2

Ở bước Configure storage:
```
Root volume:  30 GiB  gp3   Delete on termination: YES
+ Add volume: 50 GiB  gp3   Delete on termination: NO  ← quan trọng
```

### Phần 2: Mount EBS vào server (sau khi SSH vào)

```bash
# Kiểm tra disk đã attach chưa
lsblk

# Format ext4
sudo mkfs.ext4 /dev/xvdb

# Tạo mount point
sudo mkdir /data

# Mount
sudo mount /dev/xvdb /data

# Auto-mount khi reboot
echo '/dev/xvdb /data ext4 defaults,nofail 0 2' | sudo tee -a /etc/fstab

# Verify
df -h /data
```

### Phần 3: Bind Docker volumes lên EBS

```yaml
# docker-compose.yml — phần volumes:
volumes:
  dbdata:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/mongodb

  miniodata:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/minio
```

```bash
# Tạo thư mục trước khi chạy docker compose
sudo mkdir -p /data/mongodb /data/minio
sudo chown -R ubuntu:ubuntu /data
```

---

## Q: AWS yêu cầu tạo VPC — phải làm gì?

Với setup EC2 đơn giản (1 instance), **dùng Default VPC là đủ**, không cần tạo mới.

Khi AWS yêu cầu chọn VPC:
```
VPC: vpc-xxxxxxxx (default) ← chọn cái này
```

**Nếu không thấy Default VPC** (bị xóa nhầm):
1. Vào VPC Console → Your VPCs
2. Actions → Create default VPC

**Khi nào mới cần tạo VPC riêng?**
- Multi-AZ với Public + Private subnets
- ALB + ECS Fargate
- VPC Peering với MongoDB Atlas

---

## Q: IPv4 CIDR block cho VPC là gì?

```
10.0.0.0/16
```

| CIDR          | Số IP khả dụng | Dùng cho              |
|---------------|----------------|-----------------------|
| `10.0.0.0/16` | ~65,000 IP     | ✅ Standard, đủ dùng  |
| `10.0.0.0/24` | ~250 IP        | Quá nhỏ               |

---

## Q: IPv4 subnet CIDR block là gì?

```
10.0.1.0/24
```

```
VPC:    10.0.0.0/16   → toàn bộ "khu đất"
Subnet: 10.0.1.0/24   → 1 "lô đất" trong khu (~250 IP)
```

Nếu cần thêm subnet sau: `10.0.2.0/24`, `10.0.3.0/24`...
