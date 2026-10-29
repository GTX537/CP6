# W222 退货实际订单行来源贯通

COORD单人；预计45–75分钟，固定DEV-1/DEV-2复用。

Preserve exact order-line IDs and immutable quantity/unit generation snapshots in actual outbound creation; retain source identity across edits and expose it through authorized return sources without SKU inference.

Native order-line lineage is a direct fulfilment prerequisite, not formal ShipmentLine/InvoiceLine/Schedule/UOM adoption or return authorization. Existing historical unlinked rows remain unlinked; no fabricated backfill.

直接在现有出库Owner事务保存真实订单行ID与生成时数量/单位快照。编辑保留来源身份；RMA已授权来源读取该事实，不按商品猜历史归属。PostgreSQL相关行为/原子回滚/历史兼容验证和一次集中审查。正常PR合并、准确分支清理和finish释放。

## 交付快照

[报告](../../evidence/W222-01/DELIVERY.md)。登记10.54分钟；Handoff已释放、Main待正常PR/finish。Issue645在本任务既有最新main分支内修复，不另建重复任务分支。

集中审查修正：出库页面删行保留原行号并显示真实行号；新增行号取现有最大值加一。增加相应页面回归，后端既有结果复用，不扩大全仓检查。
