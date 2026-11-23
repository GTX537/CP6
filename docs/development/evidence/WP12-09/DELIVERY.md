# WP12-09 退回重送、转交加签和无人审批恢复

WP12-09 按原卡完整 DONE。Main 的实际 HTTP 入口、现有 OA 收件箱、Chrome 157.0.8103.78 和隔离 PostgreSQL 18.6 完成三组验收。OA-TASK-01 的 11/46 项原断言有完整证据，目标继续开放。 [正常 Main PR](https://github.com/GTX537/CP6/pull/1313) · [原卡](../../task-plans/WP12.md#wp12-09) · [设计手册](../../../design-handbook/modules/OA-TASK-01.md)

领卡主线为 `f64005bca2ebdbea4ea735581a2763eb90d6c295`，受验产品源码为 `c6c6ccb189ad90845aec52693517d1b6988dfc55`。Main DEV-1 位于 `D:/CP6-worktrees/wave17-dispatch-20261018`，分支 `codex/wp12-09`。正式起点 `2026-11-23T03:09:40.021387+00:00`。S01-02、S02-00、WP02-10、WP02-13、WP12-08 展开 24 项完整 DONE，含 WP02-13b、WP02-14b。 [正式派工 #408](https://github.com/GTX537/CP6.AI-Handoff/blob/756f21c8b7517f5c7a05fe184fcd272d3d7a5eb0/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T025844Z-exec-wp02-14b-wp12-09/MESSAGE.md) · [原链 START](https://github.com/GTX537/CP6.AI-Handoff/blob/16b19a4c2809bb9b31829c91628d0308983a3d8f/handoffs/CP6-DEV-20261012/tasks/WP12-09/messages/20261123T031021Z-exec-wp02-14b-start/MESSAGE.md)

## W282 和 W306 已有证据

| 原件 | 核对与本卡复验 |
|---|---|
| W282 源码 `759d89c723482f392c4eccfa5a7953959167979c`，采用 `37a88248ca9f25f3818ba5a6e31fd50ddca891b7` | 原件包含真实 Chrome 收件箱退回和批量转交、Main HTTP、隔离 PostgreSQL 18 以及双连接竞争。按 `D:/CP6-local-test/wave282/foundation/final-validation.json` 逐个匹配固定输入、预览/结果及 DB 观察。领卡树实跑 30/30，含 501 行授权后上限。W282 到领卡树的返回服务变化来自文件、字段选项、三版本字段决策和 #756 请求限额，均以领卡/候选入口校验。 [W282 原报告](../W282-01/DELIVERY.md) · [Main #718](https://github.com/GTX537/CP6/pull/718) |
| W306 源码 `19e101662c4f33bd4cf5140721572bafa80e8cb1`，采用 `b69c9d8cc9b2bc2ab50cb491b40aeb1ba9e55b60` | 原 WP12-08 完整核销。真实两人 all、any、串签旧票及原命令回放，以 `D:/CP6-local-test/wave306/foundation/multi-actor-rounds-20261108T132423Z.json` 对照。受验树复跑两组阈值及 APP/SFS 办理回归。当前 FlowEngine 另有预算、制证、服务 Job 和旧超时隔离变动，通过当前正常办理入口验证相关影响。 [W306 原报告](../W306-01/DELIVERY.md) · [Main #774](https://github.com/GTX537/CP6/pull/774) |

## 实际场景

### 退回编辑和重送

- Chrome 经收件箱打开原实例、读取退回预览与摘要、确认退回、从 `returned-form` 修改后双 CAS 保存。保留原 InstanceId、FormDataId、FLOW/FORM 两个 pin、退回快照和原轮，独立 ReturnedCycleRecord 与 returnRevision 对账。保存结果为 `DRAFT`，携准确前一 executionCycleId。陈旧实例版和陈旧数据版分别拒绝，旧普通 Submitted 草稿再送返回 409 并保原版，字段检查覆盖隐藏、只读、必填及金额精度。 [实际 HTTP 与旧草稿](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeRecoveryCardHttpPostgreSqlTests.cs) · [浏览器重送脚本](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/cp6.web/scripts/oa-task-recovery-browser.mjs)
- 普通重送原包丢失后按原命令键读取当时的 `RUNNING`、新 cycle 和任务，再由新任务真实进行人工办理。两个独立写者争同一 returnRevision，保准确胜者，另一结果为 409 `ALREADY_RESUBMITTED`。相同固定条件以 0 审批人的分支落 `SUSPENDED` 和精确槽位，有资格进入审批时为 `RUNNING`，合法绕行到 End 的实际结果为 `APPROVED`，零人工票。六种原状态有完整协议映射，普通重送到 `REJECTED` 以引擎合法可达转换为准。持久旧回执保存在当时的表示。子流程重送回执使用本事务建立的任务 ID，与新子实例和原固定版本对账。 [重送、恢复和分支竞争](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeSendBackScopePostgreSqlTests.cs) · [暂停重送](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeTaskRecoveryPostgreSqlTests.cs)
- APP 退回冻结 Subject，实际 API 的普通 SFS returned-form 读及写均拒绝，原 APP 受理字节保持一致。PLM 迭代由业务 Owner 独立撤回，OA 原撤回命令可恢复。撤回回执、原冻结提交、审批票及业务 Owner 应用各取其权威记录。 [APP 对照及撤回](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeAppTaskOperationsPostgreSqlTests.cs) · [真实撤回传送](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/ReturnedApprovalWithdrawalDeliveryPostgreSqlTests.cs)

### 转交、前后加签、撤回与竞争

- Main HTTP 转交保留原任务、FormTo 行标识、创建职责、表单内容、token、档、轮和期限。原履历为 `Transferred`，保存 expected 和实际操作者，新履历和任务当前受让人一致。序列中转交与后加签使用独立键，由当前职责人发出。前加签恢复准确父任务，同人已有另一职责时更新准确 NativeTaskId 对应履历。实际撤权、无审批资格、目标停用、不同租户、APP 冻结主体 SOD 拒绝均对账。
- 双连接转交、退回、前加签、后加签及撤回分别竞争原审批，每组只有一个合法结果，回放保留准确原通知和任务。本人及显式代理持相同原职责并发，库中只计一票。门内 revoke/epoch 变化推翻未提交决定，异常回滚和原键未知状态有验证。旧职责缺证从现有显示入口回读，仍标原来源缺证。
- 管理批量转交实跑 Chrome 和 Main，按固定名单逐项恢复。预览后新增任务排除，拒绝、成功及未知结果按独立原键对账。撤回采用原 Owner 命令和其原回执，子流程先提交的新轮使旧撤回版失效。 [业务动作 HTTP](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeRecoveryCardHttpPostgreSqlTests.cs) · [双连接竞争](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeTaskOperationRacePostgreSqlTests.cs) · [固定批次](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeTaskBatchPostgreSqlTests.cs) · [历史职责缺证](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/LegacyActorEvidencePostgreSqlTests.cs)

### 无人审批与受控恢复

- 实际节点路由无人、组织源无法证实以及 APP 原冻结主体职责冲突均生成 `Suspended` 与同事务的 PendingResolutionRecord。对账 cycle、node、token、stage/round 和固定路线。来源恢复前候选为空，assign 明确 503。默认无手工政策返回 403。恢复权限撤回、双 CAS 各自陈旧、错 policyRef 和目标为原发起人均拒绝。
- 实际 OA 收件箱显示暂停原因和发布时路线，从 `MANUAL_ASSIGNMENT` 的候选选择。双连接争同槽只生成一组正式任务与准确 FormTo。首次成功的原提交回包中断后，重新刷新页面并按原键读取原回执。当前有余槽时保持 Suspended，最后一项恢复才转 Running。前次原结果中的余槽和任务 ID 随原结果冻结。暂停和恢复均查得零人工票。
- 浏览器可复验样本的首次恢复槽 `1a5069f8-3f8a-46ff-bf4f-fdb4995687d6`，cycle `6da41988-4e31-41c5-a043-3e527d1c96f1`，唯一新任务 `433c0638-53c6-4cd3-86f4-3db5c63e77eb`，仍阻塞的槽 `e70e5088-58dc-4dc5-b0b0-ee7b3fd1da37`。原始 `result.json` SHA-256 `69c242bde078fe7850590b22a8c450d2100cf5aeb74f2c0c5189ddecad9136f4`。 [恢复浏览器脚本](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/cp6.web/scripts/oa-approver-recovery-browser.mjs) · [真实 HTTP 撤权与冲突](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/NativeRecoveryCardHttpPostgreSqlTests.cs) · [APP 冻结职责](https://github.com/GTX537/CP6/blob/c6c6ccb189ad90845aec52693517d1b6988dfc55/CP6.Tests/Erp/ApprovalTaskAssignmentPostgreSqlTests.cs)

## 原 AC 核销

已逐项核对 R2 294 行及其 46 项原标识，原件 SHA-256 为 `9f9920ffe400ba982834f2d02276535e5a5654ae6b64236fd366247418084b8d`。此目标无单列必需运行修订。 [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json)

| OA-TASK-01 原 AC | 结果 | 实测覆盖 |
|---|---|---|
| AC-T01-05 | PASS | 真实两人 all、any 与串签新轮只计当前票，部分通过或驳回继续等待。 |
| AC-T01-08 | PASS | SFS 表单、任务、历史及原结果随故障回滚，APP 票、Recorder 原决定、证据、Outbox 与原命令同事务。 |
| AC-T02-01 | PASS | 兄弟支拒绝零写，同支保另一支及 FormTo，分叉前清理准确在途集合。 |
| AC-T02-02 | PASS | 首档拒绝，上一档保 token、作废本档在途并增加新轮。W306 两人 all/any 复跑。 |
| AC-T02-03 | PASS | STARTER 原对象和双 pin，双 CAS，旧 Submitted 不复活，实际即刻完成和无人暂停均回原状态。 |
| AC-T02-04 | PASS | Main HTTP 对账当前与创建职责、确切原 FormTo、期限、档轮、actor 及原表单数据。 |
| AC-T02-05 | PASS | 实际 APP 冻结主体职责冲突和 SFS 跨租户、停用、无资格、撤命令权拒绝。 |
| AC-T02-06 | PASS | 实际浏览器恢复同一批次。PG 对账冻结任务名单、逐项拒绝和成功、排除预览后新增任务。 |
| AC-T02-07 | PASS | 两个连接竞争、原键与晚到查询不重办，任务、旧轮、记录和通知对账。 |
| AC-T03-06 | PASS | 实际组织证据缺失、空路由和 APP 职责冲突暂停。原政策、双 CAS、当前资格、精确 FormTo 与剩余槽位，零人工票。 |
| AC-T03-07 | PASS | 本人和显式代理两连接只一票。APP 门内撤权 epoch 变化致旧动作回滚。 |

| 其余原断言归属 | 原 AC | 缺口 |
|---|---|---|
| WP12-08 | AC-T01-01, AC-T01-02, AC-T01-03, AC-T01-04, AC-T01-06, AC-T01-07 | 原卡已有动作上下文和处理证据。本次只核销 W306 已完整复核的新轮及同事务断言，余项供目标复核，含普通批量审批。 |
| WP12-10 | AC-T03-01, AC-T03-02, AC-T03-03, AC-T03-04, AC-T03-05 | 按委派原卡、历史职责来源和相应原断言逐项复核。 |
| WP12-11 | AC-T04-01, AC-T04-02, AC-T04-03, AC-T04-04, AC-T04-05, AC-T04-06, AC-T05-01, AC-T05-02, AC-T05-03, AC-T05-04, AC-T05-05, AC-T05-06, AC-T05-07, AC-T05-08 | 工作日历、UTC/地区规则、截止政策和超时 worker 的完成证据。 |
| WP12-20 | AC-T06-01, AC-T06-02, AC-T06-03, AC-T06-04, AC-T06-05, AC-T06-06, AC-T06-07, AC-T06-08 | 通知偏好、投递、深链与已读水位完整验收。 |
| WP05-19 | AC-T-H40-01, AC-T-H40-02 | PLM 迭代派发及原键由 WP05-19 证明，技术验证和基线边界协同 WP05-20。 |

## 验证、工时与远端回读

Main Kestrel、Vue/Chrome、PostgreSQL 迁移和事务、收件箱鉴权及 Owner 为实际运行。用户、部门、业务输入、流程发布和传送故障使用任务隔离夹具。源不可确认使用真实 PostgreSQL 的停用部门事实，无外部组织网络服务。APP Owner 合同及授权传送的本地夹具范围见上述原件。早期浏览器选择器、APP 测试流程 JSON、读权限夹具及撤回测试的票据统计范围分别修正并复跑。权限夹具修复见 `closing.trx`，撤回精确实例复测见 `closing-fix.trx`。新增回归的三个基线失败保存在 `red.trx`，涉及子任务列表、组织证据及返回状态表示。

| 本地运行原件 | 通过 | SHA-256 |
|---|---|---|
| `verified.trx` | 9/10 | `49908fda77740d38a220c7d559464c8171a791956c6a219a2c42d3031726ad59` |
| `closing.trx` | 2/3 | `0581081fb4d87a911c087963ccb832fa09624714c920c410089d4f47e9437c3d` |
| `closing-fix.trx` | 1/1 | `467ec36e97c90952be4d3416ec34076956e5531af9fc4cee331f9f8353aa988e` |
| `browser-final.trx` | 3/3 | `8ce9790d2c5dee0c0b63b75fd7dd645a3261f5c630f2fc0793648b52d6f9e661` |
| `adjacent.trx` | 33/33 | `ba7b0730aa475841661414f60123729478009c0be319b7c33c4b3be4acba1445` |
| `gates.trx` | 11/11 | `9b85bc1a97f90aa45a35b04b6741b28d3c126ef95d4eaae6fd13b64946edf28a` |

OA Web 328/328、vue-tsc 和 .NET 构建通过。全站试跑 3545 项通过，登录启动夹具两项、Space 旧缓存一项，以及 WMS 用例的 vue-i18n 测试模块加载错误，在领卡基线独立重现，日志为 `web-failing-on-base.log`。源文件、测试日志、Chrome 图和任务锁证据位于 `D:/CP6-test-artifacts/WP12-09/20261123-wp1209`。

按 #407 的原 DELIVERY 完整 SHA 与路径查重，单次补记 WP12-13 收尾 **424.692 秒**至 **4172.010 秒**，其目标复核 13.335 秒单列。计入本卡前计划累计 175182.397 秒。核销快照 `2026-11-23T03:45:23.754165+00:00`，本卡 **2143.656 秒**，本次目标复核另列 **0.077 秒**，累计 **177326.130 秒**。本轮拆卡 0。OA 复算 11/20 完成，候选 WP12-16（4）、WP12-11（3）、WP12-05（2）、WP12-14（2）、WP12-15（2），后续派工归 COORD。 [#407](https://github.com/GTX537/CP6.AI-Handoff/blob/0805999ce0c735a10d92e6f73f4d5dcca470a2fd/handoffs/CP6-DEV-20261012/tasks/WP12-13/replies/20261123T025622Z-coord-acceptance-ack/MESSAGE.md) · [WP12-13 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/68bcfaece7a4600ed0e1ac59fa571de0ce787fe0/handoffs/CP6-DEV-20261012/tasks/WP12-13/messages/20261123T024747Z-exec-wp02-14b-delivery/MESSAGE.md) · [台账](../../roadmap.json) · [执行计划](../../planning/20261117-card-execution.md)

集中审查覆盖相对受保护 Main 基线的全部提交、原断言和状态页。远端采用 SHA、候选同树冒烟、分支处置与 DEV-1 正式释放通过原执行分支 DELIVERY 登记，入口固定在原卡消息目录。 [正式交接目录](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP12-09/messages)
