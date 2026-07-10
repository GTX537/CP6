# WMS 权限键 → 锚定 MenuId 映射（M-WMS Task 2 产出）

> 生成于 2026-07-10。**本表是 T3 种 `Sys_RoleAction` 的唯一输入**：为某权限键授某 action，须写
> `Sys_RoleAction(RoleId, MenuId=下表锚定MenuId, ActionCode=action)`。运行时
> `PermissionAggregator.FillActionKeysAsync` 以 `Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null`
> 拼出 `"{MenuKey}:{ActionCode}"`，故 **RoleAction 必须挂在下表锚定 MenuId 上**（该行 MenuKey 已由 T2 显式设定）。
>
> 真相源：`docs/seeds/wms-permission-keys.md`（30 键）。接入正本：`CP6.WebApi/Seed/WmsMenuSeed.cs`（启动幂等）。
> 文档留档：`docs/seeds/wms-menu-seed.sql` §2.5（MenuKey 明示 UPDATE，与本表一致）。

## 锚定原则

- **一键一锚定行**：每个权限键恰由一个菜单行承载 `MenuKey`（join 在 MenuId 上取该行 key）。
- **多页共键锚定主功能页**：InboundOrder/OutboundOrder/StockTake 各有「一覧」+「登録/作業」两页，
  锚定到**主操作页**（登録/作業），一覧页不设 MenuKey（不承载权限）。
- **显式设定，不靠 RoutePath 自动派生**：8 个键的 RoutePath 派生值 ≠ 权威键
  （stocktake/qc-inspection/kitting/plate-mold/ink/mobile/iot/stock-qc），已在下表标注「※派生不符」。

## 锚定映射表（30 键全落定）

| # | 权限 menu_key | 锚定 MenuId | 菜单名 | RoutePath | 备注 |
|---|---|---|---|---|---|
| 1 | `wms-warehouse` | 401 | 倉庫マスタ | `/wms/warehouse` | |
| 2 | `wms-location` | 402 | ロケーション管理 | `/wms/location` | 库位端点寄居 WarehouseController |
| 3 | `wms-stock` | 403 | 在庫照会 | `/wms/stock` | |
| 4 | `wms-stock-qc` | **429** | 在庫QC(保留/放行) | `/wms/stock-qc` | **T2 新增菜单**（T1【菜单缺】补齐）；※派生符但为新行 |
| 5 | `wms-inbound-order` | 405 | 入庫予定 登録 | `/wms/inbound-order` | 主页；404 一覧不设 key |
| 6 | `wms-inbound-receipt` | 406 | 入庫実績 入力 | `/wms/inbound-receipt` | |
| 7 | `wms-outbound-order` | 408 | 出庫指示 登録 | `/wms/outbound-order` | 主页；407 一覧不设 key |
| 8 | `wms-stocktake` | 415 | 棚卸 作業 | `/wms/stock-take` | 主页；414 一覧不设 key；**※派生不符**（/wms/stock-take→wms-stock-take） |
| 9 | `wms-material-shortage` | 417 | 材料欠品管理 | `/wms/material-shortage` | |
| 10 | `wms-outbound-routing` | 419 | 出庫ルーティング | `/wms/outbound-routing` | |
| 11 | `wms-qc-inspection` | 421 | 入荷検品(QC) | `/wms/inspection` | **※派生不符**（/wms/inspection→wms-inspection） |
| 12 | `wms-slotting` | 422 | スロッティング最適化 | `/wms/slotting` | |
| 13 | `wms-replenish` | 423 | 補充指示 | `/wms/replenish` | |
| 14 | `wms-cross-dock` | 424 | クロスドッキング | `/wms/cross-dock` | |
| 15 | `wms-kitting` | 425 | キッティング・組立 | `/wms/kit` | **※派生不符**（/wms/kit→wms-kit） |
| 16 | `wms-rma` | 426 | 返品管理(RMA) | `/wms/rma` | |
| 17 | `wms-lot-trace` | 427 | ロット追溯・回収 | `/wms/lot-trace` | |
| 18 | `wms-expiry` | 428 | 賞味期限管理(FEFO) | `/wms/expiry` | |
| 19 | `wms-paper-roll` | 441 | 原紙ロール管理 | `/wms/paper-roll` | |
| 20 | `wms-remnant` | 442 | 残材・端材管理 | `/wms/remnant` | |
| 21 | `wms-plate-mold` | 443 | 印版・木型倉庫 | `/wms/plate-mold-stock` | **※派生不符**（→wms-plate-mold-stock） |
| 22 | `wms-ink` | 444 | インキ・接着剤管理 | `/wms/ink-lot` | **※派生不符**（→wms-ink-lot） |
| 23 | `wms-pallet` | 445 | パレット管理 | `/wms/pallet` | |
| 24 | `wms-vmi` | 446 | 客先預り在庫(VMI) | `/wms/vmi` | |
| 25 | `wms-sample-stock` | 447 | 試作・サンプル在庫 | `/wms/sample-stock` | |
| 26 | `wms-mobile` | 461 | モバイル作業指示 | `/wms/mobile-task` | **※派生不符**（→wms-mobile-task） |
| 27 | `wms-wcs-task` | 462 | WCS/自動倉庫連携 | `/wms/wcs-task` | |
| 28 | `wms-carrier` | 463 | 配送業者連携 | `/wms/carrier` | |
| 29 | `wms-iot` | 464 | IoT温湿度モニタ | `/wms/iot-monitor` | **※派生不符**（→wms-iot-monitor） |
| 30 | `wms-stock-dwell` | 483 | 在庫滞留レポート | `/wms/stock-dwell` | 仅 view（唯一端点为只读POST豁免） |

> **全部 30 键均已落定锚定 MenuId。** 其中 8 键（含 stock-qc 的兄弟场景）RoutePath 自动派生值 ≠ 权威键，
> 已由 `WmsMenuSeed` 显式设定并含防御矫正块（既有库历史派生键就地矫正）。

## 非锚定内容页（不承载权限，MenuKey 由 RoutePath 自动回填或留空）

| MenuId | 菜单名 | RoutePath | 说明 |
|---|---|---|---|
| 404 | 入庫予定 一覧 | `/wms/inbound-order-list` | 与 405 共域，仅浏览 |
| 407 | 出庫指示 一覧 | `/wms/outbound-order-list` | 与 408 共域 |
| 409 | 製品入庫 | `/wms/product-inbound` | 无独立写权限键 |
| 410 | 出荷指示 一覧 | `/wms/shipping-order-list` | Shipping 为纯 GET（T1 未列写端点） |
| 411 | 出荷指示 登録 | `/wms/shipping-order` | 同上 |
| 412 | ピッキング作業 | `/wms/picking` | |
| 413 | 梱包・出荷確定 | `/wms/packaging` | |
| 414 | 棚卸 一覧 | `/wms/stock-take-list` | 与 415 共域 |
| 416 | WMSダッシュボード | `/wms/dashboard` | 纯 GET |
| 481 | 帳票センター | `/wms/report-center` | 纯 GET |
| 482 | 連携ヘルス監視 | `/wms/bridge-health` | |
| 400/420/440/460/480 | 分组父节点 | (null) | 无 RoutePath，MenuKey 恒 null |
