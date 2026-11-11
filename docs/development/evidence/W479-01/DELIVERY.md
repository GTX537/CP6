# W479-01 — 实际 v11 缺件拒绝与闭合回执恢复

从远端主线 3cf68f41be9ac3267b441218e85706cb814b7d9d 复用 DEV-2。启动 W478 已停机源的 PostgreSQL 18.6 隔离副本，使用 W477 原 v11、原文件根和固定 manifest SHA-256 f0d711a4b488e2df294364dacb8c1151f2c01036b84c24e35eed60b53cdd5e07。来源的 A ERP 原行已 Published，B 仍 Pending。本窗执行真实 pg_dump、现有 stage/recover、当前只读 ERP 对账和原生 COPY。[W477 四域原归档](../W477-01/DELIVERY.md) · [W478 当前状态](../W478-01/DELIVERY.md) · [暂存入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

| 输入 | 实际拒绝 | PostgreSQL 暂存驱动 |
| --- | --- | --- |
| 实际单租户 schema-only pg_dump | rma-stage-bundle-member-rejected | 未执行 |
| 缺少有一条原消息的 ERP Outbox CSV | rma-stage-bundle-member-rejected | 未执行 |
| 原文件根缺少索引指向的内容 | file-content-asset-not-found | 未执行 |
| 同一物理源库作为目标 | rma-stage-target-is-source | 未执行 |
| 采用同租户另一真实 v11 的 pin 读取回执 | rma-stage-receipt-not-found | 只读查询 |

四个早期拒绝后，同一新建中立库 w479_neutral、OID 45249 的表数均为零。随后完整 v11 在此库实际暂存，目录只含 public、租户恢复 schema 和 rma_archive_stage，无活动 ERP 或 Stock schema、公共 CONNECT 授权或恢复准入。原生 COPY 回读的暂存 ERP Outbox CSV 与归档逐字节一致，仍是原 Pending，SHA-256 e87e3ed4dbe3da2a6e8c316a0c348dc2e0dfbf14747085f194a62859c87c8d3c。

正确 pin 的 stage/recover 回执逐字节一致，共 11567 字节，SHA-256 83f6a3448709fe5982c24ec4e62486b14f8d1093932e76f881babaa10d6b1d3f。错 pin 使用的是 W476 实际 v11 f4a4dee5dea87c497614b20acf23bb8a8aac5a8d83aacc704651348c693a034f。前后 PostgreSQL 目录和已保存回执保持一致。对账继续观测原 Pending、当前 Published 和 PRESERVE_CURRENT。A/B 原 ERP 八表、Stock 当前读数和 RMA 原流水逐字节或按原读数一致。原 v11 的 41 份成员及外部文件未改变。

首次私有比较把 Windows psql 多出的 CR 计入回执。实际 stage 与 recover 的输出已一致。复验只排除 psql 输出行尾的 CR，确认已存 JSON、CLI 回执与目录一致。两次隔离数据库均已停机。原始输出、退出和目录证据保存在 D:/CP6-local-test/CP6-72H-20261109/W479-01。

W478 经 #946 采用并释放，实测 9.42–9.49 分钟。本窗覆盖 PGT-022 的缺件、物理目标和原回执拒绝场景。现行撤权、全局键和 route epoch、完整候选普通角色及批准切换仍待对应验收。保持 recoveryReady=false，原 375 项 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。GitHub Actions 和部署未运行。
