```bash
aws ec2 describe-instances \
    --query "Reservations[*].Instances[*].[InstanceId,State.Name,PublicIpAddress,Tags[?Key=='Name'].Value|[0]]" \
    --output table
```


```
    ❯ ------------------------------------------------------------------
    |                         DescribeInstances                        |
    +----------------------+----------+---------------+----------------+
    |  i-02672bfe496225644 |  running |  54.254.2.136 |  NotesnookMac  |
    +----------------------+----------+---------------+----------------+
```

```bash
 aws ec2-instance-connect ssh \
    --instance-id i-02672bfe496225644 \
    --os-user ubuntu
```