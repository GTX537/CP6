# W44-01 真实 START / 七天一期 D4 库存采用

COORD本聊天单人；2026-10-22T06:44:48.915061+00:00；Main基线`3ed818471dad9fd68b4b49971de72b20886c9caf`。W43 PR397/398、Handoff19已采用；实际34.94分钟，两任务分支已删除、固定席位实际finish。W37–W43不重复。

限定范围：库存调整/移动实际 Owner 的持久原操作与查询/关闭恢复。REP需要的正式Root/Claim/Transfer、RMA正式适配器与SCHED资源重绑定尚未实际采用。本窗扩展D4已采用Main journal到库存单腿/双腿事务，不作为正式TRANSFER/REP采用；Finance bridge仍为提交后best-effort，不能借原结果恢复重新发送或宣称可靠投递。

DEV-1 `codex/w44-01`，DEV-2 `codex/w44-handoff`；预计本聊天实际1–2小时，包含相关测试/集中审查/合并/席位收尾。复用Main journal，无新迁移。新版本命令必须显式原键，保持既有合法库存输入/精度与当前最终权限；同键异体拒绝，库存/序号/流水/审计/原结果一事务。原结果按当前query、原actor及精确Stock/Transaction身份检查；查询无业务/日志/root revision写入，UNKNOWN不证明回滚；明确close阻挡晚到同键。外部桥与通知只在首次已知提交后调用，不因原结果重放再次发送。

现有StockQuery页面没有调用apply/move，客户端仅接入有版本API与查询/关闭方法；不新增臆造操作页面、不宣称页面采用。真实HTTP/CSRF、PG重复/并发/撤权/双腿回滚/提交确认丢失和客户端接口定向验证。

Main窗口`docs/development/dispatch/20261022-wave44/README.md`；总计划`docs/development/planning/20261022-seven-day-delivery.md`；roadmap唯一状态源。WP02-10整卡、D4可靠投递、125目标与247运行验收保持未完成。PostgreSQL、固定目录、精简本地验证、正常PR/准确分支清理/finish；无代理/新树/Actions/既有环境迁移/部署。


## 候选验证

38项去重相关.NET通过（10项新增PG/真实HTTP、28项既有库存/Owner边界），3项客户端Vitest及完整vue-tsc -b通过。一次作者集中审查及测试观察修正完成；[DELIVERY](../../evidence/WP02-10/stock-original-operation/DELIVERY.md)。正常远端采用/准确分支及finish待执行。
