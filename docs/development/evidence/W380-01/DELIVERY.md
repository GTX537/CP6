# W380-01 · Identity Priority 四表同快照归档

DEV-1 于 2026-11-09 11:07:01 UTC 开工，登记基线 90645bc17618748e7fc86d30f270551d02168080。W379 经 #826 采用并释放后，公共入口在 aaccdd610c2772efb273a1051a213f7702bcfa87 串行接入。[W379 v4](../W379-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## v5 归档

archive.py export 在原 v4 的三个显式开关外加 --include-identity-priority，生成 cp6-rma-source-archive-v5。Identity Priority 按实际 PostgreSQL 迁移、表 owner、列类型、主键和唯一键核定原生范围。四张 crm_identity_priority 表各取精确 TenantId，按原 UUID Id 排序，保留全部原列与原值。空表仍带表头、成员摘要和零计数。[四表投影](../../../../eng/wp02-02/rma-tenant-archive/identity_priority.py) · [IdentityMessagingContext](../../../../CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs) · [四表原安全边界](../../../../CP6.Core/Persistence/Tenancy/IdentityTenantRowSecurityV1.cs)

Outbox 保留原 Payload bytea、摘要、消息状态、投递时间和 lease 字段。Inbox 保留原摘要、处理结果与时间，checkpoint 保留版本，dead-letter 保留错误及复放记录。四表原 RowVersion bytea 均保留。清单记录 Outbox 的全局 MessageId、Inbox 的全局 ConsumerName/MessageId、checkpoint 的租户复合键和各表原 Id 主键。原生 CSV 使用 UTC 和 bytea 十六进制，计数与各 COPY 独立设置 row_security=off，并沿用同一只读快照。权限不足时原生失败，完成清单不发布。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [PostgreSQL RLS](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)

## 实库

最终证据 D:/CP6-local-test/CP6-72H-20261109/W380-01/identity-priority-20261109T111628-589aa6fd，test-native-01.log 退出 0。唯一 PostgreSQL 18.6 专属实例。W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 发布 DatabaseInit PID 33372 退出 0，复用 W353/W361 的已编译 CLI。预种和正式 CLI 使用同一个数据库，依赖 SHA-256 在 inputs.json。RMA、消息和租约均为合成原生数据。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_identity_priority.py)

- A/B 实际分别写入本租户数据。B 试用 A 的原 Outbox MessageId 和 Inbox ConsumerName/MessageId 均得到 23505，同 ConsumerName/AggregateId 的两个租户 checkpoint 均成功。
- holder 59960 持有快照 00000005-00000007-1，A/B 事务 60668/32104 实际提交后，十个消费者导入同一快照。四个 Identity COPY backend 为 26780、39924、52048、18784。A 的 v5 保留 1 dead-letter、1 checkpoint、1 inbox、2 outbox。四个成员与切面前独立 native COPY 逐字节一致，切面后的四份原生输出均已变化。原 payload、lease、RowVersion、处理/复放字段和全部哈希保留在私有证据。
- B 正常 v5 CLI export/verify 通过。B 的 dead-letter 为仅表头且明确计数 0，其他三表各 1。移去空成员后实际 verify 拒绝。省略 v4 opt-in 也实际拒绝。W363/W366/W374/W379 原 v1–v4 均用当前 CLI 实际验证通过。
- W351 默认源的 Priority Outbox 未启 RLS。夹具仅在该真实表启用 FORCE RLS，低权备份登录的普通计数实际为 0。正常 v5 导出的 Identity Priority 计数原生 42501，明确指出 Cp6_OutboxMessage，完成 manifest 不存在。该证据覆盖真实 PostgreSQL 局部 RLS 故障门。
- test-red.log 保留实现前模块缺失。唯一任务 postmaster 31352 已停止，无 PID 文件，55566 无监听。

## 复现与范围

沿用已发布 DatabaseInit、已编译 CLI 和原有归档，使用空闲端口和私有证据目录。

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_identity_priority.py' --root "$b/W380-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli "$b/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --journal-cli "$b/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" --webapi "$b/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 --v1-bundle "$b/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle" --v2-bundle "$b/W366-01/rma-archive-20261109T095220-0d8f1824/v2-cut/bundle" --v3-bundle "$b/W374-01/rma-source-20261109T103956-600b02ab/v3-cut/bundle" --v4-bundle "$b/W379-01/rma-visibility-20261109T111625-8db32a6f/v4-cut/bundle"

本窗限原四表源切片。Identity 当前 IAM、其他队列域、route/dispatcher/序列水位、staging、恢复启用、对账与生产切换仍在后续，recoveryReady 为 false，正式 PGT-021/022 保持 NOT_RUN。分支 codex/w380-01，合并及 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
