# RMA-CORRECTION/1：退货记录纠错与准确冲正

本版本实现实际退货实收及 RMA-DISPOSITION/2 五种处置的纠错流程，独立入口为 `wms-return-correction`。沿用 Stock SX 草稿、编辑、送审、独立批准绑定、冻结执行、应用与撤回语义；不改写历史 R3 合同，不提供任意库存正负数量入口。

生产验收允许全模拟业务资料、错误事实和独立审批；实际 Owner、PostgreSQL 事务、库存流水、业务后继与恢复路径必须执行。`simulation=true` 表明事实来源。此处“记录错误”指原先记载的实物动作没有发生；实际发出后重新退回仍使用新的正常退货业务。

## 状态和接口

`api/wms/rma/v1/corrections`：读取 `applications/{app}/context`；POST `applications/{app}/proposals` 建立草稿；POST `applications/{app}/proposals/{id}/edit` 修改；POST 同一路径下 `submit`、`bind-approval`、`prepare`、`apply`、`withdraw` 执行动作。写入必须携带 `Idempotency-Key`，使用当前 `headRef` 做并发比较。`commands/query` 只用原申请 ID 与命令 ID 恢复已保存结果。

`DRAFT → SUBMITTED → APPROVED → POSTED`；非 POSTED 可以 WITHDRAWN。草稿内容变化追加版本，无变化不增加版本。提交后范围、数量、原因和错误依据冻结；批准绑定确切 proposal/version/contentDigest。准备冻结 Stock 快照、原动作范围、根份额、批准引用和执行键。旧快照失效需撤回原方案后新建，不偷偷替换原请求。

动作权限分别登记，不自动授予角色。执行还需要原效果 Owner 的 `correct` 和 RMA `correct-effect`；实收 VOID 需要 Quality `source-void`；可售放行更正需要 Stock QC `correct-return-release`。每次读取、原请求恢复均检查当前租户、申请、原来源以及源/目标库存范围。已有结果不要求保留写权限。

## 独立模拟来源

`ReturnCorrectionSimulation:Enabled` 和 `CasesJson` 配置精确原效果。每项含 `tenantId`、`siteId`、实际 `originalEffectRef`、`version`、`label`、`independentApproverId`、`validFrom`、`validTo`、`recordedPhysicalEffectDidNotOccur: true`、`errorEvidenceRefs`。批准人必须与草稿创建者不同。服务端读取当前配置后绑定实际候选，准备和执行再次检查批准版本与有效期；客户端不能提交“已批准”或“库存成功”标记。

未配置案例只表示当前缺少该模拟场景，可直接补充模拟输入；不要求企业提供真实数据。这个明确模拟来源属于本轮验收配置，不构造通用 OA 审批模板或等待外部审批系统。

## 实际库存、范围与后继

- 实收：准确已收范围从 LOCATION 移至 VOID，原初始 root 数量与历史实收不变；当前 return claim 从 CONSUMED_BY_RECEIPT 转为 VOID_BY_SOURCE_CORRECTION，原 claim 不重新变成预留。
- 可售、返修：原 MOVE 两腿作为完整组反向处理。可售追加准确 QC 放行更正依据，保持原质量判定/放行历史；不把整池库存重置为某一质量状态。后续控制或身份不相容时失败。
- 报废、退供应商、退客户：根据模拟的记录错误事实，恢复原范围到原地点，保留原动作和新的冲正流水。不会隐式释放其他预留。
- 提交占用原实收行的准确范围，阻止新处置抢占；实收冲正前检查所有 PREPARED 或仍有效的 APPLIED 后继，先由对应 Owner 关闭未执行意图或准确冲正已执行效果。不存在“没有结果即无效果”的推断。
- POSTED 候选构成不可变 ReversalUsage，按原效果累计扣除预算；换命令键或新候选都不能再次冲正同一范围。撤回只有未执行方案的无效果证明。恢复到有效实收的处置范围可产生新意图，旧意图不重新执行。
- `sourceClosureRef` 精确标记原错误范围永久关闭，`successorRef` 绑定 RMA 新 head 与实际 Stock 回执。Quality 追加版本，将 VOID 从四个分区扣除，原判定与观察保留；旧观察变为过期，后续判定只能覆盖剩余有效范围。若冲正发生在建立检验之前，创建检验时从已保存 VOID 初始化剩余 PENDING，不能据此推定合格。

库存当前数量、预留、召回、地点、容量、盘点冻结、两腿身份和已有负向流水在相关路径检查。无法确定 root 去向的后续原生负向流水拒绝冲正，不能拿同批次其他数量填补。完整逆向组、Stock 序号、QC 更正、RMA claim/后继、Quality VOID、冲正原结果与事件记录在一个本地事务提交。

前端以私有 IndexedDB 保存确切请求，Web Lock 防止同身份多页重复派发；刷新或断线后先查询原结果，需要时重派原键原正文。身份或申请变化后不展示旧响应，当前读取被撤销后清除已展示数据。

## 范围与验证

本轮覆盖原退货实收及上述五种已实现处置。非 RMA 通用 Stock 更正、独立商业/财务出口、完整返修/NCR 和外部事件派发继续按原目标推进；事件记录不冒充已送达外部系统。PostgreSQL 迁移仅在隔离本地验收数据库运行，没有部署或改写已有环境。实际证据及生命周期见 [W233 交付报告](evidence/W233-01/DELIVERY.md)。
