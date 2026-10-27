# 原生审批决定可靠投递

W183 消费 W181 实际终态决定 Outbox，并投递到 W182 Tool Owner 事实接收入口。原 EventId、DecisionId、Scope、序号及 payload bytes 保持不变。原人类票决结果不会被网络结果改写。

## 登记与身份

`ApprovalDecisionDelivery` 默认关闭。启用需明确 `Version`、`DestinationOrigin` 和 `Clients`（TenantId、ClientId、来自宿主秘密提供者的 ClientSecret）。映射采用独立 `ApprovalFactReceiver` 的服务登记，使用既有 C02 client_credentials、准确 CP6.Services audience、cp6.services scope、短时 token 与实际 token 登记。不会创建默认服务或凭据。原生共同库还须有真实的接收服务注册和准确 Submission 的 Subject 授权；迁移不种入这些来源。

来源和目的 origin 都来自受信配置；业务 URL 固定为 `/internal/wms/approval-results/v2`，查询追加原 EventId。HTTP 禁止重定向和 Cookie，生产验证平台 TLS 信任。响应中的链接不能改写网络目的地址。DEMO 仅在开发宿主可用，发送前核原生决定保存的证据模式与登记一致。

一个已有投递只能调整登记 Version 来恢复同身份的凭据配置，不能把原消息改投其他环境、租户、服务 Principal、issuer、destination 或 Owner。真实跨宿主身份/来源资格仍须另行核验；本轮证明隔离本地 TLS/C02 和实际原生 Owner 路径。

## 持久过程

1. 在 PostgreSQL 原生授权根和 Scope 事务中检查当前注册、准确 Subject 读权及原始决定，保存租约、递增 epoch、随机 token、登记版本和尝试。没有读权的对象不交给网络发送器。
2. 提交领取事务后，查询接收端的原 EventId。只有 200 且身份/结构匹配的 NOT_OBSERVED 才 POST 同一原包；403、404、503、超时或畸形响应都不能授权重投。
3. 接收端返回 PENDING_VALIDATION 时持久登记并继续原键查询；返回匹配的 FactAck 时记录运输 RECEIVED。GAP/CONFLICT 和应用字段保留协议含义；当前源终态序号为 1，不能接受不可能的序号缺口确认。
4. 另开原生事务重核当前注册/对象授权，以 epoch、token、未过期租约和旧状态做 CAS。确认记录与状态共同提交；迟到 worker 返回 LEASE_LOST，不能覆盖当前 worker 的结果。

领取提交确认丢失时不发网络请求；接收端已提交但 HTTP 响应丢失时查询原事件；本地确认提交应答丢失时读取已保存状态。OUTCOME_UNKNOWN 保留同一事件等待恢复。权限拒绝进入 BLOCKED，只有明确路由版本或权限登记版本变化才重新尝试。CONFLICT/RECEIVED 是不可覆盖的运输终态。

数据库保留不可变 attempts/results；延迟约束要求当前租约及明确终态/待验证状态的确认记录完整。权限源的 epoch 防止同事务撤销再恢复绕过最终重核。网络始终位于数据库事务之外；后台 worker 每五秒按准确租户作用域寻找到期工作，仅在需要处理的状态发生变化时记录告警。

## 边界

FactAck 证明接收方保存了什么；其 application 字段是接收方报告，不能代替实际 ApplicationReceipt 原件或业务变化。本轮不执行物理报废，不解除工单/借用/库存义务，不把 RECEIVED 改称 APPLIED。实际业务适用性与应用回执共同事务、回执链/摄取、GAP 恢复、当前业务页面及原七天其他出口继续。公开管理重放和业务应用重放不能从服务运输权限推导。
