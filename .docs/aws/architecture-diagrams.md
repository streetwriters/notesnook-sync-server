# Architecture Diagrams — Notesnook Sync Server on AWS

## Diagram 1: EC2 t3.medium — Single Instance (Demo/UAT)

```
                        INTERNET
                            │
                    ┌───────▼────────┐
                    │   Route 53 /   │
                    │   DNS Provider │
                    │                │
                    │ api.domain.com ├──────────────────┐
                    │auth.domain.com ├───────────────┐  │
                    │ sse.domain.com ├────────────┐  │  │
                    │  s3.domain.com ├─────────┐  │  │  │
                    └────────────────┘         │  │  │  │
                                               │  │  │  │
┌──────────────────── AWS ap-southeast-1 ──────┼──┼──┼──┼─────────────────┐
│                                              │  │  │  │                  │
│  ┌──────────────── EC2 t3.medium ───────────▼──▼──▼──▼──────────────┐  │
│  │                  2 vCPU / 4GB RAM           Elastic IP            │  │
│  │                                                                    │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │                    NGINX (reverse proxy)                     │  │  │
│  │  │          Port 80 (redirect) / Port 443 (SSL - Certbot)       │  │  │
│  │  └──────┬──────────────┬────────────────┬───────────────┬───────┘  │  │
│  │         │              │                │               │           │  │
│  │  ┌──────▼──────┐ ┌─────▼──────┐ ┌──────▼─────┐ ┌──────▼──────┐   │  │
│  │  │Notesnook.API│ │ Identity   │ │ Messenger  │ │    MinIO    │   │  │
│  │  │  .NET 9     │ │  .NET 9    │ │  .NET 9    │ │  S3-compat  │   │  │
│  │  │  port 5264  │ │  port 8264 │ │  port 7264 │ │  port 9000  │   │  │
│  │  │             │ │            │ │            │ │             │   │  │
│  │  │ SignalR Hub │ │ OAuth2 /   │ │ SSE events │ │ Attachment  │   │  │
│  │  │ Sync logic  │ │ OpenID     │ │            │ │ storage     │   │  │
│  │  └──────┬──────┘ └─────┬──────┘ └────────────┘ └──────┬──────┘   │  │
│  │         │              │                               │           │  │
│  │  ┌──────▼──────┐       │         ┌─────────────┐      │           │  │
│  │  │  Inbox API  │       │         │   MongoDB   │◄─────┘           │  │
│  │  │  Bun/TS     │       └────────►│  port 27017 │                  │  │
│  │  │  port 3000  │                 │  notes/users│                  │  │
│  │  └─────────────┘                 └──────┬──────┘                  │  │
│  │                                         │                          │  │
│  └─────────────────────────────────────────┼──────────────────────────┘  │
│                                            │                              │
│  ┌─────────────────────────────────────────▼──────────────────────────┐  │
│  │                        EBS Volumes (gp3)                            │  │
│  │                                                                     │  │
│  │   ┌──────────────────┐          ┌───────────────────────────────┐  │  │
│  │   │  Root Volume     │          │       Data Volume             │  │  │
│  │   │  30 GB           │          │       50 GB                   │  │  │
│  │   │  OS + Docker +   │          │  /data/mongodb  (notes data)  │  │  │
│  │   │  App images      │          │  /data/minio    (attachments) │  │  │
│  │   └──────────────────┘          └───────────────────────────────┘  │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │  S3 Bucket (backup only — không phải storage chính)                  │ │
│  │  mongodump + minio sync → chạy 2AM hàng ngày qua cron               │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘

Chi phí:  EC2 ~$35  +  EBS ~$6.40  +  S3 backup ~$0.23  ≈  $42/tháng
```

---

## Diagram 2: DEV + UAT (Phương án A — 2 EC2 riêng)

```
┌──────────────── AWS ap-southeast-1 ─────────────────────────────────────┐
│                                                                           │
│   ┌─────────────── DEV ───────────────┐  ┌──────────── UAT ───────────┐ │
│   │  EC2 t3.small  (~$15/tháng)       │  │ EC2 t3.medium (~$35/tháng) │ │
│   │  Auto stop 8PM / start 8AM        │  │ Always on (office hours)   │ │
│   │                                   │  │                            │ │
│   │  dev-api.yourdomain.com           │  │  uat-api.yourdomain.com    │ │
│   │  dev-auth.yourdomain.com          │  │  uat-auth.yourdomain.com   │ │
│   │  dev-sse.yourdomain.com           │  │  uat-sse.yourdomain.com    │ │
│   │  dev-s3.yourdomain.com            │  │  uat-s3.yourdomain.com     │ │
│   │                                   │  │                            │ │
│   │  Full stack (Docker Compose)      │  │  Full stack (Docker Compose│ │
│   │  EBS 30GB data                    │  │  EBS 50GB data             │ │
│   └───────────────────────────────────┘  └────────────────────────────┘ │
│                                                                           │
│   Chi phí DEV (với schedule):  ~$4 compute + $3 EBS  =  ~$7/tháng       │
│   Chi phí UAT:                ~$35 compute + $6 EBS  =  ~$41/tháng      │
│   ─────────────────────────────────────────────────────────────────────  │
│   Tổng:                                                  ~$48/tháng      │
└───────────────────────────────────────────────────────────────────────────┘

Workflow:
  Developer  →  push code  →  DEV  →  QA test  →  UAT  →  Demo khách hàng
```

---

## Diagram 3: Enterprise (Production Grade)

```
                        INTERNET
                            │
                    ┌───────▼────────┐
                    │    Route 53    │
                    │ Health Check   │
                    │   Failover     │
                    └───────┬────────┘
                            │
                    ┌───────▼────────┐
                    │   ACM (SSL)    │
                    │  Auto-renew    │
                    └───────┬────────┘
                            │
                    ┌───────▼────────────────┐
                    │  Application Load      │
                    │  Balancer (ALB)        │
                    │  WAF optional          │
                    └──┬─────┬──────┬────────┘
                       │     │      │
             ┌─────────▼─┐ ┌─▼────┐ ┌▼──────────┐
             │ECS Fargate│ │ ECS  │ │    ECS    │
             │Notesnook  │ │ Iden-│ │ Messenger │
             │API 2 tasks│ │ tity │ │  2 tasks  │
             │1vCPU/2GB  │ │2task │ │0.5vCPU/1GB│
             └─────┬─────┘ └──┬───┘ └─────┬─────┘
                   │          │            │
        ┌──────────▼──────────▼────────────▼──────────┐
        │              Private Subnets                  │
        │                                               │
        │  ┌──────────────────┐  ┌───────────────────┐ │
        │  │  MongoDB Atlas   │  │ ElastiCache Redis  │ │
        │  │  M10+ Multi-AZ   │  │  t4g.small        │ │
        │  │  Auto backup     │  │  SignalR backplane │ │
        │  │  VPC Peering     │  └───────────────────┘ │
        │  └──────────────────┘                         │
        │                                               │
        │  ┌──────────────────┐                         │
        │  │   Amazon S3      │                         │
        │  │  (thay MinIO)    │                         │
        │  │  11x9 durability │                         │
        │  └──────────────────┘                         │
        └───────────────────────────────────────────────┘

Chi phí: ~$232/tháng  (có thể giảm ~20% với Savings Plans 1 năm)
```

---

## So sánh 3 môi trường

```
                    DEV                 UAT / Demo          PROD (Enterprise)
─────────────────────────────────────────────────────────────────────────────
Instance:       t3.small            t3.medium           ECS Fargate
RAM:            2 GB                4 GB                4.5 GB total tasks
Storage:        EBS 30 GB           EBS 50 GB           Amazon S3
Database:       MongoDB (Docker)    MongoDB (Docker)    MongoDB Atlas M10+
Cache:          không               không               ElastiCache Redis
SSL:            Certbot             Certbot             ACM (auto-renew)
Uptime:         8AM–8PM weekdays    Office hours / on   24/7
Chi phí:        ~$7/tháng           ~$41/tháng          ~$232/tháng
─────────────────────────────────────────────────────────────────────────────
Tổng (3 env):                                           ~$280/tháng
```

---

## Migration Path

```
Tuần 1–2:   UAT (t3.medium)       →  validate app, demo khách hàng
Tháng 1:    + DEV (t3.small)      →  developer environment
Tháng 2:    Migrate DB → Atlas    →  MongoDB Atlas + S3 thay MinIO
Tháng 3:    EC2 → ECS Fargate     →  autoscaling, ALB, WAF
Tháng 4:    Monitoring            →  CloudWatch alerts, Grafana
```
