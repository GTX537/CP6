# W561-01 · 采购申请按供应商转单

2026-11-14 UTC。Main 基线 `e61a10186912eff5662adb39dd3985a53198cb38`，受测源码 `bd924c378b487e1843c09e3ca270171c5a58b624`。隔离 PostgreSQL 18.6、原 Main Kestrel 和真实 IAM，使用模拟采购申请、供应商和租户用户。 [Issue #1048](https://github.com/GTX537/CP6/issues/1048)

现有采购申请转单继续按建议供应商分组。每组在 Main 的共同根内核对当前会话、动作和仍未转出的申请行。一个 Core PostgreSQL 事务提交该组采购头、明细、编号、申请行 `ConvertedPoNo` 及原申请状态。无建议供应商的有效行继续保留，最后一组在全行均已转出时才推进 Converted。 [实现](../../../../CP6.Core/Services/Pur/PurchaseRequestService.ConvertPostgreSql.cs)

- 基线两项竞争均失败。撤权持有共同根时原申请请求提前返回两张采购单；申请行写入等待时原撤权可先完成。
- 修复后撤权先提交时，请求返回 403，申请状态、采购单、行和编号保持原值。第一组先取得根时，撤权等待同一物理事务，订单、明细、编号和来源回填保留，第二组拒绝。原数量 `3.12345678`、估价 `12.1234` 和交期保持。再次用旧授权拒绝；重授后只转换未转组，首单原 ID 保留。
- 在 PostgreSQL 的申请行加测试约束让回填失败，HTTP 返回 503，组内已执行的采购头、明细和编号随事务回滚。移除测试约束后，同一请求正常完成两个组。实测阻塞使用两条独立 PostgreSQL 连接和 `pg_blocking_pids`。撤权证据含角色版本、代次、快照、优先 Outbox、强审计和原回执。 [回归](../../../../CP6.Tests/Erp/Pgt017PrConversionPostgreSqlTests.cs)
- 三项 PostgreSQL、56 项申请、询价、审批和采购路由组件全部通过，零失败、零跳过。构建零错误，既有告警保留。本地 `publish` 310 文件，WebApi SHA-256 `7d27f7b36245bf4f35d153b95f4f026aa2c7661728b8445cd4973f845e037b66`，与受测宿主一致。隔离发布建库退出 0、存活 200、匿名管理及询价 401。原始 TRX、日志和摘要在 `D:/CP6-test-artifacts/W561-01`。本任务隔离实例均已停止，Actions 启动、重跑、取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。现有转单沿用申请来源标记；独立的正式 PR 份额协议、其他采购写者、旧 OA、财务、独立 Space Design、Stock 普通数据库身份、两库恢复及传输仍待验证。采购送审会触发旧 OA 通知及回调，需沿自己的提交路径核对。分支、工作区及席位处置见远端 PR 和 `main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 跟踪](https://github.com/GTX537/CP6/issues/1012)
