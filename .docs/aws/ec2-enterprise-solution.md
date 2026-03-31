# Enterprise Solution — Notesnook Sync Server trên AWS

## Vấn đề với kiến trúc single EC2

Trước khi đề xuất, cần hiểu rõ **giới hạn của plan hiện tại:**

| Vấn đề | Rủi ro với doanh nghiệp |
|--------|------------------------|
| Single EC2 = single point of failure | Downtime khi instance crash/maintenance |
| MongoDB self-hosted single node | Data loss nếu EBS hỏng |
| MinIO single node | Attachment mất nếu volume hỏng |
| No autoscaling | Quá tải khi nhiều user sync đồng thời |
| Manual SSL renewal | Cert expire → toàn bộ app down |

---

## Kiến trúc đề xuất — Production Grade

```
Internet
    │
    ▼
[Route 53] ──── DNS + Health Check Failover
    │
    ▼
[ACM] ──────── SSL/TLS (tự động renew)
    │
    ▼
[Application Load Balancer] ── HTTPS termination, WAF optional
    │
    ├─── /api/*     ──► [ECS Fargate] notesnook-server    (2 tasks)
    ├─── /auth/*    ──► [ECS Fargate] identity-server     (2 tasks)
    ├─── /sse/*     ──► [ECS Fargate] sse-server          (2 tasks)
    └─── /mono/*    ──► [ECS Fargate] monograph-server    (1 task)
              │
              ├── [MongoDB Atlas M10+]  ── Multi-AZ, managed backup
              ├── [Amazon S3]           ── thay MinIO, 99.999999999% durability
              └── [ElastiCache Redis]   ── SignalR backplane (scale ngang)
```

---

## Chi tiết từng thành phần

### 1. Compute — ECS Fargate (thay EC2)
- **Không quản lý server**, AWS lo patching/OS
- **Auto scaling** theo CPU/memory metrics
- Task sizing khuyến nghị:

| Service | CPU | RAM | Min tasks | Max tasks |
|---------|-----|-----|-----------|-----------|
| notesnook-server | 1 vCPU | 2GB | 2 | 6 |
| identity-server | 0.5 vCPU | 1GB | 2 | 4 |
| sse-server | 0.5 vCPU | 1GB | 2 | 4 |
| monograph-server | 0.25 vCPU | 512MB | 1 | 2 |

### 2. Database — MongoDB Atlas (thay self-hosted)
- **Tier:** M10 (2 vCPU, 2GB RAM) — minimum production
- **M20** nếu >50 concurrent users
- Multi-AZ replica set sẵn, automated backup, point-in-time restore
- VPC Peering với AWS VPC → không qua internet

### 3. Object Storage — Amazon S3 (thay MinIO)
- 11 nines durability, không cần quản lý
- Cần update env: `S3_INTERNAL_SERVICE_URL`, `S3_ACCESS_KEY_ID`, `S3_ACCESS_KEY`
- Bật **S3 Versioning** để recover attachment bị xóa nhầm

### 4. Cache — ElastiCache Redis (cho SignalR backplane)
- **cache.t4g.small** — đủ cho SignalR pub/sub
- Kích hoạt SignalR Redis backplane trong `notesnook-server` để scale ngang

### 5. Load Balancer — Application Load Balancer
- HTTPS termination với ACM cert (tự động renew)
- Sticky sessions cho SSE connections (`lb_cookie` stickiness)
- Health check tích hợp sẵn với ECS

### 6. Networking — VPC thiết kế chuẩn

```
VPC 10.0.0.0/16
├── Public Subnets  (AZ-a, AZ-b) ── ALB, NAT Gateway
└── Private Subnets (AZ-a, AZ-b) ── ECS Tasks, Redis
                                     MongoDB Atlas VPC Peer
```

---

## Ước tính chi phí (ap-southeast-1)

| Resource | Spec | ~$/tháng |
|----------|------|----------|
| ECS Fargate | ~4 vCPU + 4.5GB total | ~$60 |
| MongoDB Atlas M10 | Multi-AZ | ~$57 |
| Amazon S3 | 100GB + requests | ~$3 |
| ElastiCache t4g.small | Redis | ~$25 |
| ALB | + LCU | ~$20 |
| NAT Gateway | 2 AZs | ~$65 |
| Route 53 | Hosted zone + queries | ~$2 |
| **Total** | | **~$232/tháng** |

> Có thể giảm ~20% bằng Savings Plans 1 năm cho Fargate + ElastiCache.

---

## Migration Path từ plan single EC2

Nếu muốn **bắt đầu nhanh** rồi nâng dần:

```
Giai đoạn 1 (tuần 1–2):  Single EC2 t3.large → ổn định, validate app
        ↓
Giai đoạn 2 (tháng 1):   Migrate MongoDB → Atlas, MinIO → S3
        ↓
Giai đoạn 3 (tháng 2):   Chuyển EC2 → ECS Fargate + ALB
        ↓
Giai đoạn 4 (tháng 3):   Thêm autoscaling, WAF, CloudWatch alerts
```

---

## Câu hỏi cần xác định trước khi chọn

1. **Bao nhiêu user / concurrent sessions?** → ảnh hưởng task sizing
2. **RTO/RPO yêu cầu?** (downtime chấp nhận được bao lâu?)
3. **Data residency?** — dữ liệu có được lưu ngoài VN không?
4. **Team có DevOps?** — nếu không, Fargate dễ hơn EC2 nhiều
5. **Budget tháng?** — có thể optimize về $150–180 nếu cần
