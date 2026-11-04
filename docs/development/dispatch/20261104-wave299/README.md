# W299 — 版模处置实际Owner审批撤回

版模处置实际Owner可信审批撤回、准确本轮业务授权/BLOCKED显式接续、共同控制头与原结果恢复；保持原停用、未清义务及不能终结报废的真实边界。

Main基线`1a4e6edac635000951eb7b4ec5d12edecd850691`，`codex/w299-01`，DEV1；Handoff基线`f35bcb1e73ceb60f19e5057a459ca80e9e666a3e`，`codex/w299-handoff`，DEV2。COORD单人，开始有效累计5776秒，原初估1.5–3h低置信；累计2h统一校准。

## 源码事实与任务边界

实际业务为`ToolDispositionApprovalOwnerV1`冻结单一准确处置请求/送审；`ToolApplicationOwnerV2`原批准应用仍受真实义务缺证阻塞，`ToolApplicationReceiptStoreV2.Append`拒绝伪APPLIED。`ToolDispositionOwnerV1`的BLOCK立即限制新使用/维护恢复，本任务审批撤回不解除它，不删除原请求或关闭外部作业。现提交仅每工具一条，既有合同不支持新轮次，不照抄需求集的替代轮次。

- [ ] 按真实Scope/工具/准确请求/原送审建立控制头、不可变撤回事实/Outbox/业务grant和三个前向PG迁移；现旧提交与原BLOCKED/STALE回执显式采用，GET不造历史。
- [ ] 真实`ToolDispositionApprovalOwnerV1.Withdrawal/Delegation`和`OperationsOwnerV1`：原认证实际人，独立dispose-withdraw/dispose-delegate，当前实物Scope/字段/Remarks、准确原申请读取，独立grant当前版本/最终fence；TASK权限不代用，无默认授予。
- [ ] `ToolApplicationOwnerV2`读同control，ETag覆盖当前头、最终业务决定点核对；WITHDRAWN后迟到审批只STALE，保原终态/原键。真实BLOCKED不冒APPLIED，不制造报废、库存或财务效果；实际两连接竞争按当前合法状态断言。
- [ ] `ApprovalBusinessEvidenceV2`明确版模分派；实际`ToolWithdrawalDeliveryV1`/DI/worker沿现可信路由按原body/原actor查询优先；pending intake、BLOCKED新明确授权、旧命令被接续后的拒绝和丢答恢复。
- [ ] 原`ToolDispositionApprovalController`及版模现页面/API/恢复模块采用，保准确工具/送审，区分审批撤回、物理停用与运输状态；显式grant/再授权、刷新原操作、身份变化清理，不在恢复缓存存原因明文。
- [ ] 定向PG/认证HTTP/真实页面验证当前Owner权限、共同控制、原事实/Outbox/原命令原子性、BLOCKED显式接续、当前义务不变、源/租户边界和前向采用。未变wire/TLS有效来源复用，不全仓回归。
- [ ] 一次任务集中审查、单一报告、四项目状态/台账/生成视图同步、Main/Handoff逐个正常合并、远端核对、准确分支清理和官方finish释放。

沿PostgreSQL18、模拟业务数据、Trust Internal Contracts、Fail-Fast。24h目标继续，原37530DONE/2IN_PROGRESS/343PLANNED、原59余53及125/247不变。不触发Actions、不部署、不迁移既有数据库。
