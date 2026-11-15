# W656-01 Space 刊发终态与人工 Reconcile 写权限

Main 基线 `6f6b75ebf836427a2b274e8a0238afe767a33969`，本地提交 `3461d4575d989e70bc784519e084c528f3a60d36`。W655 在相同写者、Main 权限适配器和真实 worker 源码上观察到暂停先提交后仍写出刊发业务终态，复用该 PostgreSQL 基线证据。 [登记](D:/CP6-test-artifacts/W656-01/main-start.json) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [W655 基线](D:/CP6-test-artifacts/W655-01/DELIVERY.md)

## 写入

- `FailNoEffect` 使用原生 `SpaceArtifactWriteTransaction.BeginMetadataAsync`，与 Main 暂停共用数据库根锁。用当前持久租约、租户及提交前期限核准原 Attempt、Plan、Job 和目标。`Publishing→Ready`、原 Attempt 的 `FailedNoEffect` 和同一审核链终态同事务。已持久的全项拒绝按原批次操作键和 payload hash 读取，可用原 Job 完成终态。真实 COMMIT 应答遗失时，在原事务结束后读回已持久的终态。 [终态写者](../../../../CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs#L937)
- 已签名人工重试创建的 Reconcile Job 用相同原生 helper。在根锁下重读当前 Attempt 和目标，保存 `ReconciliationRequired→Publishing`、原 Attempt Preflight 和该 Job 的 `ProcessingStarted`。仓库发现与 WMS I/O 从该事务结束后继续。技术候选和 Job/Step 结算使用原 runner。 [Reconcile 写者](../../../../CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs#L884) · [生产 worker](../../../../CP6.WebApi/BackgroundServices/SpacePublishJobWorker.cs#L50)

## 本地实证

PostgreSQL 18.6、真实签名 Main Kestrel 和生产 `SpacePublishJobWorker`。刊发用户有 `space:model:publish` 和 `space:model:read`。六组 `FailNoEffect` 验证分别使用模拟的能力变化、阻断预检或全项拒绝结果和真实数据库、事务、Job/Attempt 及审核记录。人工 Reconcile 的历史输入由原 worker 接收模拟 WMS 部分状态并生成真实安全 hold 和 Issue，之后使用实际内置 WMS/Runtime 完成原 Reconcile Job。 [实际夹具](../../../../CP6.Tests/Erp/SpacePublishNoEffectWriteAuthorityPostgreSqlTests.cs)

| 写入边界 | 暂停先提交 | 业务先提交 |
| --- | --- | --- |
| 能力变化与阻断预检 | worker 等待暂停持有的 Main 根锁。版本保持 Publishing，没有业务终态或拒绝回执。重新启用后同一 Job 和刊发 Attempt 完成。 | 版本真实 UPDATE 持有根锁，签名暂停等待。`Ready`、`FailedNoEffect`、终态审核原 `xmin` 相同并保留。 |
| WMS 全项拒绝 | 原批次、拒绝回执和观察先已提交。暂停期间它们保留，刊发终态没有写入。重新启用后同 Job、同 Attempt 完成，Apply 总计一次。 | 版本、终态 Attempt 和审核一次提交。原批次及拒绝凭据保留，实际库位、WMS 操作及 Runtime 投影没有新增。 |
| 人工 Reconcile 重开 | 原 Reconcile Job 已领取，随后等待暂停的根锁。版本保持 `ReconciliationRequired`，原 Issue 保留，未写该 Job 的 `ProcessingStarted`。重新启用后同 Job 完成重开，三个重开写入为 `xmin=1205`。 | 签名暂停等待重开事务。暂停时读取 `Publishing`、Preflighting 与原审核为 `xmin=1067`。此时发现调用仍在事务外等待，原 WMS/Runtime 写集保持。 |

`FailNoEffect` 真正丢弃了一次 PostgreSQL 原生 `COMMIT` 回应。从持久数据读回原终态，Job 正常完成，后续 worker pass 无重复写，原签名 HTTP 幂等键返回原 Attempt/Job/Plan。两例 Reconcile 后续各提交一个原生 WMS 操作和一个实际 Runtime 库位，保留原 Batch ID、操作键及 hash，解决同一 Issue。相同人工重试键读回原 Reconcile Job、Attempt 和 Plan。 [FailNoEffect 锁与写集](D:/CP6-test-artifacts/W656-01/native-2/permission-consumption-native-2) · [最终 ACK 证据](D:/CP6-test-artifacts/W656-01/final-capability/permission-consumption-final-capability/space-publish-no-effect-capability-commit-ack-lost.json) · [Reconcile 暂停先](D:/CP6-test-artifacts/W656-01/reconcile/permission-consumption-reconcile/space-publish-reconcile-reopen-pause-first.json) · [Reconcile 业务先](D:/CP6-test-artifacts/W656-01/reconcile/permission-consumption-reconcile/space-publish-reconcile-reopen-effect-first.json)

追加 Reconcile 前，`FailNoEffect` 六次原生顺序与一次真实 ACK 遗失共 **7 通过**。最终源码通过 **2 次 Reconcile、3 次能力变化与 ACK 定向回归、7 项契约**，各运行零跳过。首轮夹具把持久 `NotApplied` 枚举写成适配器的 `Rejected`，编译失败。修正测试后运行保留了实际数据库和适配器两种表示。W621 内置物理写者的历史专项证据按其原范围复用。 [首次编译](D:/CP6-test-artifacts/W656-01/native-1/build.log) · [FailNoEffect TRX](D:/CP6-test-artifacts/W656-01/native-2/results.trx) · [Reconcile TRX](D:/CP6-test-artifacts/W656-01/reconcile/results.trx) · [最终能力与 ACK TRX](D:/CP6-test-artifacts/W656-01/final-capability/results.trx) · [最终契约 TRX](D:/CP6-test-artifacts/W656-01/contracts-final/results.trx) · [W621 报告](D:/CP6-test-artifacts/W621-01/DELIVERY.md)

六个 `RequireReconciliationAsync` 调用记录已经观察到的部分或未知事实，并维持安全 hold。它们涵盖非 Pending 批次后的能力变化、外部证据后的预检变化、恢复与超时查询的 Partial、Apply 返回的部分或矛盾结果、WMS 回读不符。原 Issue 与审核属于该事实记录。W656 的两项业务写者是上述终态释放和人工 Reconcile 重开。 [事实写入](../../../../CP6.Space.Infrastructure/SpacePublishOrchestrator.cs#L886)

## 本地发布与交接

发布绑定提交 `3461d4575d989e70bc784519e084c528f3a60d36`，310 个文件，清单 SHA-256 `1a574670e1e9b950909fc36c6695ce01a77f576ea04e0ece26d9dfd612120f3a`。7 个产品 DLL 与受测 DLL 逐一匹配。独立 PostgreSQL 57280 和发布 API 57289 实际启动，`GET /health/live` 200，匿名 POST 刊发尝试 401。端口 57280–57289 无监听，所属 PostgreSQL 与测试/API 进程均已停止。 [源文件及程序集哈希与启动](D:/CP6-test-artifacts/W656-01/publish-verified-smoke.json) · [文件清单](D:/CP6-test-artifacts/W656-01/publish-sha256.json) · [停机证据](D:/CP6-test-artifacts/W656-01/process-verification.json)

`codex/w656-01` 保留本地提交，DEV-1 工作树干净。集成及席位释放交协调方。此次执行范围为隔离的本地验证。

采用 Main `aaf872ec8f87b6566bcda1a7ac4b6c521f90f373` 时，重新核对两个刊发源码文件及 PostgreSQL 夹具三个 Git blob 与已验证源码一致。该基线随后包含的 W652 ERP 出库统计字段和对应定向夹具没有修改本刊发调用链，沿用上述实测及该范围的发布证据。
