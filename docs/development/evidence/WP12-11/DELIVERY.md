# WP12-11 准确截止策略与超时 worker 竞争

EXEC-WP02-14B 依 #412 完成 Main WP12-11。本卡起点 `2026-11-23T06:19:32.031610+00:00`，领卡主线 `6e3d9d07ac92fb906027df1ecc6817755705a9d8`，受验产品与测试源码 `0d8f006314883bc1e168b68416bb7ba771ecca10`，前置及拆分余卡 28 项完整 DONE。工作树为 `D:/CP6-worktrees/wave17-dispatch-20261018` 的 Main DEV-1，任务分支为 `codex/wp12-11`。 [正式派工 #412](https://github.com/GTX537/CP6.AI-Handoff/blob/cc532e6b153a44a65e0e3f9b64a02cfe45aa0dcc/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T061343Z-exec-wp02-14b-wp12-11/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/f702f07a590d8cc6938273d55f40d774768c8a6a/handoffs/CP6-DEV-20261012/tasks/WP12-11/messages/20261123T062018Z-exec-wp02-14b-start/MESSAGE.md) · [正常受保护 PR](https://github.com/GTX537/CP6/pull/1315)

## 日历、截止与执行边界

- 版本化入口按租户、日历 ID 与修订操作原年历。同一本年历使用租户 UUID。默认工作周固定为周一至周五，date-only 例外优先。PostgreSQL 同一来源捕获新旧入口的例外修订，存量迁移固定切换时的可证明例外。导入在当前编辑权下按原命令绑定源包、年份和插入及保留清单，整批只写一次。实际浏览器读取包 `JP-STATIC-2026-2027/1`、覆盖年份 `[2026, 2027]`、SHA-256 `d9b278aced895d99369d72a0bc3c87c1b26cd149e581e45ce407842981e77fe1`，原导入 `fd84f35f-0ebb-4a00-81aa-1e505c922595`。 [版本化日历](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Core/Services/Oa/VersionedWorkCalendarV1.cs) · [迁移](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Persistence.PostgreSql/Migrations/Core/20261123065000_VersionedWorkCalendarV1.cs)
- 新原生待办固定实际租户时区及规则字节摘要。按 UTC 算小时，按当地日期与日历修订算工作日。工作日从起始次日开始，最多扫描 366 日。DST 缺时刻给出 `AMBIGUOUS_OR_INVALID_LOCAL_TIME`，重复时刻要求真实 offset。America/New_York 2027-11-07 01:30 取 −240 和 −300 分钟得到 05:30Z 和 06:30Z，实际原生待办固定了 −300 分钟的凭证。时区或政策缺证的待办可查阻断，现有旧截止照原事实读。 [时间计算](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Core/Services/Oa/WorkflowDeadlineTimeV1.cs) · [原生截止入库](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Core/Services/Wf/FlowEngine.DeadlinesV2.cs)
- 日历和 Flow 新版发布后，原待办沿原截止与动作。重算独立授予 `Calendar.Recalculate`，以当前租户、原任务和实例版本及准确预览清单确认，保留原流程政策。浏览器重算命令 `6f568340-5754-45ae-922d-2a3ceac3771a`，预览 `53dfdc51-be22-4ffd-b744-dfe26658ce9c`，UTC 截止 `2026-11-24T17:00:00+00:00`（1）更新为 `2026-11-25T17:00:00+00:00`（2）。新截止与审计、前后凭证及一条通知同事务。兼容列 `Wf_FlowTask.DueAt` 保留初始投影，原生收件箱和 worker 共同读取当前截止指针。 [重算 Owner](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Core/Services/Oa/VersionedWorkCalendarV1.Recalculation.cs) · [现有 OA 年历](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/cp6.web/src/views/oa/admin/WorkCalendar.vue)
- 领取固定原任务和实例版本，数据库提交门把效果与原 epoch、回执和双版本关联。两个实际 hosted worker 在隔离库持独立服务身份扫描，原动作槽只有一个领取和一个提交。实际 Main 人工决定 POST 与超时按共同事务根完成竞争。旧租约及撤权不能提交新动作。浏览器读回已核实的 worker 原命令 `80651dcd-fd2a-4b87-aba6-c48ec75e626e`、租约代次 `1` 与唯一提醒，人工票为零。 [超时 Owner](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Core/Services/Oa/NativeTimeoutOwnerV2.cs) · [数据库门](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Persistence.PostgreSql/Migrations/Core/20261123065100_WorkflowDeadlineEvidenceV1.cs) · [收件箱诊断](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/cp6.web/src/views/oa/inbox/NativeTimeoutStatus.vue)

## 原 AC 核对

OA-TASK-01 R2 原册 294 行，SHA-256 `9f9920ffe400ba982834f2d02276535e5a5654ae6b64236fd366247418084b8d`。46 项逐项对照原断言和登记归属，本卡 14 项全部通过，累计 **25/46**。原册必需补充 0 项及必需运行修订 0 项。目标保持开放。 [日历原 AC](https://github.com/GTX537/CP6/blob/6e3d9d07ac92fb906027df1ecc6817755705a9d8/docs/development/assessments/20261011/catalogs/engineering-oa.json#L20780) · [超时原 AC](https://github.com/GTX537/CP6/blob/6e3d9d07ac92fb906027df1ecc6817755705a9d8/docs/development/assessments/20261011/catalogs/engineering-oa.json#L20858)

| 原断言 | 结果 | 实际运行及边界 |
| --- | --- | --- |
| AC-T04-01 | PASS | Main HTTP 与隔离 PostgreSQL 读取租户日历，旧编辑同源留版，周六补班、旧修订和清除例外回默认均可核对，外租户 404。 |
| AC-T04-02 | PASS | 实际 API 忽略起始星期五，默认跨到周一，例外允许提前到周六，跨 366 日范围返回 CALENDAR_RANGE_EXCEEDED。 |
| AC-T04-03 | PASS | 实际外层丢首回包、浏览器刷新及 Main 进程重启并发按原键读同一批次，人工补班保留，包版本、摘要和支持年份固定。 |
| AC-T04-04 | PASS | 发布日历保持原截止。独立 Calendar.Recalculate 授权预览，撤权、跨租户和陈旧清单或双版本拒绝，确认同事务记录前后凭证和一条通知。 |
| AC-T04-05 | PASS | Main 工作日 API 和实际新待办通过 DST 缺失及重复时刻。明确 offset 选项与实际持久 UTC、本地日期时间、规则摘要、日历及 Flow/node 原版可对账。 |
| AC-T04-06 | PASS | 租户时区缺失和无效均有 INVALID_POLICY 原记录，无新业务截止或 worker 领取。W283 原有有效历史事实只读。 |
| AC-T05-01 | PASS | 实际发布新版由 remind 改为 reject 后，旧待办仍按原 Flow/node policy 和 UTC 截止只产生提醒。 |
| AC-T05-02 | PASS | 两个实际 hosted worker 与独立 PostgreSQL 会话只认一个原槽和一个赢家，租约结束以同原键进入新 epoch，旧 epoch 及提交点超期零效果。 |
| AC-T05-03 | PASS | 实际 SYSTEM_EXECUTION 与当前 WF_AUTH 同事务。未登记或已撤销服务、缺共同门或陈旧办理版本都不能提交。APP 人工要求保持待办和 BLOCKED_POLICY。 |
| AC-T05-04 | PASS | 同一 occurrence 重扫和原键重放只产生一条 TIMEOUT_REMINDER Outbox，零人工票，后续 occurrence 采用固定间隔。 |
| AC-T05-05 | PASS | 真实升级保持原职责与实际服务 actor，目标停用或与 APP 原冻结主体职责冲突时保留未办待办和 BLOCKED_TARGET，原告警及回执唯一。 |
| AC-T05-06 | PASS | 错误边只取消原 token 和任务并建恢复任务，平行待办仍合法，重算后错误变量采用已领取的 UTC 截止。APP 无系统终态授权明确阻断。 |
| AC-T05-07 | PASS | 实际 Main HTTP 人工先赢与 worker 先赢各保单一决定及原回执。Commit 完成后的丢回包后由独立上下文查回同一原 timeout 命令，Outbox 一条。 |
| AC-T05-08 | PASS | 旧待办夹具保留已有 DueAt 且始终可读缺 pin，坏时区、缺时区及未知政策的新事实可查，真实 worker 没有自动效果。 |

其余 21 项按原归属继续供目标复核。WP12-08 6 项、WP12-10 5 项、WP12-20 8 项、WP05-19 2 项（其中技术验证和基线边界协同 WP05-20）。通知投递与偏好继续由 WP12-20 验收。 [原任务定义](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/docs/development/task-plans/WP12.md#wp12-11)

## 实跑与复用

- 最终本地构建 0 错误。当前日历、超时、实际浏览器及权限门定向运行 **45/45**，原 Owner 回归 **27/27**。前端 `vue-tsc` 通过，定向 Vitest **16/16**。实际 Main Kestrel、两个 `NativeTimeoutWorkerV2` Host、Chrome 与 PostgreSQL 18.6 运行在本机隔离租户及进程。夹具只提供受控日期、旧待办数据和提交后丢失回包，应用内部 API 按实际请求执行。 [日历 PostgreSQL 用例](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Tests/Erp/WorkflowDeadlineCalendarPostgreSqlTests.cs) · [竞争用例](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Tests/Erp/NativeTimeoutRacePostgreSqlTests.cs) · [双 Host 浏览器用例](https://github.com/GTX537/CP6/blob/0d8f006314883bc1e168b68416bb7ba771ecca10/CP6.Tests/Erp/NativeTimeoutBrowserPostgreSqlTests.cs)
- W283 原生领取、系统决定、升级、错误边、原回执及 OA Host 观察以领卡树核对并重跑受影响链。W640 的旧扫描使用实际版本和共根，旧业务权威仍从原流程合同执行。W531 的旧年历撤权门随 PGT017 回归，新增权限独立核证。JOB 中原键、回执恢复及错过处理沿 WP02-21b 已采用合同重跑。 [W283](../W283-01/DELIVERY.md) · [W640](../W640-01/DELIVERY.md) · [W531](../W531-01/DELIVERY.md) · [WP02-21b](../WP02-21b/DELIVERY.md)
- 最初回归命中版本入口缺失及无效租户时区导致旧提交失败。中间轮 48 通过、1 项权限目录反射互锁失败，已按现有 `OwnerActions` 登记修正。PGT011 旧断言在已采用的基线程序集复现失败，按 WP02-21b 分别核验停写后禁止新领取、仍可读原回执和撤读后拒绝。基线 TRX SHA-256 为 `3e738b602766f66f3b05d9e39e8dacbcf5b8f00b165cc524a2d2f89cd7b42d23`。验证原件保存在 `D:/CP6-test-artifacts/WP12-11/20261123-wp1211`。最终 TRX SHA-256 分别为 `e3df1faab625c4916448579d9a59e8badb99e8516890ecb20aee123d1f99cbe7` 和 `616d5d03c7f8dafb42fcb75d2f0a9e86cbfccdd9bded9d3efe2472ec9cadcfd8`。年历和超时浏览器 JSON SHA-256 分别为 `3f23d43efb6b10b608f33d6435621b2a665804d882f91e1bbcba2fbbee7f57ab` 和 `a3f4c84467638006d65748efe9fc224c9d72de5f2988cd531d0f19786e243d00`。

## 台账与交回

核销快照 `2026-11-23T07:14:09.885797+00:00`。本卡 3277.773 有效秒，目标复核 0.081 秒，额度 14400 秒，21600 秒触发强制拆卡，本轮拆卡 0。按 #411 用 WP12-16 原 DELIVERY 的提交 `d98aa9c450be2a1b7ef864583c7c63956a9085ea` 与仓内路径查重，单次补记 **285.513 秒**至原卡 **2768.678 秒**，原目标复核 0.075 秒单列。计入本卡前计划累计 180402.682 秒，加本卡后 **183680.536 秒**。 [签收 #411](https://github.com/GTX537/CP6.AI-Handoff/blob/7fe2544bcd4dca395253151f222892cb8f8dd90c/handoffs/CP6-DEV-20261012/tasks/WP12-16/replies/20261123T061113Z-coord-acceptance-ack/MESSAGE.md) · [WP12-16 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/d98aa9c450be2a1b7ef864583c7c63956a9085ea/handoffs/CP6-DEV-20261012/tasks/WP12-16/messages/20261123T060400Z-exec-wp02-14b-delivery/MESSAGE.md)

同一正常 Main PR 更新台账和四份状态页。OA 完成 13/20，按图候选为 WP12-17（3）、WP12-05（2）、WP12-14（2）、WP12-15（2）、WP12-20（2），派发由 COORD。正式远端采用、同树冒烟、任务分支处置、DEV-1 释放及后续工时，以受保护 PR、席位记录和原通信分支回读的 DELIVERY 为据。 [台账](../../roadmap.json) · [单卡执行计划](../../planning/20261117-card-execution.md) · [Main PR](https://github.com/GTX537/CP6/pull/1315)
