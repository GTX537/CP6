# W385-01 · Identity Priority v5 原子 staging

DEV-2 于 2026-11-09 11:38:59 UTC 从 #828 `9968ffccfacf11dfaa6fc895ad04aafdc206ede9` 开工。采用 W380 实际 v5 原包，复用 W382 的物理隔离、原 tenant schema、journal、来源与可见性校验。[W380 原归档](../W380-01/DELIVERY.md) · [W382 v4 staging](../W382-01/DELIVERY.md)

`stage.py` 在现有 PostgreSQL 单事务中将四个 v5 原成员用 `COPY HEADER MATCH` 装载到私有 `rma_archive_stage` 的四张同名中性表。保留原列顺序、PostgreSQL 类型、列可空性、原租户 UUID、原 `Id` 主键和八字节 RowVersion。BPCHAR 使用 v3 已建立的同源 ICU collation，无 typmod 补位。Outbox 的全局 MessageId、Inbox 的全局 ConsumerName/MessageId 与 checkpoint 的 ConsumerName/TenantId/AggregateId 继续独立。提交前核对四类行数，仅 v5 `STAGED` 回执增加 `identityPriorityCounts`。[实现](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [原 Identity Priority 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/IdentityPriority/20261002151342_PostgreSqlIdentityPriorityBaselineV1.cs)

## 实库验证

唯一纯原生 PostgreSQL 18.6 环回实例，端口 55567。`test-native-01.log` 退出 0。实际原件为 `D:/CP6-local-test/CP6-72H-20261109/W385-01/rma-stage-v5-20261109T114222-55429b03`。使用一个实例的两个隔离数据库，无 .NET 构建或运行。A `v5-cut` manifest SHA-256 为 `2fccba4c841598f66a8af29dad5be8714d5069d42c1d43cc2d72d655f58b2290`，B `v5-cli-b` 为 `d4d942cc7ba798f27636008271c43ed9619384d31dcb5545725bd93aa80718a4`。

- 实现前旧 CLI 对 A 原包在连接前退出 1，返回 `rma-stage-bundle-member-rejected`，保留 `test-red.log`。
- 保持 A 可信 pin 和原件，在 native driver 已完成全部 COPY 后将一条 Outbox 的 MessageId 改成另一条的大小写和尾空格变体。backend 16028、xid 757 收到原生 23505 `Cp6_OutboxMessage_MessageId_key`，事务回滚，目标无残留 schema，backend 26772 的只读查询无回执。
- 同库、同租户、同 pin 的正常 CLI 重试由 backend 1832、xid 758 提交。夹具丢弃 853 字节 stdout，backend 41008 只读返回原持久回执。带换行的恢复输出 SHA-256 为 `17c1f701a64d919479f7a3be7fbecf622f633300668554e0face718b1a945e76`。四表行数为 1 dead-letter、1 checkpoint、1 inbox、2 outbox，`recoveryReady=false`。
- A 四个 CSV 经原生 UTC COPY 回出，逐字节等于 W380 原成员，包括二进制 payload、摘要、NULL、租约、时间和 RowVersion。实际 PostgreSQL catalog 核对四个原 Id 主键和三个原唯一键作用域，原 journal、tenant ACL 和隔离状态通过。B 在另一隔离库正常提交，dead-letter 保留原表头和 0 行，其他三表各 1 行。B dead-letter 回出摘要为 `a472602823f0b06da26192b302fff6f9b9f6f090095bc0323739e2b54e89a6f2`。
- 实物 v1–v4 的八份 stage/recover driver SQL 均与 #828 逐字节相等，含原回执写入和读取。结果在 `test-legacy-sql.log`，原实库兼容证据复用 W382。任务 postmaster 57904 已停止，原生 `pg_ctl status` 为 3，55567 无监听。

## 复现和范围

使用 W380 已导出的两个原包、空闲环回端口及任务专属证据目录。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_identity_priority_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    $v5 = "$b/W380-01/identity-priority-20261109T111628-589aa6fd"
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_identity_priority_stage.py' --root "$b/W385-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55567 --a-bundle "$v5/v5-cut/bundle" --a-sha256 2fccba4c841598f66a8af29dad5be8714d5069d42c1d43cc2d72d655f58b2290 --b-bundle "$v5/v5-cli-b/bundle" --b-sha256 d4d942cc7ba798f27636008271c43ed9619384d31dcb5545725bd93aa80718a4

本窗交付私有 staging 和原回执恢复。当前授权、实时消息处理、派送、route/dispatcher 水位、对账与正式 PGT-021/022 恢复验收分窗。分支 `codex/w385-01` 本地保存，DEV-2 工作树干净，远端采用和席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
