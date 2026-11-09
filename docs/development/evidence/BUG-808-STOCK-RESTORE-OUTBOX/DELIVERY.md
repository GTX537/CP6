# BUG-808-STOCK-RESTORE-OUTBOX

2026-11-09 07:46:30 UTC 于 DEV-2 开工，分支 `codex/bug-808-stock-restore-outbox`，登记基线 `90e69393ef976c82c871155a44d77cb56c3872c7`。W352-VERIFY 在隔离的原生备份候选上观察到源库和候选库对同一 MessageId 各派送一次。 [#808](https://github.com/GTX537/CP6/issues/808)

Stock worker 在实际队列 `DbContext` 首次查询 Authority 前核验恢复状态，并在同一实际队列事务的 `AuthorizeAsync` 重查。部署绑定只传 `PrivacyRestore.ControlConnectionString` 是否存在非空白值的标志。配置存在或实际库带恢复标记时，以 `STOCK_OUTBOX_RESTORE_DISPATCH_FORBIDDEN` 拒绝。标记读取复用原 PostgreSQL 目录查询，函数和表显式限定为 `pg_catalog`。真实隐私准入开放后，Stock 候选仍保持派送隔离。 [队列入口](../../../../CP6.Core/Services/Integration/StockOutbox/StockOutboxWorkerV1.cs) · [事务授权](../../../../CP6.Core/Services/Integration/StockOutbox/StockOutboxHostV1.cs) · [部署绑定](../../../../CP6.WebApi/Program.cs)

## 实际验证

- Windows、.NET SDK 8.0.424、PostgreSQL 18.6，独占端口 `55561`。使用既有夹具应用真实 Core 与 IdentityPriority 迁移，用模拟业务数据和原 Stock Owner 写入 A/B。原夹具调用 `pg_dump -Fc` 与单事务 `pg_restore`，恢复到不同 OID 的私有候选。回归直接对候选调用真实 worker，发送边界使用模拟 publisher。 [原生回归](../../../../CP6.Tests/Erp/StockOutboxRestorePostgreSqlTests.cs)
- 基线自动回归在 `Assert.Empty` 失败，证实源库先派送后，候选 worker 将同一 MessageId 交给模拟发送器。首次测试因私有路径缺少夹具要求的 `permission-consumption-` 前缀未初始化数据库，修正路径后取得上述原生失败。保留 `results/red.trx`、`results/red-native.trx` 及相应日志。
- 修复后定向回归 15 通过、0 失败、0 跳过，含现有 WebApi 后台实际向已认证 WebSocket 派送、同一原操作的并发 worker 和提交确认丢失。审查限定 `pg_catalog` 后复验两个受影响场景，2 通过、0 失败、0 跳过。最终 TRX 于 08:03:44 UTC 完成，距开工 17 分 13 秒。其他 Stock 用例沿用首轮结果。原件在 `D:/CP6-local-test/CP6-72H-20261109/BUG-808-STOCK-RESTORE-OUTBOX`，包括 `results/green.trx`、`results/final.trx` 和 `test-final.log`。
- 实测候选缺配置时由实际数据库标记拒绝，有配置但暂缺标记时同样拒绝。应用连接与队列物理库不同，仍以队列为准。夹具在实际队列事务开始后加标记，事务授权拒绝。原隐私 Owner 实际应用恢复，原 HTTP 准入使用的函数取得有效租约后，候选仍无派送。候选原消息保持 `Pending`，消息、绑定、路由与原 journal 比较一致，`DispatchRun`、claim、send attempt、mutation receipt 和外发均为零。源 A 与归档后提交的 B 各自合法派送，使用相同操作 UUID 和各自租户消息键，均可读回原 `COMMITTED` 结果。
- 三个私有实例均有停止记录，五个候选按原 OID 与标记定向删除。复核无本任务进程和 `55561` 监听。正式 PGT-022 的生产切换与完整验收仍归原任务。Actions 触发、取消、工作流变更及部署均为零。

分支候选在 DEV-2 本地提交，工作树交 COORD 收回。远端采用、分支处置、Issue 关闭及席位释放由 COORD 记录。
