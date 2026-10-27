# Tool Owner 审批事实接收

W182 接收实际原生 Workflow 已保存的 `oa.app/2.0` 决定。它登记审批事实及独立应用查询流，不执行报废，不生成业务应用回执。原送审意图和决定原件保持不可变。

## 服务和对象权限

`ApprovalFactReceiver` 是独立、默认关闭的宿主配置。启用时指定 `Version`、准确 `EnvironmentId`、`EvidenceMode`（REAL/仅开发 DEMO）和 `Clients`；每项包含既有 C02 `ClientId`、准确 `TenantId`、`PrincipalId` 与 `CanReceive`。复用实际 C02 签名、发行者、CP6.Services audience、cp6.services scope、服务主体和有效未撤销 token 登记，不接受人类 Cookie、客户端传入租户或仅有签名而未登记的 token。

另须由实际 Owner 来源明确提供 `cp6_tool_approval_fact_v2.registrations` 服务登记和 `subject_grants` 对准确原 `SubmissionId` 的权限。迁移不种入身份或权限，不由送审方的旧注册隐式授权 Workflow 回送。新事实要求当前接收及读取权；原键查询/相同原消息重放只要求当前读取权。权限变更参与已有授权根和 Scope epoch，事务末尾重核，撤销再恢复也不能逃过检查。

## 接口与原件

- `POST /internal/wms/approval-results/v2`：有权接收后严格核闭合结构、原对象/政策/轮次、摘要及原生 Workflow 决定和完整证据。同事件同包返回原 Inbox 的 DUPLICATE 结果；同事件异包持久保存冲突并返回 409。不同事件但同决定、同语义保留各自原始入站 bytes，共享唯一事实和应用流。
- `GET /internal/wms/approval-results/v2/{eventId}`：返回已保存 FactAck；本授权 Scope 内未观察到事件为 NOT_OBSERVED。已知事件仍要求准确对象读权；依赖不可用返回错误，不能伪报未观察到。
- `GET /internal/wms/approval-application-streams/{streamId}/result`：已登记且有当前对象读权的流返回 `{kind:"NOT_APPLIED",receiptStreamId,observedAt}`；未知流返回 404。当前没有应用写入器，不从旧实物状态推断 APPLIED。

保存原始入站 bytes、规范包、独立摘要、实际 Workflow 原事件映射和决定证据原件。原生源参与者与接收 Owner 在同一 PostgreSQL 事务中使用已验证的 Scope；此实现不声称具备跨宿主证据获取资格。原工作流/人类提交权后来撤销不改写历史决定；当前服务和对象读写权独立检查。DEMO 原决定不能登记为 REAL。

正常终态序号为十进制文本 `1`。大于 1 的缺口包持久登记为 GAP，202 返回 NOT_APPLIED 且没有事实或应用流；后续源查询/补洞投递仍是待实现范围。应用流由服务端创建，天然唯一键是 Scope、InstanceId、Subject.businessRevision、ApplicationUnitId（准确处置请求 ID），不会随重发 EventId 变化。

接收一次提交包含事实、流、Inbox 和审计；数据库延迟约束同时核实际原生决定来源、准确业务原件及不可缺失的 Inbox/流，禁止修改历史行。提交确认丢失为 OUTCOME_UNKNOWN，调用方以原事件查询，不能生成新决定来假装恢复。

## 保留范围

当前 Tool 的工单、借用、库存等关闭义务没有完整实际来源，审批接收不能解除它们。实际业务适用性检查、业务变化与 ApplicationReceipt 共同事务、应用回执链、决定可靠发送及缺口恢复、原生办理页面和当前业务视图继续独立接入。SYSTEM_EXECUTION/WITHDRAWAL 仅有输入结构识别；原生决定来源尚不提供这些终态时拒绝处理，不宣称已经支持其业务执行。真实运行登记、企业政策和正式验收未由本地夹具替代。
