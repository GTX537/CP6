# 库位规则送审与审批证据绑定 v1

落实 WMS-MASTER-01 v1.1.1 §7.1/7.2 的 08S、08Q、08W。业务主体仍为 `Main.Wms.LocationRuleProposal.v1`；直接采用[现有候选 Owner](location-rule-proposal-v1.md)及[真实字典与 UOM 来源](location-rule-references-v1.md)。审批绑定成功只到 `APPROVED_PENDING`，08E 生效另有前置。

## 准确候选与原生审批

独立入口 `/wms/location-rule/{proposalId}/review`。资源 `wms-location-rule` 新增 `review-policy`、`review-submit`、`review-decide`、`review-bind`；query、完整字段读取、真实 Location/Warehouse 范围和来源权限仍须当前有效。目录登记不自动授权。政策维护使用实际启用角色，显式每项最少人数、有效期、作者/提交人分离选择和拒绝后 `RETURNED`/`REJECTED` 行为；分离可明确为 false，不能用默认真假代替决定。外部审批要求目前仅允许空列表，不能伪造外部来源。

POST `/api/location-rule/owner-v1/{id}/review-policies` 保存不可变新政策。POST `/{id}/review-submissions` 必须传原 operationId、强 If-Match、准确 candidate Owner/id/version/digest、policy、reviewRoundId、reviewBasis、declaredChangeRelations、replacesSubmissionId。仅完整且当前 RESOLVED 的 DRAFT/RETURNED 候选可送审。当前字典、真实 UOM/BASE、字段/数据范围和政策再核验；冻结准确原件、前版引用/完整原件差异摘要、依据与关系，同事务保存送审、SUBMITTED、原操作和 outbox。

冻结包中的原件使用原始 JSON 值，保留原时间字符串、偏移和精度；不让 OA 的毫秒编码重写既有候选。OA 主体 `WMS_LOCATION_RULE_REVIEW` / `LOCATION_RULE_PROPOSAL`，snapshot `Main.Wms.LocationRuleReview.snapshot/1`。实际原生 FLOW intake、绑定、发布版本、任务分配及人类签署均沿用现有 OA；没有客户端 approved 标志或任意外部批准替代。签署时重核实际人、委派 principal、当前专业角色和分离，达到每项不同 principal/actual handler 人数后才产生正式决定。

## 08Q、退回与撤回

可信原生决定通过固定接收入口进入不可变事实流；浏览器 POST `/{id}/review-binding` 只提供原 UUID、submissionId、expectedReceiptId、原因和 If-Match。服务端读取真实决定/轮次/subject/version/digest，批准再核当前候选、政策、来源与权限，然后保存准确 `Main.Wms.LocationRuleApproval.v1` 及回执：

| 触发 | 当前状态 | 结果 |
| --- | --- | --- |
| 正式送审 | DRAFT / RETURNED | SUBMITTED，固定当前轮次 |
| 当前轮次批准绑定 | SUBMITTED | APPROVED_PENDING |
| 当前轮次拒绝绑定 | SUBMITTED | 冻结政策指定 RETURNED 或 REJECTED |
| 退回后实质编辑 | RETURNED | 新版本 DRAFT，来源重置 UNRESOLVED，清除当前轮次 |
| 同版退回后重审 | RETURNED | 必须指向准确被拒绝轮次，产生新轮次 |
| 合法撤回 | 任一非 WITHDRAWN/EFFECTIVE 状态 | WITHDRAWN，保留原版本、轮次和历史 |
| 撤回或非当前轮次的迟到决定 | 非当前 SUBMITTED | STALE 回执，无新批准绑定和业务状态效果 |

已有准确 APPLIED 回执按历史返回，不能覆盖后来轮次。APPLIED 在本合同仅表示 08Q 证据绑定；任何回执都不能解释为容量/品类/动作规则已生效。选择器仍为 revision0/current=null，原生库位隔离、容量和 Stock 原件不变。批准绑定与撤回竞争同一对象前驱；失败整笔回滚。

## 可靠投递与恢复

闭合的传输身份和 PostgreSQL 投递白名单显式包含该 Owner，不能从请求正文拼接 SQL schema/目的地址。固定决定接收 `/internal/wms/location-rule-review-results/v1`；结果/回执链 `/internal/wms/location-rule-review-binding-streams/v1/{streamId}/result|receipts`。OA 回执摄取复用现有固定入口。实际 C02 client_credentials、当前租户、注册、subject grants、REAL/DEMO 和固定 origin 必须一致；丢失 ACK 先查询原事件，禁止盲目再发。

原操作查询 `review-operations/{uuid}`、`review-binding-operations/{uuid}`，对应 POST `/close`；本人真实任务由 `/api/oa/tasks/location-rule-review/{id}` 提供，签署恢复使用原生 OA command。写权撤销后当前 query 仍可恢复本人原结果；读权撤销不披露。UNKNOWN 保留原键，关闭确认前不另生成 UUID 重试。浏览器只保存作用域与对象/操作 UUID，使用 Web Locks 和当前身份复核，不持久保存私人意见、规则正文或服务凭据；晚响应和不同来源回执拒绝展示。

## 宿主配置与交付边界

`LocationRuleReview:EvidenceMode` 必须明确 REAL 或 DEMO；环境 Scope 必须真实注册。投递/接收的 `LocationRuleReviewSubmissionDelivery`、`LocationRuleReviewDecisionDelivery`、`LocationRuleReviewReceiptDelivery`、`LocationRuleReviewReceiptRecovery` 均默认关闭；接收复用 `ApprovalFactReceiver`，实际字段以 Program 绑定与共用 options 为准。需要受信 C02 客户端和 Secret provider、实际审批绑定、租户授权及专属主体 grants；不种植默认审批人，不写机器专属配置。受理/接收注册仅在开发环境允许 DEMO。

PostgreSQL 前向迁移新增政策/送审/outbox、投递、可信事实、批准绑定/回执链、状态及明确投递 Owner；原件不可变、外键、当前事务 journal/事实来源与启用触发器共同约束。只在隔离本地数据库验证；不执行既有环境迁移。

[本轮交付](evidence/W211-01/DELIVERY.md)记录实际验证、修复、复用来源及收尾。08E 所需当前时钟政策/占用和效果 Owner、Selector 生效前驱、仓库/库位激活、正式 Transfer/Stock 及七天完整出口均保留。没有 Actions、生产候选、真实企业验收或部署结论。

## W212 时间前置

规则审批页可对准确候选进行[原生时钟政策与时间评估](warehouse-clock-policy-v1.md)。CURRENT取真实认可来源观察，PLANNED保留带偏移指定时刻且只预测；未知来源不放行。时间满足仍不构成08E应用或作业准入，原审批与Selector语义保持。
