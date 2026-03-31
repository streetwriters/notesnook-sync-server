# Architecture Diagrams (Mermaid) — Notesnook Sync Server on AWS

> Paste từng block vào [mermaid.live](https://mermaid.live) để xem và lấy shareable link.

---

## Diagram 1: EC2 t3.medium — Single Instance (Demo/UAT)

```mermaid
graph TD
    Internet(["Internet"])

    subgraph DNS["Route 53 / DNS Provider"]
        dns["api.domain.com\nauth.domain.com\nsse.domain.com\ns3.domain.com"]
    end

    subgraph AWS["AWS ap-southeast-1"]
        subgraph EC2["EC2 t3.medium — 2 vCPU / 4GB RAM  |  Elastic IP"]
            Nginx["NGINX Reverse Proxy\nPort 80 → 443  |  SSL Certbot"]

            subgraph Docker["Docker Compose"]
                API["Notesnook.API\n.NET 9 / port 5264\nSignalR Hub + Sync"]
                Identity["Identity Server\n.NET 9 / port 8264\nOAuth2 / OpenID"]
                Messenger["Messenger\n.NET 9 / port 7264\nSSE Events"]
                MinIO["MinIO\nport 9000\nAttachment Storage"]
                Inbox["Inbox API\nBun/TS / port 3000"]
                MongoDB[("MongoDB\nport 27017\nnotes / users")]
            end
        end

        subgraph EBS["EBS Volumes — gp3"]
            RootVol["Root Volume 30GB\nOS + Docker + App images"]
            DataVol["Data Volume 50GB\n/data/mongodb\n/data/minio"]
        end

        subgraph S3Backup["S3 Bucket — backup only"]
            backup["mongodump + minio sync\nCron 2AM daily"]
        end
    end

    Internet --> DNS
    DNS --> Nginx
    Nginx --> API
    Nginx --> Identity
    Nginx --> Messenger
    Nginx --> MinIO
    API --> MongoDB
    Identity --> MongoDB
    MinIO --> DataVol
    MongoDB --> DataVol
    DataVol --> backup

    classDef aws fill:#FF9900,color:#000,stroke:#FF9900
    classDef dotnet fill:#512BD4,color:#fff,stroke:#512BD4
    classDef db fill:#13AA52,color:#fff,stroke:#13AA52
    classDef storage fill:#569A31,color:#fff,stroke:#569A31
    classDef nginx fill:#009639,color:#fff,stroke:#009639

    class AWS,EBS,S3Backup aws
    class API,Identity,Messenger,Inbox dotnet
    class MongoDB db
    class MinIO,DataVol,RootVol storage
    class Nginx nginx
```

**Chi phí:** EC2 ~$35 + EBS ~$6.40 + S3 backup ~$0.23 ≈ **$42/tháng**

---

## Diagram 2: DEV + UAT (2 EC2 riêng)

```mermaid
graph LR
    Dev(["Developer"])
    Customer(["Khach hang / CTO"])

    subgraph AWS["AWS ap-southeast-1"]
        subgraph DEV["EC2 t3.small — DEV\n~$7/thang  |  Auto stop 8PM / start 8AM"]
            dev_nginx["NGINX + Certbot\ndev-api / dev-auth\ndev-sse / dev-s3"]
            dev_stack["Full Stack\nDocker Compose\n5 services"]
            dev_ebs[("EBS 30GB\n/data/mongodb\n/data/minio")]
            dev_nginx --> dev_stack --> dev_ebs
        end

        subgraph UAT["EC2 t3.medium — UAT\n~$41/thang  |  Always on"]
            uat_nginx["NGINX + Certbot\nuat-api / uat-auth\nuat-sse / uat-s3"]
            uat_stack["Full Stack\nDocker Compose\n5 services"]
            uat_ebs[("EBS 50GB\n/data/mongodb\n/data/minio")]
            uat_nginx --> uat_stack --> uat_ebs
        end
    end

    PROD(["PROD\nsau nay"])

    Dev -->|"push code + test"| DEV
    DEV -->|"QA approve\ngit tag release"| UAT
    UAT -->|"CTO approve\ndemo pass"| PROD
    Customer -->|"review / demo"| UAT

    classDef aws fill:#FF9900,color:#000,stroke:#FF9900
    classDef env fill:#1A73E8,color:#fff,stroke:#1A73E8
    classDef db fill:#13AA52,color:#fff,stroke:#13AA52

    class AWS aws
    class DEV,UAT env
    class dev_ebs,uat_ebs db
```

**Chi phí:** DEV ~$7 + UAT ~$41 = **~$48/tháng**

---

## Diagram 3: Enterprise (Production Grade)

```mermaid
graph TD
    Internet(["Internet"])

    subgraph AWS["AWS ap-southeast-1"]
        Route53["Route 53\nHealth Check + Failover"]
        ACM["ACM — SSL/TLS\nAuto-renew"]
        ALB["Application Load Balancer\nHTTPS termination  |  WAF optional"]

        subgraph Fargate["ECS Fargate — Public Subnets  |  Auto Scaling"]
            API["Notesnook.API\n2 tasks  |  1 vCPU  |  2GB"]
            Identity["Identity Server\n2 tasks  |  0.5 vCPU  |  1GB"]
            Messenger["Messenger\n2 tasks  |  0.5 vCPU  |  1GB"]
        end

        subgraph Private["Private Subnets — Multi-AZ"]
            Atlas[("MongoDB Atlas M10+\nMulti-AZ replica set\nAuto backup + PITR\nVPC Peering")]
            Redis["ElastiCache Redis\nt4g.small\nSignalR backplane"]
            S3["Amazon S3\n99.999999999% durability\nthay MinIO  |  Versioning ON"]
        end
    end

    Internet --> Route53
    Route53 --> ACM
    ACM --> ALB
    ALB --> API
    ALB --> Identity
    ALB --> Messenger
    API --> Atlas
    Identity --> Atlas
    API --> Redis
    Messenger --> Redis
    API --> S3

    classDef aws fill:#FF9900,color:#000,stroke:#FF9900
    classDef compute fill:#1A73E8,color:#fff,stroke:#1A73E8
    classDef db fill:#13AA52,color:#fff,stroke:#13AA52
    classDef cache fill:#DC382D,color:#fff,stroke:#DC382D
    classDef storage fill:#569A31,color:#fff,stroke:#569A31
    classDef lb fill:#8C4FFF,color:#fff,stroke:#8C4FFF

    class AWS,Private aws
    class Fargate,API,Identity,Messenger compute
    class Atlas db
    class Redis cache
    class S3 storage
    class ALB,Route53,ACM lb
```

**Chi phí:** ~$232/tháng (giảm ~20% với Savings Plans 1 năm)

---

## Diagram 4: Migration Path

```mermaid
graph LR
    W1["Tuan 1-2\nUAT t3.medium\nDemo khach hang\n~$41/thang"]
    M1["Thang 1\n+ DEV t3.small\nDeveloper env\n~$48/thang"]
    M2["Thang 2\nMigrate DB\nMongoDB Atlas\n+ S3 thay MinIO"]
    M3["Thang 3\nEC2 → ECS Fargate\n+ ALB + Redis\n~$232/thang"]
    M4["Thang 4\nMonitoring\nCloudWatch\nGrafana alerts"]

    W1 -->|"demo pass"| M1
    M1 -->|"scale can thiet"| M2
    M2 --> M3
    M3 --> M4

    classDef phase fill:#1A73E8,color:#fff,stroke:#1A73E8
    classDef prod fill:#13AA52,color:#fff,stroke:#13AA52

    class W1,M1 phase
    class M2,M3,M4 prod
```

---

## So sánh 3 môi trường

```mermaid
graph LR
    subgraph DEV["DEV — t3.small\n~$7/thang"]
        d1["2 vCPU / 2GB RAM"]
        d2["EBS 30GB"]
        d3["MongoDB Docker"]
        d4["Certbot SSL"]
        d5["8AM-8PM weekdays"]
    end

    subgraph UAT["UAT / Demo — t3.medium\n~$41/thang"]
        u1["2 vCPU / 4GB RAM"]
        u2["EBS 50GB"]
        u3["MongoDB Docker"]
        u4["Certbot SSL"]
        u5["Always on"]
    end

    subgraph PROD["PROD — ECS Fargate\n~$232/thang"]
        p1["4.5GB total tasks"]
        p2["Amazon S3"]
        p3["MongoDB Atlas M10+"]
        p4["ACM auto-renew"]
        p5["24/7 + autoscaling"]
    end

    DEV -->|"promote"| UAT
    UAT -->|"go live"| PROD
```
