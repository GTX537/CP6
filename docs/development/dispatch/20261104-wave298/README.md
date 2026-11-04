# W298 — 需求集实际Owner可信撤回

需求集实际Owner可信撤回、同control应用互斥、精确业务授权/BLOCKED显式接续和原结果恢复；准确需求集/成员源与EvidenceMode保留。

基线`afc3cc08a081f2affc3f7033b758e312838169d6`，分支`codex/w298-01`，DEV1；Handoff基线`eef6ab08facda49f6b0e89fce6acfa72df79b205`，`codex/w298-handoff`，DEV2。COORD单人，正常配额内复用固定目录，集成席位空闲。开始时目标有效累计3730秒，初估1.25–2.5h仍为低置信工程区间；约2h统一校准。

## 原范围与实施

原WP12-19其他Owner采用余项；DOC已在W297交付，不能将DOC/ITER证据推广为需求集完成。现RequirementSet Review已有精确原版本/专业政策/决定/应用，但无可信撤回控制头。原业务需求正文/成员授权和模式来自`RequirementSetService`，撤回仅终结本轮审批控制，不逆转已应用结果。

- [x] `RequirementSetReviewControlStoreV1`及三个前向PG迁移：真实Scope/需求集/送审轮次控制头、不可变withdrawal/outbox/grant、旧提交明确采用；不GET造历史。
- [x] `RequirementSetReviewOwnerV1.Withdrawal/Delegation`、`.Application`与现Owner：原认证实际人、精确grant、当前对象/字段/原成员读授权和EvidenceMode；独立动作不默认授角色；withdraw/apply共CAS，先应用保原receipt，先撤回迟到决定STALE；撤回后新轮次不分叉。
- [x] `ApprovalBusinessEvidenceV2`明确需求集分派、`RequirementSetWithdrawalDeliveryV1`实际DI/worker：同原body/键、query-first、未知结果保持；新明确授权接BLOCKED，已接续旧命令不再生效。共用wire不另造权威合同。
- [x] 原`RequirementSetReviewController`、`requirementSetReview.ts`、`RequirementSetProfessionalReview`及原操作恢复：真实Owner上下文/独立委派/撤回，页面区分业务与运输，明确再授权/丢答查询、身份切换清理，恢复记录不存正文。
- [x] 定向PG/认证HTTP验证本Owner竞争/回滚、先后顺序、精确grant变更/BLOCKED、新版本原件/成员权限、跨租户/EvidenceMode拒绝和旧提交采用；复用不变wire/TLS证明。受影响Vue及一次真实浏览器到PG恢复链，保实际运行来源，不重复全仓。
- [ ] 一次任务集中审查、单一报告、四项目状态/roadmap同步、Main/Handoff正常合并核远端、准确分支清理与官方finish释放。

模拟业务数据、PG18、Trust Internal Contracts、Fail-Fast和精简验证沿用。不触发Actions、不部署、不迁移既有数据库。原卡暂不DONE，下一W299实际版模处置Owner。

## DELIVERY

W298：需求集实际Owner可信撤回、原审批应用共control、准确本轮业务委派/BLOCKED显式接续、不可变原结果及实际页面恢复完成本地验收。 [单一报告](../../evidence/W298-01/DELIVERY.md)。最后一项的相关验证/集中审查与文档已完成，远端采用/finish仍待核对；之后先独立修复BUG744，再进入W299。
