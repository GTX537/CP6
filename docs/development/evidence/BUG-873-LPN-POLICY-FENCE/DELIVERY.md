# W436 · BUG-873-LPN-POLICY-FENCE

2026-11-10 UTC。活动 BUG 为 [#873](https://github.com/GTX537/CP6/issues/873)，范围为原 WP02-03 / PGT-017 的 LPN 混装策略写者。开发基线 `576fb14ccff0aa6bea1be296a58ec434c865dfe1`，复用 DEV-2 `codex/bug-873-lpn-policy-fence`，COORD 单人执行。成功的 start 登记只执行一次。

## 问题与实现

`POST /api/v2/wms/lpns/policies` 原先经过 HTTP 前置授权后直接读取和保存策略。在请求已通过入口检查、租户暂停随后提交的情况下，原实现仍可修改策略。

PostgreSQL 的 `LpnService.UpsertPolicyAsync` 现在使用已注册的 `OperationsOwnerV1`、`wms-mobile:lpn-manage` 动作和共同认证根事务。写入采用 Owner 的当前用户身份，执行提交前授权校验。缺失或不同上下文的 Owner 返回既有 503，策略动作使用现有 `OrderOwnerResult` 输出当前授权拒绝。保留既有 DTO、合法值、原实体及其他 LPN 操作。新增行为只面向 PostgreSQL。

## 实际验证

采用模拟租户、普通用户、管理员及角色，实际 Main Kestrel HTTP 和隔离 PostgreSQL 18.6，端口 55568。测试先以普通用户正常保存策略，确认原行和真实 Creator。两个独立控制事务分别阻塞目标租户 UPDATE 和策略表访问。原生等待观测确认策略请求已到达后端，再放行租户暂停提交，最后放行策略表。

| 观测 | 修复前原版本 | 修复后候选 |
| --- | --- | --- |
| 实际测试执行数 | 1，预期失败，0 跳过 | 1 通过，0 跳过 |
| 策略请求等待共同认证根 | `False` | `True` |
| 暂停提交后的 HTTP | 200 | 401，REAUTH_REQUIRED |
| 策略完整 JSONB 原行 | 已改变 | 逐字节保持 |
| 真实暂停 Revision | 2 | 2 |

修复前策略后端 `29436` 等待策略表控制连接 `49452`，暂停在 `2026-11-10T06:04:59.063433+00:00` 提交后策略仍写入。修复后策略后端 `38716` 等待暂停后端 `29520`，暂停提交后旧会话被拒绝。用例比较完整原行，包括 Id、TenantId、Creator、Modifier、时间、RowVersion 和两个混装标志。

同一测试源码 SHA-256 `e5045e861b93a17c7f1e36a88d09ed4e0965701d311d3a7537e8a6962e620a54` 用于修复前后两次实测。失败日志、TRX、原生等待、实际原行、正常编译及进程停止记录保存在本地 `D:/CP6-local-test/CP6-72H-20261109/W436-01`。关键原始记录为 `red-hosted-01/actual.json`、`green-hosted-01/actual.json` 及各自 `stopped.json`。两次 fixture 均已停止，无遗留监听。

使用正常锁定依赖和 SDK 构建，修复前只编译测试，修复后编译 Core、Web API 和测试。未变化的 Entity、PostgreSQL Provider、Space 依赖复用 W435 已核编译结果，并核对自 W433 到当前基线的相关源码和构建输入。全量产品测试、SQL Server 验收及部署未执行。

## 验证过程与估时

预计本窗口 26–43 分钟，参照 W435 实测完整席位周期 22 分 35 秒。官方席位领取时间为 05:20:41.876956 UTC，期间包含用户中断与接续，墙钟席位占用不能当作实际 Codex 执行时长。预算按用户确认的 48 小时 / 172800 秒执行。

首次检查因可用物理内存不足未进入构建。后续串行构建限制编译进程托管堆，测试项目的 512 MiB、1536 MiB 上限与 Core 的 1536 MiB、4096 MiB 上限分别出现实际 OOM，记录保留。Core 失败栈涉及全项目分析器。此局部修复的 Core 构建使用 `RunAnalyzersDuringBuild=false`，保留正常源码编译、实际回归与集中 diff 审查；Core 全项目静态分析器未完成，不记录为通过。成功的 Core、Web API 构建位于 `green-build-04`，随后只重编测试并复用同一产品输出。按项目提高该进程上限并限制并行度后完成相关构建，未修改系统内存或网络设置。编译警告保留在原始日志，成功结果以对应退出码为准。

## 集中审查及交付边界

COORD 集中审查完整任务 diff，核对策略读写在共同事务内、当前 actor 来源、现有 HTTP 拒绝映射、合法输入校验、测试中的真实锁等待和提交先后、完整原行、关闭 fixture、依赖复用范围及工作区归属。只有本工单源文件、回归和必要状态文档纳入提交。

原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED。正式 PostgreSQL 仍为 16 PASS、11 NOT_RUN。PGT-017 继续 NOT_RUN，其他 LPN 写者及完整撤权入口覆盖仍属原计划余项。本次定向工单修复不代表全部 writer 已采用共同栅栏。

W435 已由 #872 进入远端 main，原任务分支清理和席位释放回执在台账核销。本工单候选通过后经正常 PR 合并，再核对远端 main 和 Issue 关闭。正式 `manage.ps1 finish` 回执记录准确分支处置、固定工作区状态和席位释放。

候选与受保护主线工作流树相同，普通工作流只手动触发，R2 候选限定版本 tag。本任务只推送分支和正常 PR，不触发 Actions，不执行生产部署。接手时未发现该分支排队或运行中的 Actions。
