# W650-01 Space 规划分支预约的共同事务栅栏

固定 Main `04db2f57646fa4fcc135836c3fe7c5177ae44dd3`，受测源码提交 `a26f254e6c8fc0a1dd3776e2e7dee72804ecaf57`。真实签名 Main Kestrel 和隔离 PostgreSQL 18.6，使用模拟站点、已发布并固定的模型与原规划权限。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

规划预约 `PUT /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}` 在原 Serializable 写入里取得 Main 当前 `space:planning:scenario:create` 权限栅栏，提交前确认当前会话。在同一边界校验本站当前 Published 版本并创建原规划目标、分支与 CloneVersion Job。相同原分支键继续按原输入哈希读取已提交结果。沿用生产与规划版本隔离、原 Job 及已验证的当前处理器。 [规划写入](../../../../CP6.Space.Infrastructure/SpacePlanningScenarioService.cs)

| 实际顺序 | 独立数据库回读 |
| --- | --- |
| 预约先 | 预约先持 Main 根并在原分支插入处等待，签名暂停等待预约连接。业务返回 200，分支、目标版本、Job 一起提交，三个 native `xmin` 都是 1077。之后暂停 200 并推进授权代次，原预约保留。 |
| 暂停先 | 预约等待实际签名暂停的 Main 根，暂停提交后预约返回 401。原模型、版本、分支及 Job 写集逐行一致。 |
| 恢复及原处理 | 恢复启用、用相同分支键重发。已经提交的目标版本和 Job 返回原 ID，原写集不变。被阻断的预约按恢复后的当前授权生成一份。原 W647 实际规划克隆继续通过两种提交顺序。 |

基线 W651 在已采用源码的实际 API 上观测到两项缺口。暂停先提交后旧预约仍返回 200 并新增三行。修复后四项原生场景全部通过，零跳过。 [基线证据](D:/CP6-test-artifacts/W651-01/DELIVERY.md) · [受测参数](D:/CP6-test-artifacts/W650-01/native/invocation.json) · [真实 TRX](D:/CP6-test-artifacts/W650-01/native/results.trx)

本地发布 310 文件，清单 SHA-256 `12f2e683b32ef952ab7234bf183fc5e2f6377ee7529df8374917c3ef597c736b`。四个产品 DLL 与受测文件字节一致，两个提交文件的 Git blob 与验证一致。隔离 PostgreSQL 57270，Kestrel 57278 实机 `/health/live` 200，匿名规划 PUT 401。所有任务进程已停止。 [本地发布](D:/CP6-test-artifacts/W650-01/publish-smoke.json) · [逐文件清单](D:/CP6-test-artifacts/W650-01/publish-sha256.json)

正式 PGT-017 仍待其余登记写者和 Main 对 CRM 封存入口的故障恢复联验。本任务未运行 Actions 或部署。
