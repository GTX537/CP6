# W227 退货政策发布与原结果恢复

COORD在本聊天单人执行，沿用executing-plans、固定席位和一次集中审查。预计实际60–100分钟；保持原七天目标。

目标：在现有RMA可信仓库/站点范围内，由具备独立policy-publish动作的人员明确发布退货原因、允许处理方式、签收后期限及SOD要求；保留不可变版本、当前头、原命令恢复和实际页面。

- [x] 在RmaFormalOwnerV1.Policy.cs及RmaPolicyContractV1.cs实现发布/读取/原键查询，复用现有Scope、权限、事务与MainOwnerOperationJournal；policy_heads与policy_versions按租户/仓库/站点保存，CAS更新、旧版本不改写。
- [x] PostgreSQL迁移登记policy-publish动作但不自动授予角色，不插入任何企业政策；策略明确Enabled、期限（null是发布者明确不设期限）、原因选项、处理方式及独立审批要求。无政策/停用保持明示，不补默认成功。
- [x] RMA接口与实际页面提供原政策/当前政策、编辑发布/停用、身份隔离的原请求恢复；政策只供申请参考，申请意图不因缺政策变成授权，授权时仍须实际source cut/额度/权限。
- [x] 相关PG/HTTP验证覆盖版本CAS、共同事务回滚/未知提交、当前撤权/跨租户/范围、不可变历史和边界；Vue验证实际发布/展示/身份与恢复，类型检查。无变化结果复用。
- [ ] 一次完整任务审查，同范围同步台账、四份状态和交付报告；正常PR/准确任务分支清理/official finish释放席位。

依据：WMS-RMA R3 5.1/5.7.1–2要求有明确policyRef/version与按政策执行SOD；W224–W226已有真实申请/原结果/原商业来源。当前没有企业退货政策源，新增本域政策管理入口为Owner扩展，不冒称RMA-C03已采用。完整签收、发票/交期、source cut、E/A/P和IN仍为后续真实采用；不从已发数量推可退额度，不另造ERP审批账。

## 交付快照

[报告](../../evidence/W227-01/DELIVERY.md)。登记30.48分钟，代码/相关验证/集中审查完成，Handoff已释放；Main正常PR/finish仍待完成。最终生命周期以wave227-final-verification.json为准。
