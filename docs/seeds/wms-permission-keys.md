# WMS 写端点 × 权限键清单（M-WMS Task 1 真相源）

> 生成于 2026-07-10。本表是 M-WMS 横切接线波的**唯一真相源**：T2（`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子）与 T3（逐端点贴 `[RequirePermission("menu_key","action")]`）均以本表为准。
> 依据：`docs/00-横切接线规范.md` 第一章（功能级四粒度）+ 样板 `CP6.WebApi/Controllers/Space/LocationPublishController.cs` + 现有 `docs/seeds/wms-menu-seed.sql`（48 条菜单，**均缺 MenuKey**——这正是本波要补的）。
> **本任务只产出本文档，不改任何控制器/种子/测试代码。**

## 约定

- **资源键 = `menu_key:action`**。MenuKey 下划线小写，尽量对齐 `wms-menu-seed.sql` 已有菜单结构/RoutePath。
- **`高危?` 列三值**：
  - `是` = 触及**库存/金额/不可逆**（T3 与审计 T2 的**最高优先级**贴点，绝不可与 view/edit 混授）。
  - `状态` = 独立的工作流状态流转（不直接动库存/钱，但仍**单独成键**，不塞进 edit/view）。
  - `否` = 四基粒度 `view/add/edit/del` 之一。
- **只读 POST 豁免**：纯查询/报表的 POST 归 `view`，在表内标 `只读POST→view` 并在末尾清单逐条附理由。
- 覆盖 `CP6.WebApi/Controllers/Wms/` 下**全部 29 个含写端点的控制器**（另 3 个 `WmsDashboard`/`ReportCenter`/`Shipping` 为纯 GET，无写端点，不在本表）。

---

## 一、写端点映射表

| 控制器 | HTTP方法 + 路由 | 方法名 | 建议 MenuKey | action | 高危? | 备注 |
|---|---|---|---|---|---|---|
| WarehouseController | POST `/api/wms/warehouse` | Create | `wms-warehouse` | add | 否 | 菜单401 倉庫マスタ |
| WarehouseController | PUT `/api/wms/warehouse/{cd}` | Update | `wms-warehouse` | edit | 否 | |
| WarehouseController | DELETE `/api/wms/warehouse/{cd}` | Delete | `wms-warehouse` | del | 否 | |
| WarehouseController | POST `/api/wms/warehouse/location` | CreateLocation | `wms-location` | add | 否 | **同控制器跨菜单**：库位归菜单402 ロケーション管理 |
| WarehouseController | PUT `/api/wms/warehouse/location/{cd}` | UpdateLocation | `wms-location` | edit | 否 | |
| WarehouseController | DELETE `/api/wms/warehouse/location/{cd}` | DeleteLocation | `wms-location` | del | 否 | |
| StockController | POST `/api/wms/stock/apply` | Apply | `wms-stock` | adjust | 是 | 通用库存变动/调整，直接改在庫数量 |
| StockController | POST `/api/wms/stock/move` | Move | `wms-stock` | move | 是 | 棚移动，双伝票库存转移 |
| StockQcController | POST `/api/wms/stock-qc/{stockId}/set` | SetSingle | `wms-stock-qc` | set | 是 | 改单件库存QC状态（放行/保留），影响可用量。**【菜单缺】** |
| StockQcController | POST `/api/wms/stock-qc/by-work-order/{workOrderNo}` | MarkByWO | `wms-stock-qc` | set | 是 | 按工单批量改QC状态。**【菜单缺】** |
| InboundOrderController | POST `/api/wms/inbound-order` | Create | `wms-inbound-order` | add | 否 | 菜单404/405 入庫予定 |
| InboundOrderController | PUT `/api/wms/inbound-order/{no}` | Update | `wms-inbound-order` | edit | 否 | |
| InboundOrderController | DELETE `/api/wms/inbound-order/{no}` | Delete | `wms-inbound-order` | del | 否 | |
| InboundOrderController | POST `/api/wms/inbound-order/{no}/confirm` | Confirm | `wms-inbound-order` | confirm | 是 | 确定入库预定→驱动下游收货 |
| InboundOrderController | POST `/api/wms/inbound-order/{no}/cancel` | Cancel | `wms-inbound-order` | cancel | 是 | 取消已确定单，逆转预定 |
| InboundReceiptController | POST `/api/wms/inbound-receipt` | Confirm | `wms-inbound-receipt` | post | 是 | 入庫実績確定→过账入库、库存增加。菜单406 |
| OutboundOrderController | POST `/api/wms/outbound-order` | Create | `wms-outbound-order` | add | 否 | 菜单407/408 出庫指示 |
| OutboundOrderController | PUT `/api/wms/outbound-order/{no}` | Update | `wms-outbound-order` | edit | 否 | |
| OutboundOrderController | DELETE `/api/wms/outbound-order/{no}` | Delete | `wms-outbound-order` | del | 否 | |
| OutboundOrderController | POST `/api/wms/outbound-order/{no}/confirm` | Confirm | `wms-outbound-order` | confirm | 是 | 确定出库指示 |
| OutboundOrderController | POST `/api/wms/outbound-order/{no}/cancel` | Cancel | `wms-outbound-order` | cancel | 是 | 取消，释放已分配库存 |
| OutboundOrderController | POST `/api/wms/outbound-order/{no}/allocate` | Allocate | `wms-outbound-order` | allocate | 是 | 引当，预留/占用库存 |
| OutboundOrderController | POST `/api/wms/outbound-order/{no}/start-picking` | StartPicking | `wms-outbound-order` | pick | 状态 | 拣货开始（状态流转） |
| OutboundOrderController | POST `/api/wms/outbound-order/{no}/ship` | Ship | `wms-outbound-order` | ship | 是 | 出荷确定→库存出库 |
| OutboundOrderController | POST `/api/wms/outbound-order/from-work-order/{workOrderNo}` | FromWorkOrder | `wms-outbound-order` | add | 否 | 从工单生成出库单（创建） |
| OutboundOrderController | POST `/api/wms/outbound-order/from-order/{webOrderNo}` | FromOrder | `wms-outbound-order` | add | 否 | 从Web订单生成出库单（创建） |
| StockTakeController | POST `/api/wms/stock-take/plan` | CreatePlan | `wms-stocktake` | add | 否 | 菜单414/415 棚卸 |
| StockTakeController | POST `/api/wms/stock-take/{no}/start-count` | StartCount | `wms-stocktake` | count | 状态 | 开始盘点 |
| StockTakeController | PUT `/api/wms/stock-take/{no}/counts` | UpdateCounts | `wms-stocktake` | count | 状态 | 录入盘点数（盘点明细，非主库存） |
| StockTakeController | POST `/api/wms/stock-take/{no}/submit` | Submit | `wms-stocktake` | submit | 状态 | 提交待承認 |
| StockTakeController | POST `/api/wms/stock-take/{no}/approve` | Approve | `wms-stocktake` | approve | 是 | **承認即写入盘盈亏调整到主库存**（不可逆） |
| StockTakeController | POST `/api/wms/stock-take/{no}/cancel` | Cancel | `wms-stocktake` | cancel | 是 | 取消盘点 |
| MaterialShortageController | POST `/api/wms/material-shortage/{id}/resolve` | Resolve | `wms-material-shortage` | resolve | 状态 | 标记欠品已解决。菜单417 |
| MaterialShortageController | POST `/api/wms/material-shortage/{id}/dismiss` | Dismiss | `wms-material-shortage` | dismiss | 状态 | 忽略欠品告警 |
| OutboundRoutingController | POST `/api/wms/outbound-routing` | Create | `wms-outbound-routing` | add | 否 | 菜单419 出庫ルーティング（Preview 为GET，不在表） |
| OutboundRoutingController | PUT `/api/wms/outbound-routing/{id}` | Update | `wms-outbound-routing` | edit | 否 | |
| OutboundRoutingController | DELETE `/api/wms/outbound-routing/{id}` | Delete | `wms-outbound-routing` | del | 否 | |
| QcInspectionController | POST `/api/wms/qc-inspection/from-inbound/{inboundNo}` | CreateFromInbound | `wms-qc-inspection` | add | 否 | 菜单421 入荷検品 |
| QcInspectionController | POST `/api/wms/qc-inspection` | CreateDirect | `wms-qc-inspection` | add | 否 | |
| QcInspectionController | PUT `/api/wms/qc-inspection/{no}/items` | SaveItems | `wms-qc-inspection` | edit | 否 | 保存检验明细 |
| QcInspectionController | POST `/api/wms/qc-inspection/{no}/judge` | Judge | `wms-qc-inspection` | judge | 是 | 合否判定→影响库存QC/可用性 |
| QcInspectionController | POST `/api/wms/qc-inspection/{no}/cancel` | Cancel | `wms-qc-inspection` | cancel | 状态 | 取消检验（未过账前） |
| SlottingController | POST `/api/wms/slotting/analyze` | Analyze | `wms-slotting` | analyze | 状态 | 分析生成 SlottingPlan（仅建议，不动库存）。菜单422 |
| SlottingController | POST `/api/wms/slotting/{no}/approve` | Approve | `wms-slotting` | approve | 状态 | 批准优化方案（不直接移库） |
| SlottingController | POST `/api/wms/slotting/{no}/cancel` | Cancel | `wms-slotting` | cancel | 状态 | 取消方案 |
| ReplenishController | POST `/api/wms/replenish` | Create | `wms-replenish` | add | 否 | 菜单423 補充指示 |
| ReplenishController | POST `/api/wms/replenish/generate-batch` | GenerateBatch | `wms-replenish` | generate | 状态 | 批量生成补充指示 |
| ReplenishController | POST `/api/wms/replenish/{no}/execute` | Execute | `wms-replenish` | execute | 是 | 执行补充→库存移动 |
| ReplenishController | POST `/api/wms/replenish/{no}/cancel` | Cancel | `wms-replenish` | cancel | 状态 | 取消补充指示 |
| CrossDockController | POST `/api/wms/cross-dock` | Create | `wms-cross-dock` | add | 否 | 菜单424 クロスドッキング |
| CrossDockController | POST `/api/wms/cross-dock/{no}/execute` | Execute | `wms-cross-dock` | execute | 是 | 执行越库→库存直通移动 |
| CrossDockController | POST `/api/wms/cross-dock/{no}/cancel` | Cancel | `wms-cross-dock` | cancel | 状态 | 取消越库单 |
| KittingController | POST `/api/wms/kit/masters` | CreateMaster | `wms-kitting` | add | 否 | 菜单425 キッティング（套件主数据） |
| KittingController | PUT `/api/wms/kit/masters/{kitSku}` | UpdateMaster | `wms-kitting` | edit | 否 | |
| KittingController | DELETE `/api/wms/kit/masters/{kitSku}` | DeleteMaster | `wms-kitting` | del | 否 | |
| KittingController | POST `/api/wms/kit/orders` | CreateOrder | `wms-kitting` | add | 否 | 套件作业单（复用 add） |
| KittingController | POST `/api/wms/kit/orders/{no}/execute` | Execute | `wms-kitting` | execute | 是 | 组装执行→消耗组件、产出套件（库存转换） |
| KittingController | POST `/api/wms/kit/orders/{no}/cancel` | Cancel | `wms-kitting` | cancel | 状态 | 取消作业单 |
| RmaController | POST `/api/wms/rma` | Create | `wms-rma` | add | 否 | 菜单426 返品管理 |
| RmaController | POST `/api/wms/rma/{no}/receive` | Receive | `wms-rma` | receive | 是 | 退货收货→库存入库 |
| RmaController | POST `/api/wms/rma/{no}/start-inspection` | StartInspection | `wms-rma` | inspect | 状态 | 开始检验 |
| RmaController | POST `/api/wms/rma/{no}/judge` | Judge | `wms-rma` | judge | 是 | 退货处置判定（良品入库/报废/返修），不可逆 |
| RmaController | POST `/api/wms/rma/{no}/close` | Close | `wms-rma` | close | 状态 | 关闭 RMA |
| RmaController | POST `/api/wms/rma/{no}/cancel` | Cancel | `wms-rma` | cancel | 状态 | 取消 RMA |
| LotTraceController | POST `/api/wms/lot-trace/recall` | Recall | `wms-lot-trace` | recall | 是 | 设/撤召回标记→阻断出货，合规级。菜单427（Forward/Backward/Summary 为GET） |
| ExpiryController | POST `/api/wms/expiry/dispose` | Dispose | `wms-expiry` | dispose | 是 | 报废过期库存。菜单428（Expiring 为GET） |
| PaperRollController | POST `/api/wms/paper-roll` | Create | `wms-paper-roll` | add | 否 | 菜单441 原紙ロール（Match 为GET） |
| PaperRollController | POST `/api/wms/paper-roll/{no}/consume` | Consume | `wms-paper-roll` | consume | 是 | 消耗原纸卷库存 |
| PaperRollController | POST `/api/wms/paper-roll/slit` | Slit | `wms-paper-roll` | slit | 是 | 分切→原卷转子卷（库存转换） |
| PaperRollController | POST `/api/wms/paper-roll/{no}/dispose` | Dispose | `wms-paper-roll` | dispose | 是 | 报废原纸卷 |
| RemnantController | POST `/api/wms/remnant` | Create | `wms-remnant` | add | 否 | 菜单442 残材・端材（Match 为GET） |
| RemnantController | PUT `/api/wms/remnant/{no}` | Update | `wms-remnant` | edit | 否 | |
| RemnantController | POST `/api/wms/remnant/{no}/reserve` | Reserve | `wms-remnant` | reserve | 状态 | 预留残材（软预留） |
| RemnantController | POST `/api/wms/remnant/{no}/unreserve` | Unreserve | `wms-remnant` | reserve | 状态 | 撤销预留（复用 reserve 权限，管理预留） |
| RemnantController | POST `/api/wms/remnant/{no}/use` | MarkUsed | `wms-remnant` | use | 是 | 标记使用→残材库存出库 |
| RemnantController | POST `/api/wms/remnant/{no}/dispose` | Dispose | `wms-remnant` | dispose | 是 | 报废残材 |
| RemnantController | DELETE `/api/wms/remnant/{no}` | Delete | `wms-remnant` | del | 否 | |
| PlateMoldController | POST `/api/wms/plate-mold` | Create | `wms-plate-mold` | add | 否 | 菜单443 印版・木型（Warnings 为GET） |
| PlateMoldController | PUT `/api/wms/plate-mold/{no}` | Update | `wms-plate-mold` | edit | 否 | |
| PlateMoldController | POST `/api/wms/plate-mold/{no}/use` | RecordUsage | `wms-plate-mold` | use | 状态 | 记录使用次数（寿命计数，不动库存） |
| PlateMoldController | POST `/api/wms/plate-mold/{no}/maintenance/start` | StartMaintenance | `wms-plate-mold` | maintenance | 状态 | 保养开始 |
| PlateMoldController | POST `/api/wms/plate-mold/{no}/maintenance/complete` | CompleteMaintenance | `wms-plate-mold` | maintenance | 状态 | 保养完成 |
| PlateMoldController | POST `/api/wms/plate-mold/{no}/discard` | Discard | `wms-plate-mold` | dispose | 是 | 报废印版/木型（不可逆） |
| PlateMoldController | DELETE `/api/wms/plate-mold/{no}` | Delete | `wms-plate-mold` | del | 否 | |
| InkController | POST `/api/wms/ink/lots` | CreateLot | `wms-ink` | add | 否 | 菜单444 インキ・接着剤 |
| InkController | POST `/api/wms/ink/lots/{no}/open` | OpenLot | `wms-ink` | open | 状态 | 开封使用（状态流转） |
| InkController | POST `/api/wms/ink/lots/mix` | Mix | `wms-ink` | mix | 是 | 调墨→消耗多墨、产出新墨（库存转换） |
| InkController | POST `/api/wms/ink/matches` | RecordMatch | `wms-ink` | add | 否 | 登记调色配方（复用 add） |
| PalletController | POST `/api/wms/pallet` | Create | `wms-pallet` | add | 否 | 菜单445 パレット |
| PalletController | PUT `/api/wms/pallet/{no}` | Update | `wms-pallet` | edit | 否 | |
| PalletController | POST `/api/wms/pallet/{no}/complete-building` | CompleteBuilding | `wms-pallet` | complete | 状态 | 码盘完成（状态流转） |
| PalletController | POST `/api/wms/pallet/{no}/move-to-shipping` | MoveToShipping | `wms-pallet` | move | 是 | 托盘移至出货区（库存位置变动） |
| PalletController | POST `/api/wms/pallet/{no}/mark-shipped` | MarkShipped | `wms-pallet` | ship | 是 | 标记已出货→库存出库 |
| PalletController | DELETE `/api/wms/pallet/{no}` | Delete | `wms-pallet` | del | 否 | |
| VmiController | POST `/api/wms/vmi/billings/calculate` | Calculate | `wms-vmi` | calculate | 是 | 计算并 upsert 月度保管费（金额）。菜单446 |
| VmiController | POST `/api/wms/vmi/billings/{no}/confirm` | Confirm | `wms-vmi` | confirm | 是 | 确定账单→金额锁定 |
| SampleStockController | POST `/api/wms/sample-stock` | Create | `wms-sample-stock` | add | 否 | 菜单447 試作・サンプル（Overdue 为GET） |
| SampleStockController | PUT `/api/wms/sample-stock/{no}` | Update | `wms-sample-stock` | edit | 否 | |
| SampleStockController | POST `/api/wms/sample-stock/{no}/lend` | Lend | `wms-sample-stock` | lend | 是 | 样品借出→库存变动 |
| SampleStockController | POST `/api/wms/sample-stock/{no}/return` | Return | `wms-sample-stock` | return | 是 | 样品归还→库存变动 |
| SampleStockController | POST `/api/wms/sample-stock/{no}/expire` | Expire | `wms-sample-stock` | expire | 是 | 样品失效核销（不可逆） |
| SampleStockController | DELETE `/api/wms/sample-stock/{no}` | Delete | `wms-sample-stock` | del | 否 | |
| MobileController | POST `/api/wms/mobile/task` | Create | `wms-mobile` | add | 否 | 菜单461 モバイル作業指示 |
| MobileController | POST `/api/wms/mobile/task/{no}/start` | Start | `wms-mobile` | start | 状态 | 作业开始 |
| MobileController | POST `/api/wms/mobile/scan` | Scan | `wms-mobile` | scan | 状态 | 扫码记录（驱动作业进度） |
| MobileController | POST `/api/wms/mobile/task/{no}/done` | Done | `wms-mobile` | complete | 是 | 作业完成→库存操作过账 |
| MobileController | POST `/api/wms/mobile/task/{no}/cancel` | Cancel | `wms-mobile` | cancel | 状态 | 取消作业 |
| WcsTaskController | POST `/api/wms/wcs-task` | Create | `wms-wcs-task` | add | 否 | 菜单462 WCS/自動倉庫 |
| WcsTaskController | POST `/api/wms/wcs-task/{no}/dispatch` | Dispatch | `wms-wcs-task` | dispatch | 状态 | 下发任务给设备 |
| WcsTaskController | POST `/api/wms/wcs-task/{no}/start` | Start | `wms-wcs-task` | start | 状态 | 任务开始 |
| WcsTaskController | POST `/api/wms/wcs-task/{no}/complete` | Complete | `wms-wcs-task` | complete | 是 | 自动仓移动完成→库存变动 |
| WcsTaskController | POST `/api/wms/wcs-task/{no}/fail` | Fail | `wms-wcs-task` | fail | 状态 | 任务失败标记 |
| WcsTaskController | DELETE `/api/wms/wcs-task/{no}` | Delete | `wms-wcs-task` | del | 否 | |
| CarrierController | POST `/api/wms/carrier` | Create | `wms-carrier` | add | 否 | 菜单463 配送業者連携 |
| CarrierController | POST `/api/wms/carrier/{no}/event` | AddEvent | `wms-carrier` | event | 状态 | 追加物流跟踪事件 |
| CarrierController | POST `/api/wms/carrier/{no}/pickup` | PickUp | `wms-carrier` | event | 状态 | 集货状态（归并入 event） |
| CarrierController | POST `/api/wms/carrier/{no}/in-transit` | InTransit | `wms-carrier` | event | 状态 | 运输中状态（归并入 event） |
| CarrierController | POST `/api/wms/carrier/{no}/delivered` | Delivered | `wms-carrier` | event | 状态 | 已送达状态（归并入 event） |
| CarrierController | POST `/api/wms/carrier/{no}/fail` | Fail | `wms-carrier` | event | 状态 | 配送失败状态（归并入 event） |
| IotMonitorController | POST `/api/wms/iot/sensors` | CreateSensor | `wms-iot` | add | 否 | 菜单464 IoT温湿度モニタ |
| IotMonitorController | PUT `/api/wms/iot/sensors/{id}` | UpdateSensor | `wms-iot` | edit | 否 | |
| IotMonitorController | DELETE `/api/wms/iot/sensors/{id}` | DeleteSensor | `wms-iot` | del | 否 | |
| IotMonitorController | POST `/api/wms/iot/sensors/{id}/readings` | PostReading | `wms-iot` | ingest | 状态 | 传感器读数写入（设备数据摄取） |
| IotMonitorController | POST `/api/wms/iot/simulate` | Simulate | `wms-iot` | simulate | 状态 | 演示用模拟数据生成（写读数） |
| StockDwellController | POST `/api/wms/stock-dwell/summary` | Summary | `wms-stock-dwell` | view | 只读POST→view | **只读POST豁免**：纯查询 GetSummaryAsync，POST 仅为传复杂查询体。菜单483 |

---

## 二、MenuKey 汇总清单（去重，共 30 个）

| # | MenuKey | 对应现有菜单（wms-menu-seed.sql） | 说明 |
|---|---|---|---|
| 1 | `wms-warehouse` | 401 倉庫マスタ `/wms/warehouse` | ✅有菜单行（缺 MenuKey，待 T2 补） |
| 2 | `wms-location` | 402 ロケーション管理 `/wms/location` | ✅有菜单行；库位端点寄居 WarehouseController |
| 3 | `wms-stock` | 403 在庫照会 `/wms/stock` | ✅ |
| 4 | `wms-stock-qc` | —— | **【菜单缺】** StockQcController 无对应菜单行，待 T2 补种（建议归 在庫 段） |
| 5 | `wms-inbound-order` | 404/405 入庫予定 | ✅（一控制器对两菜单行：一覧+登録） |
| 6 | `wms-inbound-receipt` | 406 入庫実績 `/wms/inbound-receipt` | ✅ |
| 7 | `wms-outbound-order` | 407/408 出庫指示 | ✅ |
| 8 | `wms-stocktake` | 414/415 棚卸 `/wms/stock-take` | ✅ |
| 9 | `wms-material-shortage` | 417 材料欠品管理 `/wms/material-shortage` | ✅ |
| 10 | `wms-outbound-routing` | 419 出庫ルーティング `/wms/outbound-routing` | ✅ |
| 11 | `wms-qc-inspection` | 421 入荷検品(QC) `/wms/inspection` | ✅（路由 seed=`/wms/inspection`，控制器=`/api/wms/qc-inspection`，前端路由需对齐） |
| 12 | `wms-slotting` | 422 スロッティング `/wms/slotting` | ✅ |
| 13 | `wms-replenish` | 423 補充指示 `/wms/replenish` | ✅ |
| 14 | `wms-cross-dock` | 424 クロスドッキング `/wms/cross-dock` | ✅ |
| 15 | `wms-kitting` | 425 キッティング `/wms/kit` | ✅（route seed=`/wms/kit`） |
| 16 | `wms-rma` | 426 返品管理(RMA) `/wms/rma` | ✅ |
| 17 | `wms-lot-trace` | 427 ロット追溯・回収 `/wms/lot-trace` | ✅ |
| 18 | `wms-expiry` | 428 賞味期限管理 `/wms/expiry` | ✅ |
| 19 | `wms-paper-roll` | 441 原紙ロール `/wms/paper-roll` | ✅ |
| 20 | `wms-remnant` | 442 残材・端材 `/wms/remnant` | ✅ |
| 21 | `wms-plate-mold` | 443 印版・木型 `/wms/plate-mold-stock` | ✅（route seed=`/wms/plate-mold-stock`，控制器=`/api/wms/plate-mold`） |
| 22 | `wms-ink` | 444 インキ・接着剤 `/wms/ink-lot` | ✅（route seed=`/wms/ink-lot`，控制器=`/api/wms/ink`） |
| 23 | `wms-pallet` | 445 パレット `/wms/pallet` | ✅ |
| 24 | `wms-vmi` | 446 客先預り在庫(VMI) `/wms/vmi` | ✅ |
| 25 | `wms-sample-stock` | 447 試作・サンプル `/wms/sample-stock` | ✅ |
| 26 | `wms-mobile` | 461 モバイル作業指示 `/wms/mobile-task` | ✅（route seed=`/wms/mobile-task`，控制器=`/api/wms/mobile`） |
| 27 | `wms-wcs-task` | 462 WCS/自動倉庫 `/wms/wcs-task` | ✅ |
| 28 | `wms-carrier` | 463 配送業者連携 `/wms/carrier` | ✅ |
| 29 | `wms-iot` | 464 IoT温湿度モニタ `/wms/iot-monitor` | ✅（route seed=`/wms/iot-monitor`，控制器=`/api/wms/iot`） |
| 30 | `wms-stock-dwell` | 483 在庫滞留レポート `/wms/stock-dwell` | ✅ 仅 view（唯一端点为只读POST豁免） |

> 29 个键有对应菜单行（缺 MenuKey，T2 统一补），**1 个键菜单缺**：`wms-stock-qc`。
> **路由不一致提醒（供 T2/前端）**：`wms-menu-seed.sql` 中若干 RoutePath 与控制器路由前缀不一致（qc-inspection/plate-mold/ink/mobile/iot），本表以控制器实际路由为准，MenuKey 命名取业务域名，T2 播种与前端 `v-permission` 需逐字对齐本表 MenuKey。

---

## 三、高危动作清单（真高危：库存/金额/不可逆，共 36 个资源键）

> 这些是 T3 贴 `[RequirePermission]` 与审计 T2（钱与库存优先）的**第一优先级**。每个都**绝不可**与 view/edit 混授。

| 资源键 | 为何高危独立 |
|---|---|
| `wms-stock:adjust` | 通用库存变动，直接增减在庫数量 |
| `wms-stock:move` | 棚移动，双伝票库存转移 |
| `wms-stock-qc:set` | 改库存QC状态（放行/保留），改变可用库存 |
| `wms-inbound-order:confirm` | 确定入库预定，驱动下游收货 |
| `wms-inbound-order:cancel` | 取消已确定单，逆转预定 |
| `wms-inbound-receipt:post` | 入库实绩过账，库存增加 |
| `wms-outbound-order:confirm` | 确定出库指示 |
| `wms-outbound-order:cancel` | 取消并释放已分配库存 |
| `wms-outbound-order:allocate` | 引当，预留/占用库存 |
| `wms-outbound-order:ship` | 出荷确定，库存出库 |
| `wms-stocktake:approve` | 承認即写盘盈亏调整入主库存，不可逆 |
| `wms-stocktake:cancel` | 取消盘点作业 |
| `wms-qc-inspection:judge` | 合否判定，影响库存QC/可用性 |
| `wms-replenish:execute` | 执行补充，库存移动 |
| `wms-cross-dock:execute` | 越库直通，库存移动 |
| `wms-kitting:execute` | 组装消耗组件/产出套件，库存转换 |
| `wms-rma:receive` | 退货收货，库存入库 |
| `wms-rma:judge` | 退货处置判定（入库/报废/返修），不可逆 |
| `wms-lot-trace:recall` | 召回标记，阻断出货，合规级 |
| `wms-expiry:dispose` | 报废过期库存 |
| `wms-paper-roll:consume` | 消耗原纸卷库存 |
| `wms-paper-roll:slit` | 分切，原卷→子卷库存转换 |
| `wms-paper-roll:dispose` | 报废原纸卷 |
| `wms-remnant:use` | 标记使用，残材出库 |
| `wms-remnant:dispose` | 报废残材 |
| `wms-plate-mold:dispose` | 报废印版/木型（method=Discard，归并 dispose），不可逆 |
| `wms-ink:mix` | 调墨，消耗多墨/产出新墨，库存转换 |
| `wms-pallet:move` | 托盘移至出货区，库存位置变动 |
| `wms-pallet:ship` | 托盘标记已出货，库存出库 |
| `wms-vmi:calculate` | 计算并 upsert 月度保管费（金额） |
| `wms-vmi:confirm` | 确定账单，金额锁定 |
| `wms-sample-stock:lend` | 样品借出，库存变动 |
| `wms-sample-stock:return` | 样品归还，库存变动 |
| `wms-sample-stock:expire` | 样品失效核销，不可逆 |
| `wms-mobile:complete` | 移动端作业完成，库存操作过账 |
| `wms-wcs-task:complete` | 自动仓移动完成，库存变动 |

### 3b. 独立但低危的状态流转动作键（`状态`，共 30 个，仍单独成键、不塞 edit）

`wms-outbound-order:pick` · `wms-stocktake:count` · `wms-stocktake:submit` · `wms-material-shortage:resolve` · `wms-material-shortage:dismiss` · `wms-slotting:analyze` · `wms-slotting:approve` · `wms-slotting:cancel` · `wms-replenish:generate` · `wms-replenish:cancel` · `wms-cross-dock:cancel` · `wms-kitting:cancel` · `wms-rma:inspect` · `wms-rma:close` · `wms-rma:cancel` · `wms-qc-inspection:cancel` · `wms-mobile:start` · `wms-mobile:scan` · `wms-mobile:cancel` · `wms-wcs-task:dispatch` · `wms-wcs-task:start` · `wms-wcs-task:fail` · `wms-carrier:event` · `wms-pallet:complete` · `wms-plate-mold:use` · `wms-plate-mold:maintenance` · `wms-ink:open` · `wms-remnant:reserve` · `wms-iot:ingest` · `wms-iot:simulate`

> 非CRUD独立动作键合计 = 36（真高危） + 30（状态流转） = **66 个**。

---

## 四、只读 POST 豁免清单（归 view，共 1 个）

| 端点 | 豁免理由 |
|---|---|
| POST `/api/wms/stock-dwell/summary`（StockDwellController.Summary） | 纯查询：仅调 `IStockDwellService.GetSummaryAsync`，无任何写副作用；用 POST 只为传递复杂查询体 `StockDwellQuery`。归 `wms-stock-dwell:view`。 |

> 复核结论：其余看似"查询/分析"的 POST 均有写副作用，**不**豁免——
> `slotting/analyze`（生成 SlottingPlan 记录）、`vmi/billings/calculate`（upsert 账单）、`lot-trace/recall`（写召回标记）、`iot/simulate` 与 `iot/.../readings`（写读数）均为写端点，已按上表贴权限。

---

## 五、命名归并判断与疑点（供 T2/T3 复核）

1. **一控制器跨两 MenuKey**：`WarehouseController` 同时承载倉庫（`wms-warehouse`）与库位（`wms-location`，即 CreateLocation/UpdateLocation/DeleteLocation）两个菜单域。T3 贴点须按端点分别贴不同 menu_key，勿一刀切。
2. **`wms-stock-qc` 菜单缺**：StockQcController（`/api/wms/stock-qc`）无对应 Sys_Menu 行。建议 T2 在"在庫"段（400 区）补一条菜单，MenuKey=`wms-stock-qc`。其两端点均高危（改库存QC状态）。
3. **动作动词归并（有意为之）**：
   - `wms-carrier:event` 归并了 AddEvent/PickUp/InTransit/Delivered/Fail 五个物流跟踪状态端点为**一个** action——它们都只写配送跟踪状态、不动库存，同权限管理合理。
   - `wms-remnant:reserve` 归并 Reserve+Unreserve（预留的加/减为同一管理权限）。
   - `wms-plate-mold:maintenance` 归并 保养 start/complete。
   - `dispose` 作为统一"报废/廃棄"动词，覆盖 Expiry/PaperRoll/Remnant 的 Dispose 与 PlateMold 的 **Discard**（方法名不同，语义同=报废）。
4. **`dispose` 一词两义提醒**：本表中 `dispose` 统一指**报废/廃棄**；简报示例里"dispose（RMA 处置）"的 RMA 处置判定本表用的是 `wms-rma:judge`（更贴合 method=Judge 与"判定"语义），二者不冲突（不同 menu_key）。若 T3 偏好与简报字面一致，可将 `wms-rma:judge` 改名 `wms-rma:dispose`——**待拍板**。
5. **`add` 复用于同域多创建端点**：`wms-ink:add`（CreateLot + RecordMatch）、`wms-kitting:add`（CreateMaster + CreateOrder）、`wms-outbound-order:add`（Create + FromWorkOrder + FromOrder）、`wms-qc-inspection:add`（CreateFromInbound + CreateDirect）——同一"创建"粒度复用 add，不为每个创建端点造新键。若某些需要更细授权（如"仅可从工单生成、不可手工建"），T3 可再拆——**当前判定为不拆**。
6. **`状态` 档动作的高危再评估**：`wms-mobile:scan`/`wms-mobile:complete`、`wms-wcs-task:complete` 位于移动端/自动仓这条**能实际驱动库存**的链路上。本表已将 complete/done 提级为真高危`是`；scan 暂列`状态`。若审计要求移动端全链路从严，可将 scan 一并提级——**待 T2 审计拍板**。
7. **路由 vs 菜单 RoutePath 不一致**（qc-inspection / plate-mold / ink / mobile / iot 五处，详见 §二注）：本波不改控制器路由，但 T2 播种菜单与前端 `v-permission` 必须以本表 MenuKey 为唯一基准逐字对齐，避免键名分叉。
