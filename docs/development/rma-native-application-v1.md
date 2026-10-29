# 原生退货申请入口 v1

`RmaApplicationOwnerV1` 提供基于现有出库单与明细的退货意向申请。创建得到 `DRAFT`，提交得到新的不可变 `SUBMITTED` 版本；两者均没有退货授权、数量权益占用、收货、库存移动或财务效果。旧 RMA 命令仍保留原有未就绪错误。

这是独立的 `RMA-NATIVE-APPLICATION/1` 合同，HTTP 根路径为 `/api/wms/rma/native-applications-v1`，不是正式 `/api/wms/rma/v1` 的 RMA-C01/C02。原生出库行 ID、单位编码和发货交易号保持其真实含义，不包装成正式 ShipmentLine、InvoiceLine、Schedule、版本化 UOM 或可授权范围。正式合同没有被改写、缩小或宣布验收；后续采用需有明确的来源映射。当前只有服务/API 入口，前端申请页面尚未接入。

| 请求 | 行为 |
|---|---|
| `GET sources/{outboundId}` | 在当前 RMA 权限范围内读取已完成客户出库单及其真实明细、数量、单位、批次、库位、发货交易号和来源版本 |
| `POST /` | 保存申请草稿；请求含 operationId、applicationId、outboundId、sourceVersion、reason、requestedOutcome、lines、remarks；明细含 outboundLineId、quantity |
| `GET /?page=1` | 按当前权限过滤后分页，50 条/页 |
| `GET {applicationId}?version=1` | 当前版本或指定不可变历史版本，返回强 ETag |
| `POST {applicationId}/submit` | 请求含 operationId、expectedVersion；强 `If-Match` 必需；原子追加 SUBMITTED 版本并更新 Head |
| `GET operations/{operationId}` | 当前查询权限下恢复原结果，未查到返回 UNKNOWN |
| `POST operations/{operationId}/close` | 关闭确认尚未提交的原操作，之后同一操作不能执行；已经提交的操作返回原结果 |

原操作 ID 和申请 ID 使用 UUID。新增/提交使用现有 `wms-rma:add`，读取和已提交原结果恢复使用 `wms-rma:query`，关闭未提交原操作使用 `wms-rma:cancel`。提交只是申请意向，不是独立审批；当前没有将 `add` 解释为正式 `authorization.decide`。

来源范围直接复用 `OperationsAccessV1.Outbounds()` 在 RMA 当前数据范围下的部门/本人及仓库、区域约束；本人范围还检查申请创建者。查询不隐式扩权至其他租户或其他出库来源。ReturnReason、Remarks、OperatorCd 字段读取权限分别控制申请原因、备注、申请人显示，写入按原因/备注字段权限执行。

数量按十进制字符串输入/输出，支持完整 `numeric(21,8)` 正数范围，无浮点转换或精度截断。每行不得超过该真实发货行数量；申请本身不占用授权权益，因此不在不同申请间虚构权益扣减。客户、产品、单位、仓库、批次等从来源读取，不接受浏览器覆写。来源缺失单位/客户/发货信息立即明确失败，不补默认值。

采用既有 OperationsOwner 当前权限与事务、业务表锁、提交时撤权检查和 Main 原操作日志。请求入口校验一次；内部不加资格登记、签名包装、序列化复制或日志内容逐字段复验。来源版本是调用者选择的来源快照并发校验，提交强 ETag 是申请 Head 的并发校验。已有内部日志结果直接反序列化使用，原操作输入冲突保持明确 409。

Head、不可变申请版本、原操作结果及审计在同一 PostgreSQL 事务提交。并发提交只有一个版本获胜，失败事务不留下版本或日志。提交回应丢失仍返回原有 UNKNOWN 语义，通过同一原操作查询/重放恢复，不能自行换操作 ID 重做。

迁移 `20261030022000_RmaApplicationV1` 只创建新 schema 与两张表、不可变版本保护和 Head 外键，不修改原生业务数据；仅在隔离本地 PostgreSQL 验证，未应用于既有环境。正式部署仍按独立环境流程进行。
