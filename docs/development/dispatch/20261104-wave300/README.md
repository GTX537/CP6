# W300 — 库位规则实际Owner审批撤回

库位规则实际Owner可信审批撤回、真实提案控制版本与原审批应用竞争、独立业务授权/BLOCKED显式接续、原命令及页面恢复；保持旧提案入口和未生效规则边界。

Main基线`324f722644e6e79df0ac75dfc3396d0f1858306d`，`codex/w300-01`，DEV1；Handoff基线`05992786234fb4d3425b5b160020632d1ee01775`，`codex/w300-handoff`，DEV2。COORD单人，开始有效累计8818秒，当前估计0.75–1.5h低置信；累计14400秒校准。

## 实际调用链与执行清单

原卡WP12-19；当前`LocationRuleOwnerV1.WithdrawAsync`与`LocationRuleReviewOwnerV1.ApplyAsync`共同读写`cp6_location_rule_v1.proposals`，已有真实revision/token、不可变原件及提案journal。当前审批应用只生成APPROVED_PENDING，激活与库存效果属于其他任务。直接前置沿现有Scope/OperationsAuthority、MainOwnerOperationJournal、oa.app冻结提交/可信撤回以及实际LocationRule评审，不新设证明包装。

- [x] 复用提案真实revision/token作为共同control；可信撤回事实、Outbox、原命令及提案journal同事务。前向PG迁移仅追加必要存储/严格原子约束；旧原件、状态和合法提案入口保留，无GET造历史。
- [x] 实际LocationRuleReviewOwner撤回、原操作查询/关闭、独立业务委派、BLOCKED原事实不变的新显式接续；真实权限/字段/当前grant与最终fence，原实际人不能由worker改写。
- [x] 原Apply和提案撤回参与真实控制竞争；先APPLIED后withdraw返回原应用，先withdraw后迟到决定STALE；既有提案撤回无可信事实时只能当前人新明确请求，不自动补历史。
- [x] 显式ApprovalBusinessEvidence分派、原body/原键查询优先运输、合法RETURNED、pending intake、失答恢复和旧BLOCKED被接续后的拒绝。既有网络合同复用。
- [x] 原库位评审页面/HTTP采用，准确对象与送审、独立当前身份缓存、明确业务撤回/运输/规则未激活区别；实际授权与刷新原结果恢复。
- [x] 定向PG：真实应用/撤回双连接竞争、旧提案入口、同事务故障回滚、当前授权及字段拒绝、原键恢复、grant接续和原始数据精度不变；相关Vue/类型与实际Main登录HTTP/Chrome。
- [ ] 一次集中审查、单一报告、四状态/台账/生成页同步；Main/Handoff正常合并、远端采用、准确任务分支清理、进程停止与官方finish。

约24h目标继续；PostgreSQL18、模拟业务数据、Trust Internal Contracts、Fail-Fast、精简本地验证。原37530DONE/2IN_PROGRESS/343PLANNED，原59余53，125/247不变。无子代理、Actions、生产部署或既有环境迁移。

## DELIVERY

W300：库位规则实际Owner可信审批撤回、原提案共同控制版本/应用竞争、独立业务委派与BLOCKED明确接续、原命令和真实页面恢复完成本地验收；旧提案入口、原件和APPROVED_PENDING未激活边界保留。 [单一报告](../../evidence/W300-01/DELIVERY.md)。相关验证与集中审查通过；远端采用/finish待核，随后W301整卡核销。
