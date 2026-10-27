# W176-01 版模处置审批冻结与原键恢复

COORD单人继续CP6-7D-20261022。Main基线`00b45abb5987d235c6c9cb85819373a8926746ff`，复用DEV1/DEV2。W175已归档一次，仅关闭WP12-06.wave175_approval_binding限定字段。

## 实际交付

WMS原处置申请现在可通过独立有权政策冻结送审。`dispose-policy`登记/撤销适用原因、所需人工批准人数、申请人/办理人分离要求、准确Site、有效时段及依据；`dispose-submit`保存送审意图。权限种子只登记动作名，不授角色，不种入现实政策。旧REQUEST/BLOCK及未知义务保持原义。

Subject以原处置REQUEST原ID/版本1及原事实摘要为权威，不将实物编号、当前版本或一次HTTP键伪作新审批业务身份。原摘要保留Main.Wms.ToolDisposition.fact-json/1域；政策和冻结快照使用明确排序JSON域，原Unicode不归一。受信EnvironmentId从部署Scope取得，映射为小写UUID字符串；Tenant/Starter来自真实身份，服务包不接浏览器伪造。businessSubmittedRowVersion以原不可变REQUEST的正bigint序列无损映射8字节Base64，仅标识该申请版本。

冻结事务保存准确Subject、Policy、当前实物/Site及义务快照、永久IntentIdentity、真实发起授权证据及职责分离人员、完整oa.app/2.0 SubmitCommand原包、OwnerOutbox、原命令结果与审计。Submissions/Outbox双向延迟外键保证同次提交，原包必须逐字相同，所有事实禁止更新/删除/截断。审批意图唯一约束不因HTTP键或运输说明变化而重置；同意图同发起人和授权/快照才关联原Submission，异身份/内容冲突。原意图待投递期间不同政策不能开第二审批。

认证根、当前原Owner动作/对象/字段/租户、可信Scope门、政策发布人当前权限和准确来源均在同一PostgreSQL事务最终核验。集中审查发现并修正末端嵌套读取后的实物复核顺序，防止同UoW晚写漏过先前检查。已有原命令只需当前读权，不要求政策或送审写权仍有效；同键异载荷拒绝，未知提交查询原结果；未观察不当作回滚，可在同一排他门下封闭未提交键。

实际API在`/api/wms/specials/v1/tools/{id}`下增加approval-context、disposition-policies、approval-submissions；原键查询位于`/api/wms/tool-approval/v1/operations`。政策和冻结保存返回201；提交状态明确为DISPATCH_PENDING，实例尚未启动。原版模处置页增加政策、送审、原结果恢复入口；身份绑定最小恢复记录、冲突保输入、晚到结果拒绝，权限失效清显示。

## 本地验证与集中审查

证据：`D:/CP6-local-test/wave176/foundation/final-validation.json`，源码/测试blob绑定。

- 18项不同PG18/真实HTTP检查：本轮最终8项审批行为全部通过；最初运行中10项未改处置回归成功，按原来源复用。覆盖准确冻结原包/无实例或报废副作用、双连接重复键、丢失commit响应、写权撤销后读原结果、政策失效、错误/过期输入、实物晚写及末端撤权整体回滚、真实登录API201和未知字段拒绝。
- 15项Vue/恢复检查（5审批、6处置、4权限恢复）、类型检查与Vite构建通过，0skip。前端未再变化，构建不因提交SHA变化重跑。
- 初次运行4项失败、下一次2项失败完整保留。包括测试查询/注入缺租户、夹具政策授权来源与实际宿主不同、测试SQL表名，以及真实实现的末端实物复核顺序问题；分别定位修正，最终定向验证通过。没有把失败改称成功，也没有为绿色结果删除必要风险场景。
- 一次COORD任务级SPEC/QUALITY覆盖完整diff、实际Owner身份/政策/冻结/永久意图、原子性、当前权限、同事务变化、数据库约束、页面和恢复。正式规范验收与本地行为检查分开。

## 原目标剩余范围

这是WP10-48/36及OA-APP的实际Owner本地冻结子交付，完整任务状态不抬高。仍需真实服务注册与传输资格、Outbox worker、Workflow TxS绑定选择/精确版本固定、TxE引擎启动、TxD实际决定及DecisionOutbox、Owner Inbox/业务应用与ApplicationReceipt。需要准确政策/授权证据读取与当前资格适配时，必须消费本轮原记录，不靠摘要等同签名或原人类授权持续有效。四项最小采用门未齐，不声称v2运行上线。

WORK/BORROW/STOCK/OA等外部义务仍UNKNOWN，不能以空清单或审批提交终结报废；Stock/Finance处置不自动生成。没有默认现实政策或审批批准。无浏览器直连PG端到端、247真实验收、企业验收、Actions、既有环境迁移或生产部署。七天D1/D4完成、其余阶段部分完成的状态保留。

## 收尾快照

Handoff正常合并：https://github.com/GTX537/CP6.AI-Handoff/pull/152；source `ad859bc56b40ddeb5d5c75f2bb395ac5531f13e0`，目标 `6e283a0f052f8b5555d6d82f5cad24b1c9b29888`；准确树采用已核对，任务分支已定向删除、DEV2已finish释放。目标仍为codex/t02c-entry-handoff-20260921。

本报告提交时Main待正常PR/远端核对/finish，不预写已释放；最终结果见本轮外部final-verification及checkpoint，下轮台账据实归档。

预计90–150分钟；本报告登记25.68分钟，最终实际以官方Main START→finish计，Handoff不重复累加。固定目录和依赖缓存保留，根目录用户原件保持。

下一步将本轮准确冻结原包接入受信服务传输和OA-APP接收/启动/决定闭环，并继续原工程、WO/SCHED和D2/D5/D6/D7出口。
