# W634-01 历史重发父作业的 PostgreSQL 提交

基线 `951f06374945137a13fb4dd35101bfaf53de8cd3`，本地源码与测试提交 `c193ce8c0f87c34780ccea0717f34cdd72c52d61`。在固定基线用真实签名和真实 worker 运行正常流程。子发布已经原生提交时，独立数据库连接仍读到父 `ValidationPassed` 和空的 `PublishAttemptId`，事务原子性断言按预期失败。W633 正向数据与相关阶段证据继续适用。 [Main #1159](https://github.com/GTX537/CP6/issues/1159) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [基线 TRX](D:/CP6-test-artifacts/W634-01/baseline-atomic/results.trx) · [原提交差异](D:/CP6-test-artifacts/W634-01/baseline-atomic/permission-consumption-baseline-atomic/w633-child-committed.json) · [基线源码与 DLL](D:/CP6-test-artifacts/W634-01/baseline-atomic/source-and-assemblies.json) · [W633 报告](D:/CP6-test-artifacts/W633-01/DELIVERY.md)

`CloneHistoricalSnapshot`、`ValidateHistoricalSnapshot` 进入和终态，以及 `QueuePublish` 在各自原生写入中共用 Main 租户根锁和持久父 Job 的当前租约。提交前再次核对租约、取消状态和租户有效期限。克隆在同一事务复制历史快照、完成目标初始化并记录父阶段。校验保留可恢复的 Validating，能力读取和计算在事务外，终态根据当前 Draft 归属、版本修订及 Published 指针提交 Run、Issues 与父状态。子 Plan、Attempt、Job、两条审计、目标 Publishing 与父 `MarkPublishQueued` 在已有子写入事务中同时提交。 [父执行器](D:/CP6-worktrees/dev-2/CP6.Space.Infrastructure/SpaceHistoricalRepublishService.cs:500) · [原生子发布事务](D:/CP6-worktrees/dev-2/CP6.Space.Infrastructure/SpacePublishOrchestrator.cs:267)

旧的部分提交按原固定子请求键、目标和历史来源、Plan/ValidationRun、原请求哈希与历史入队审计核对，并基于该审计恢复原始子 Job。已经提交的 Link 使用原结果读回。旧记录只缺 Link 时，通过当前父 Job 事务补写父行。COMMIT 应答未知时尝试只读原事实，无法确认时沿用原 Job 恢复协议。 [旧数据与提交读回](D:/CP6-worktrees/dev-2/CP6.Space.Infrastructure/SpaceHistoricalRepublishService.cs:708)

## 真实 PostgreSQL 回归

PostgreSQL 18.6 与真实 Main Kestrel 在独占端口运行。模拟已刊发、历史、仓库和身份数据。原调用人由 `space:model:rollback` 和 `space:model:read` 签名，租户暂停、恢复也走原签名管理入口。真实 `SpacePublishJobWorker`、当前持久租约和父处理器运行全部阶段，受控夹具保留子 Job 队列供独立 W621 物理效果回归。 [父链 TRX](D:/CP6-test-artifacts/W634-01/native-1/results.trx) · [原生锁、数据与回执快照](D:/CP6-test-artifacts/W634-01/native-1/permission-consumption-native-1)

| 写入边界 | 暂停先提交 | 效果先提交 |
| --- | --- | --- |
| Clone | 父仍 Requested、目标 Initializing，零新快照行。 | 父 SnapshotCloned、目标 Draft，楼层、区域、机架、层、库位、元素六类原逻辑 ID 与新物理 ID 留存。 |
| Validate 进入 | 保留原 Draft、空 Run。 | 保留独立 Validating、空 Run，后续原 Job 沿用该状态。 |
| Validate 终态 | 保留 Validating 和预先种入的开放 Info，零新 Run、Issue。 | 同一已提交 Passed Run 与由真实校验生成的关联 Info Issue 留存。恢复原 Job 时回用原 Run ID。 |
| Queue | 父继续 ValidationPassed，无子 Plan、Attempt、Job 或审计写入。 | 原子提交七个业务写行（同一 `xmin=1630`）。签名暂停提交后，夹具模拟中断已提交事务的应答，原 Attempt 从真实记录读回同一 Plan、Attempt、Job，父 Job 在暂停中成功结束。 |

暂停阻止新效果的用例均使用原父 Job 下一 Attempt 恢复，已经提交的快照、Run 和子记录保持原 ID。各顺序的同键签名复读返回 202、`Idempotent-Replay: true`，GET 返回同一业务收据。旧形态用真实 child COMMIT 产生的行导入 W633 已观察的无 Link 状态，再运行同一父 Job。恢复后只有父关联行的 `xmin` 改变，子发布 Attempt、子 Job、Plan、目标和审计的行版本均相同。 [Queue 应答遗失](D:/CP6-test-artifacts/W634-01/native-1/permission-consumption-native-1/historical-republish-QueuePublish-effect-first.json) · [旧 Link 恢复](D:/CP6-test-artifacts/W634-01/native-1/permission-consumption-native-1/historical-republish-legacy-link-recovery.json)

共 **35 项通过、零跳过**。父链 9 项覆盖四个实际写入边界的两种顺序、Queue 原生 COMMIT 应答遗失和旧 Link 恢复。W604 前台发布、重试、历史预留与角色变化 8 项，W621 真实内置 WMS 与 Runtime 物理效果、到期和应答遗失 7 项，控制器、计划和 worker 11 项。新父链用例的应答故障落在 Queue 边界。 [W604 前台](D:/CP6-test-artifacts/W634-01/foreground-regression/results.trx) · [W621 物理发布](D:/CP6-test-artifacts/W634-01/child-regression/results.trx) · [相关控制](D:/CP6-test-artifacts/W634-01/related-controls/results.trx)

## 本地发布与签出

集中审查六个任务文件，最新已确认 Main `919b1868041fbeb2034022b5072d38ca4bdc9d14` 与这六个文件无重叠。六份提交文件与测试时 SHA-256 一致，七份本地发布 DLL 与受测宿主字节一致。Debug PostgreSQL 发布 310 文件，清单 SHA-256 `278a75cf6cb8ae0d895668d24fd22efbe9fb82704b46269aa5faf40347a73f0e`。真实启动使用 PostgreSQL 57090 和 Kestrel 57098，活性 200，匿名历史重发和发起发布均为 401。所有任务进程已停止，57090–57099 无监听。`codex/w634-01` 保留本地待协调采用，DEV-2 工作树干净，席位待协调方 finish。Actions、推送与生产部署均为零。 [发布与冒烟](D:/CP6-test-artifacts/W634-01/publish-verified-smoke.json) · [发布文件哈希](D:/CP6-test-artifacts/W634-01/publish-sha256.json) · [停机与范围核对](D:/CP6-test-artifacts/W634-01/process-verification.json)
