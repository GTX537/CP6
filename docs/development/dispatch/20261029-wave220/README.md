# W220 原生退货申请与授权分离

COORD单人；预计45–75分钟，固定DEV-1/DEV-2复用。

Native outbound-backed return applications: immutable draft/submitted versions, current RMA authority, exact quantity, atomic original-operation recovery; no authorization or stock/finance effects.

Versioned native application contract, not formal RMA-C01 or full OriginalFulfilment/entitlement adoption. Existing RMA effect commands remain fenced.

直接采用现有OperationsOwner权限/事务和Main原操作日志。只在请求边界校验输入，在业务决定点核对来源与版本；内部不加资格证明层、序列化复制或重复校验。定向PostgreSQL/HTTP行为验证和一次任务审查，正常PR合并、准确分支清理、finish释放。

实际来源为已发货原生OutboundOrder/Detail，不伪装正式ShipmentLine/InvoiceLine/Schedule/range。接口独立版本化，正式RMA合同保留。

## 交付快照

[报告](../../evidence/W220-01/DELIVERY.md)。16项定向验证通过；Handoff已合并释放，Main待PR/finish。当前登记16.9分钟。
