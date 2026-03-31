# Kiểm tra thông số Server AWS EC2 (Ubuntu)

## Các lệnh cốt lõi

| Lệnh | Kiểm tra cái gì? |
|---|---|
| `lsblk` | Danh sách ổ đĩa + EBS volumes + mountpoint |
| `df -h` | Dung lượng thực tế đang dùng (%) |
| `free -h` | RAM: total / used / free / available |
| `nproc` | Số CPU cores |
| `uptime` | Thời gian chạy + load average |

**Chạy tất cả cùng lúc:**
```bash
echo "=== BLOCK DEVICES ===" && lsblk && \
echo "=== DISK USAGE ===" && df -h && \
echo "=== MEMORY ===" && free -h && \
echo "=== CPU ===" && nproc && \
echo "=== UPTIME ===" && uptime
```

---

## Đọc `lsblk` — Bản đồ ổ đĩa

```
nvme0n1      50G  ← EBS Volume (root)
├─nvme0n1p1  49G  /         ← Partition chính, chứa OS + data
├─nvme0n1p14  4M            ← BIOS boot (đừng đụng)
├─nvme0n1p15 106M /boot/efi
└─nvme0n1p16 913M /boot

nvme1n1      60G            ← EBS Volume thứ 2, KHÔNG có mountpoint
                               → chưa format, chưa dùng được
```

**Quy tắc nhớ:**
- `nvme` = NVMe SSD (AWS thế hệ mới, nhanh hơn `xvd*`)
- Có `MOUNTPOINTS` = đang dùng được
- Không có `MOUNTPOINTS` = volume "trần", cần format + mount

---

## Đọc `df -h` — Mức dùng thực tế

```
/dev/root   48G   2.2G used  46G free   5%
```

- `df` khác `lsblk`: `lsblk` cho thấy **cấu trúc**, `df` cho thấy **bao nhiêu % đã dùng**
- Ngưỡng cảnh báo: **> 80%** nên mở rộng hoặc dọn dẹp

---

## Đọc `free -h` — RAM

```
               total    used    free   available
Mem:           3.7Gi   438Mi   3.1Gi    3.3Gi
Swap:             0B      0B      0B
```

- **`available`** mới là con số quan trọng (RAM thực sự có thể dùng, bao gồm cả cache)
- **Swap = 0** → Nếu RAM đầy, process bị kill (OOM Killer). Nên thêm swap nếu RAM < 4GB

---

## Đọc `uptime` — Sức khỏe server

```
15:31:49 up 14 min,  1 user,  load average: 0.00, 0.00, 0.00
                                             ^1min ^5min ^15min
```

**Load average so với số CPU (`nproc`):**

| Load vs nproc | Trạng thái |
|---|---|
| `load < nproc` | Bình thường |
| `load ≈ nproc` | Đang căng |
| `load > nproc` | Quá tải |

---

## Snapshot server ip-10-0-1-78

| Thành phần | Giá trị | Đánh giá |
|---|---|---|
| OS | Ubuntu (EC2) | - |
| IP (private) | 10.0.1.78 | Subnet riêng |
| Root EBS (nvme0n1) | 50GB / dùng 5% | OK |
| Data EBS (nvme1n1) | 60GB chưa mount | Cần setup |
| RAM | 3.7GB / dùng 12% | OK |
| CPU | 2 vCPUs | t3.medium hoặc tương đương |
| Swap | Không có | Nên thêm |
| Uptime | 14 phút | Server mới boot |

---

## Checklist server mới — Việc cần làm tiếp theo

```
[ ] Mount nvme1n1 (60GB data disk)
[ ] Thêm swap file (khuyên 2-4GB)
[ ] Cài Docker / Docker Compose
[ ] Setup firewall (ufw hoặc Security Group)
[ ] Deploy ứng dụng
```
