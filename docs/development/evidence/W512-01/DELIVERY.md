# W512-01 · 询盘配置 Owner 的实际 PostgreSQL 共同事务

采用 [PGT-013](../../acceptance/postgresql-tenancy.json) 的现行询盘配置 Owner 组合。CRM 测试源 `f97f28ffaada4d718ef372039b7e27eaef362131`，CRM #202 核对远端 `e71bb41514cb89c560d4d3d17f114065f74a8cf2` 与测试树一致。使用实际签名 Kestrel、.NET 8、独占 PostgreSQL 18.6 及普通 CRM runtime LOGIN。业务、身份权限输入及本地 Dapr sidecar 均为模拟。[CRM #202](https://github.com/GTX537/CP6.CRM/pull/202)

- 正常签名保存由真实 Owner 写入 `Bundles`、`Cp6_OutboxMessage`、`Audits`、`ConfigurationSaveResults`。控制连接依次阻塞四个真实 INSERT，`pg_stat_activity` 四次均观察到 PID 28960、xid 1061。独立普通连接四次都只见原写集。释放最后一处阻塞后返回原 201，独立观察见完整配置和原回执。
- 当前权限 HTTP 调用一次，在同一测试数据库观察到零普通 Owner 活动事务。四个写入阶段没有 sidecar 调用。提交后实际托管发布者调用本地 Dapr 协议 sidecar，此时回调可见已提交的业务、原回执、审计和 Outbox。
- 分别撤销实际普通角色对四表的 INSERT 权限，四次 PostgreSQL 原生 `42501` 均命中预期关系。每次回读业务、配置头、日历与规则、原回执、审计、消息/路由/绑定/门行摘要均保持。恢复授权后同键返回原 201，相同请求原样重放，只产生一套效果。
- 基线用实际独立普通后端 2876 和 3040 构造错配。挂接事务抛异常前已由另一连接提交入队门 `False→True`，原事务回滚后仍可见。配置 Outbox 改为在首条 SQL 前复用现有 Owner 事务检查。最终复测错配被明确拒绝，门 `False→False`、原配置与实际 Outbox 摘要不变。[CRM #201](https://github.com/GTX537/CP6.CRM/issues/201)

最终 `owner-phases-03.trx` 为 6 通过、0 跳过，涵盖错配、一次正常完整图、四处真实故障。首轮 RED 和中间一次定向修复通过均保留，原始 TRX 与观测在 `D:/CP6-test-artifacts/W512-01`。三次运行的独占 PostgreSQL 均已停止。Main 同类 Stock Owner 的原原子性、晚段失败、无损原键与 Outbox 证据直接复用。[Main 创建与回滚](../W336-01/DELIVERY.md) · [Main 移库故障](../W337-01/DELIVERY.md) · [Main 原共同事务](../W426-01/DELIVERY.md)

正式 PostgreSQL 由 18 PASS / 9 NOT_RUN 更新为 19 PASS / 8 NOT_RUN。原询盘维持 60/60。完整 WP02-10 的其他消费者与物理租户 schema/共享回执范围按各自任务继续。集中审查覆盖事务边界、权限故障、精确回执和网络时序。CRM 本地及远端任务分支定向清理，DEV-2 席位释放。Actions 触发和取消为零，部署为零。
