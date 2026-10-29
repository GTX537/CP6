# 原商业退货来源 v1（W226 限定采用）

`Main.Erp.SalesReturnSource.v1` 保存真实发运时的原订单商业字段，供正式退货申请追溯。它是 WP04-34 的直接来源前置，不是可退资格、独立退货授权、贷项或退款决定；完整 WP04-32、WP04-34、WP10-22 与 D2 仍未完成。

## 写入与身份

既有 `ShipAsync` 在 `captureReturnApplicationSource=true` 时，按实际订单/出库明细 UUID 映射，捕获币种、汇率、订单与明细计价区分、原订单数量和单位、计价单位、设定/个别单价、原行金额、特殊/暂定价格标记。相同 SKU 的不同原订单行不合并；金额、数量保留实际十进制值和 null，不重算金额，不把订单数量或已发数量解释为可退额度。

商业快照与库存、包裹、发运快照在同一 PostgreSQL Owner 事务提交。独立表 `cp6_sales_return_source_v1.snapshots` 按租户、出库 UUID、实际发运行 UUID 保存不可变原始字节和引用；版本及摘要只在写入时生成，内部读取信任存储合同。`OriginalFulfilment.commercialSnapshotRef` 指向已保存记录。普通发运或仅 `captureReturnSource=true` 保持原有行为，历史缺失不会按当前价格补写。

价格正文不进入既有公开发运 `Facts`，避免只有 WMS 来源读取权限的调用者通过该接口获得订单金额。该表只在本地隔离数据库验证迁移；本轮不迁移既有环境。

## 查询

`GET /api/erp/sales-return-sources/v1/{outboundId}/{shipmentLineId}` 返回既有 `{code:0,data:...}` 信封，禁止缓存。需要当前 RMA query、出库对象/仓库范围，以及当前原订单头与原明细对象范围、`UnitPrice` 和 `Amount` 读取权限。使用既有 Owner 权限与业务事务锁；原订单取消或软删除本身不抹去历史读取资格，仍检查当下权限。权限失败返回 403，未捕获返回 `SALES_RETURN_SOURCE_NOT_CAPTURED` /404。

`data.reference` 是商业快照引用；`original` 保留发运/订单引用、精确明细 UUID、原订单号/行号、物料、客户身份、捕获时间与 `terms`；`current` 单列当前原记录的客户/物料、删除与取消状态、当前 `terms`。`currentState` 仅比较已列出的商业字段：`TERMS_UNCHANGED`、`TERMS_CHANGED`，或物理删除原明细后的 `LINE_MISSING`（current=null）。该状态不代表来源全貌一致或资格有效。物理缺失明细只有按当前订单头能证明部门/全部范围时才允许读取；“仅本人”范围缺少原明细创建者依据时返回403。原订单头已不存在或不在当前范围时同样拒绝查询，不能绕过当前对象授权展示旧价格。

所有 decimal 均为精确文本；null 表示原字段未记录，零值仍为 `"0"`。数据不包含未实现的政策、发票、交期、签收证明或可退余额，不伪造其引用和版本。

## 页面与后续

正式 `/wms/rma-applications` 详情按实际发运行提供“查看原商业记录”，并列显示原值/当前值和明确缺失值。历史未捕获显示“发运时未保存”。身份或申请上下文变化清空旧值；读取被撤权或过期请求返回时不恢复旧身份数据。

本轮保留 ERP 来源、Main WMS 独立退货决定和 E/A/P 占用的既有边界。后续须接入实际商业退货依据与独立授权，再由正式 IN 消费；不另建 ERP 平行退货审批账，也不以来源快照关闭完整资格验收。[实施窗口](dispatch/20261029-wave226/README.md)。
