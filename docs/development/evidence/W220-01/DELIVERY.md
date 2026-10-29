# W220 原生退货申请 Owner

原生退货申请API独立保存DRAFT/SUBMITTED不可变版本，不再将申请视为授权；实际出库来源、完整21,8数量、当前权限、同事务原操作与审计已落地。16项相关PostgreSQL/HTTP验证通过，0跳过。

[合同与入口](../../rma-native-application-v1.md)。新增RmaApplicationOwner/API、当前RMA来源/字段授权、PostgreSQL独立Head与不可变版本存储。复用OperationsOwner事务和Main原操作日志；边界校验一次，内部直接信任既定合同，不增加资格/签名包装、重复哈希核验或序列化冻结。

实际验证：16项本地PostgreSQL/HTTP通过、0跳过，包含现有旧RMA命令保持原拒绝的一项回归。完整decimal(21,8)、申请历史、来源变化、原操作冲突、当前字段/仓库/租户、强ETag、并发单次提交、日志失败整笔回滚、丢失提交回应后在撤销add权限下读取原结果、关闭未提交操作及实际HTTP拒绝伪造状态均覆盖。迁移在隔离测试库实际应用；未运行全仓回归或重复已通过验证。

证据：`D:/CP6-local-test/wave220/foundation/related-fixed-20261029T033541Z.json`，同名日志及results/TRX。首次`related-20261029T033341Z`仅编译失败：测试辅助方法Applications与已有Tool方法重载冲突；改为RmaApplications后上述16项通过，失败记录保留。未把未运行的首次测试计为通过。

Versioned native application contract, not formal RMA-C01 or full OriginalFulfilment/entitlement adoption. Existing RMA effect commands remain fenced. 前端申请页未接入；本轮是服务/API交付。正式来源/权益和独立授权审批仍未实现，原始D2出口及其他七天未完阶段不关闭。正式125目标、247验收和375任务状态不提升；只为WP10-22附记限定交付。

一次完整任务diff审查，检查事务、权限、数量、来源版本及原操作恢复；没有额外审查代理。没有依赖/工作流变化、Actions运行/取消或部署，也未迁移既有环境。

预计45–75分钟，截至快照登记16.9分钟；最终START到finish计一次，不叠加Handoff时间。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/196)已合并，准确分支已删除，DEV-2已释放。Main当前为PR前快照，完成远端采用后由官方finish清理准确分支并释放DEV-1，最终本机wave220-final-verification.json记录结果。固定目录、用户资料和缓存保留。

下一步：Continue actual D2 return authorization/fulfilment adoption and other original seven-day exits. Do not reopen completed waves or add qualification wrappers.
