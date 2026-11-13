# W527-01 · PGT-022 不完整备份与旧消息恢复

正式 PostgreSQL **23 PASS / 4 NOT_RUN**，本轮 PGT-022 为 PASS。原询盘 **60/60**。基线 `422c578f28dbc6a1afcb2fe831d61eb476525daa`，源码提交 `7028b48ecedd1f5219f4559046e0646449d3b52d`，复用 CP6 的固定 DEV-1 与 `codex/w527-01`。原任务 WP02-02 继续开放。

## 实现

Main 在正式 Platform 0.10.4 上采用数据库 Owner 显式推进 Stock 路由纪元。先提交排空，再以不同操作键和排空原回执推进。PostgreSQL 约束同时核对现行锁、原纪元、控制版本、同事务原回执和 audit。允许同一发布者的终态队列切换。Pending、Dispatching、活跃运行或未决发送阻止推进。旧终态保留原绑定，新操作采用新纪元。普通运行角色沿用现有权限。

RMA 同库恢复在源事务内核对现行路由，旧纪元或未决发送使恢复拒绝并回滚。只读对账记录备份与现行水位、当前纪元和原完成回执。Stock 纪元 1 继续使用已有 v1 归档段。较新纪元在同一 v11 外层使用 Stock v2 归档段，保留历史绑定。单笔 Pending Stock ADJ 的专用候选导入仍限纪元 1。 [操作说明](../../../../eng/wp02-02/rma-tenant-archive/LIVE-RECOVERY.md)

## 实际验收

隔离 PostgreSQL 18.6、.NET SDK 8.0.424，模拟业务数据和 transport。数据库、Owner 工具、worker、恢复和对账均实际执行。新夹具由已停机源和关闭候选复制，原目录未启动或改写。

| 场景 | 实际结果 |
| --- | --- |
| 正常 Owner 纪元与原回执 | 纪元 1 → Draining → 纪元 2，两个事务分别提交。同键重试与查询逐字节返回原回执，改变输入拒绝，缺原键为 UNKNOWN。只持 advisory lock 的直接更新以 23514 回滚，关闭候选和错误数据库 OID 拒绝 |
| 旧 v11 对账与恢复 | 原备份 Pending，当前同一原件 Published。纪元推进前后的原键、payload、原 claim ticket、发送及完成回执一致。备份水位 `1423:1423:`，读取当前水位 `1520:1520:`。旧纪元对账为 BLOCKED，恢复已开始的 A 行补写全部回滚，无成功回执 |
| 实际未决发送 | 同一 v11 的另一隔离副本使用当前 Main worker，模拟 transport 返回 UNKNOWN。重新执行同一原 request，transport 总计 1 次。现行纪元下对账为 `UNKNOWN/SEND_OUTCOME_UNKNOWN`，Owner 排空拒绝，恢复补写回滚。恢复期间零自动发送 |
| 全局消息键与当前权限 | PostgreSQL 实际全局键探针判定他租键 BLOCKED、缺失键 UNKNOWN，未暴露他租原件或改键。A 当前禁用与已撤销凭据保持，B 业务行、Stock 和 ERP 原件保持，关闭候选未开放 |
| 新纪元备份 | Stock v2 导出、摘要验证通过，原终态仍指向纪元 1。实际完整 RMA v11 导出并验证，纪元 2 的段在无 PUBLIC 授权的隔离中立库暂存、读回原回执，`recoveryReady=false` |
| 受影响回归 | Main Tests、WebApi 及 TenantStorage 的 PG 构建成功，TenantStorage 使用正式锁文件。19 项 Stock PostgreSQL 回归通过，0 失败、0 跳过。含新纪元后入队派送、旧票据拒绝、普通运行和正式包 guard 回归 |

缺件沿用 [W479 原生 schema-only、缺 ERP 共享片和未知外部资产拒绝](../W479-01/DELIVERY.md) 及 [W526 同源缺 journal、缺文件、当前 access/refresh 拒绝](../W526-01/DELIVERY.md)。当前有效 ERP 租约到 Published 的原生对账沿用 [W478](../W478-01/DELIVERY.md)。同源 v11 与 Stock effects 仍用 [W520](../W520-01/DELIVERY.md) 的 `f25588aa95ccf06bcfd90c016fe5fb0c5b61cb2137b22823232a82da44ade1e8`、`778bb5f72a0ebb016a288a4ff5265477a6c8f460fd34378b8c6381470158d764`，关闭候选引用 [W521 的五份原件](../W521-01/DELIVERY.md)。

原始记录在 `D:/CP6-test-artifacts/W527-01/`。`native-03/actual.json` 对应现行推进操作 `ccb7a2eb-83cc-487c-94f5-395eaf2ba339`、旧归档和两个全局键探针。`native-unknown/actual.json` 与 `native-epoch2-stage/actual.json` 分别记录 UNKNOWN 和新 v11 暂存。`exercise527.py`、`StockUnknownW527.cs`、`results/stock-final-02.trx` 和各停机回执可核对。应用迁移在 xUnit 由 EF 实际执行，同源副本应用同一迁移 SQL 与对应历史记录。

## 审查与交付

一次集中审查覆盖事务/锁序、旧原件、UNKNOWN、租户隔离和归档版本。早期用例误在写事务查询旧 ticket，已按原合同在独立只读事务读取并复验。首次中立暂存因保留默认 PUBLIC 权限被正确拒绝，关闭权限后重新暂存和读回通过。初始红色与失败输出均保留。

完整迁移的 Identity Directory fresh 安装专项为 1 通过、1 失败。相同失败在 W526 基线复现，新增的 Stock 函数摘要和权限已独立核对。保留 [独立缺陷 #995](https://github.com/GTX537/CP6/issues/995)。本次未运行全仓回归、其他业务模型恢复或生产部署。

正式剩余 PGT-011、PGT-017、PGT-024、PGT-025。分支、远端采用和固定席位释放凭据由本任务 PR 和 `D:/CP6-test-artifacts/W527-01/main-finish.json` 记录。本轮 GitHub Actions 触发、取消均为零。 [正式矩阵](../../acceptance/postgresql-tenancy.json)
