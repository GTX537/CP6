# 退货额度与独立授权 v1

W229 在正式退货申请上加入额度查询、独立批准/拒绝、撤销/到期关闭、不可变批准份额与原命令恢复。业务代码在 PostgreSQL 和实际 HTTP 宿主中执行。按用户 2026-10-29 决定，模拟业务数据及外部服务可用于生产验收，不以缺少真实企业数据阻塞本目标。

## 原始坐标与数量

沿用 WMS-RMA-01 的 SourceCoordinate、ReturnEntitlement、AuthorizationDecision、RangeClaim、ReturnShare 语义。唯一占量坐标是来源 Owner、原发运行 Owner/id、发票行 Owner/id 或 NONE、排程 Owner/id 或 NONE、基础单位 code/version。RMA、批准决定、来源切片版本和原引用 digest 不参与占量坐标，因此重新申请和来源升级不会清空同坐标占用。

所有数量使用最多 13 位整数和 8 位小数的十进制字符串；SQL 区间为 numeric(21,8)，浏览器用 BigInt 定点计算。范围为左闭右开，允许相邻、拒绝重叠和零宽度。批准范围必须同时属于申请行与来源允许范围，且不与当前占用相交。剩余额度是来源允许区间减去有效占用的测度；已收货和已贷记独立展示，不再重复扣减。来源剩余额度可能大于本申请可选额度，后者还受申请行范围限制。

来源、当前政策或占用变化推进额度版本。浏览器提交精确申请头、额度头和额度快照引用；同一原坐标的两次并发批准至多有一个采用旧额度头。采用现有 RMA 写入事务锁，无另造分布式资格协议。

## 明确的模拟来源

`IRmaReturnSourcesV1` 只提供业务事实，不批准退货，不写占用。现有 `RmaSimulatedReturnSourcesV1` 用配置段 `RmaReturnSimulation`：`Enabled=true`，`CasesJson` 为 `RmaReturnSimulationCaseV1[]` 的 JSON。

每条场景明确提供：tenantId、实际保存的 shipmentRef/shipmentLineId、cutVersion、deliveredAt、模拟 deliveryProofRef、可空 invoiceLineRef/scheduleRef、eligibleRanges、historicalOccupiedRanges、historicalReceivedRanges、creditedRanges。后三组即使为空也必须显式给出。收货及贷记区间必须属于占用，占用必须属于来源允许区间；允许区间必须属于实际原发运行。构造时校验场景输入，内部计算直接使用类型化十进制区间。

`Simulation.Wms.ReturnSourceCut.v1` 引用和 `evidenceMode=SIMULATED` 标记来源，页面展示模拟标识和批准份额的签收来源 Owner。模拟签收/发票引用不会修改原生保存的发运快照。缺失场景不会猜测历史为零；由执行对话补齐完整场景，不等待真实数据。可复现完整场景见 `RmaAuthorizationPostgreSqlTests.ReturnApprovalSetup`；实际 HTTP 测试用同一 `CasesJson` 启动真实 Program。

W228 签收证据登记仍是证据收集。本轮模拟切片可提供验收所需签收/发票/历史事实，不将证据登记本身冒认为正式累计签收核算。

## 决定、撤销及权限

仅 SUBMITTED 申请可生成决定。当前 `authorization-decide` / `authorization-revoke` 动作、租户、仓库范围、字段权限、申请版本、当前政策和独立审批规则在现有 Owner 事务中执行；迁移登记动作，不自动授予角色。批准要求政策启用、原因及期望处理在政策允许集合、签收后申请期限符合政策。政策要求独立审批时，申请人不能代替另一审批人决定。

决定版本、批准份额、原始申领、命令结果和待发事件在同一事务保存。授权不变更申请为其他状态，也不写库存、退款或贷项；已批准时申请不能直接撤回，先关闭授权。拒绝无需读取额度来源；当前授权上下文 `includeEntitlements=false` 同样支持无来源依赖的撤销。

撤销要求精确当前决定和全部当前份额引用。只释放 ACTIVE_RESERVED；CONSUMED_BY_RECEIPT 和 UNKNOWN_HOLD 保留。原始申领、份额、决定不覆盖，当前占用以 `return_claim_segments` 为准；消费方必须同时核对当前授权与当前段状态。已到期批准通过同一显式关闭命令生成 EXPIRED，否则生成 REVOKED。W229 不运行自动到期任务，时间经过不自动释放范围。

新的实物入库消费方需在相同事务锁下核对当前决定、生效区间和剩余批准范围，再写真实入库效果。此消费方尚待下一任务实现。本轮对已收货/未知段的关闭测试使用显式下游状态夹具，未宣称已完成实物入库实现。

## HTTP 与页面

- `GET /api/wms/rma/v1/applications/{id}/authorization/context?includeEntitlements=true|false`：当前政策、权限引用及可选额度。默认 false。
- `GET .../authorization?version=N`：当前/历史决定；尚无决定返回 `200` 和 JSON `null`。
- `POST .../authorization/decide`：闭合输入；APPROVED 必须含批准行、精确区间/数量、额度引用；REJECTED 不含份额/额度头。
- `POST .../authorization/revoke`：精确决定、完整份额引用、当前权限引用和原因。
- `POST /api/wms/rma/v1/authorization/commands/query`：按 applicationId/commandId 查询原结果，NOT_OBSERVED 不表示无效果。

沿用 correlation、no-store、幂等键、CSRF 和当前权限。原操作空间 `Main.Wms.RmaAuthorizationCommand.v1` 绑定租户、原操作者、申请、动作和冻结请求体；改体复用同键冲突。原结果读取只要求当前可见权限，撤销写权限后仍可恢复自己已接受的结果；来源/政策后续变化、到期不重写原结果。

实际 `RmaFormalApplicationsView` 接入 `RmaAuthorizationPanel`。先读取当前授权，明确加载额度后才批准；拒绝/撤销无需额度查询。恢复数据保存在私有 IndexedDB，身份下 Web Locks 排他，先查询原请求，明确点击才重派同键同体；未知、失败重派、切换申请/身份后的迟到响应不会丢弃原请求。

## 本轮范围与下一步

本轮验收额度、正常政策批准/拒绝/撤销、同坐标占用及页面恢复。非空 exceptionApprovalRef 的特批采用、正式实物入库、五类后续处置与商业/财务处理仍待实现；不将本轮关闭为完整 WP10-22。`authorization_outbox` 是同事务冻结事件，发送器/消费者尚未接入，不能声称已投递。仅应用于隔离验收数据库，部署状态另记。[交付报告](evidence/W229-01/DELIVERY.md)。
