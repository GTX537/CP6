# ERP 权限键 → 锚定 MenuId 映射（M-ERP Task 2 产出）

> 生成于 2026-07-12。**本表是 T3b 种 `Sys_RoleAction` 的唯一输入**：为某权限键授某 action，须写
> `Sys_RoleAction(RoleId, MenuId=下表锚定MenuId, ActionCode=action)`。运行时
> `PermissionAggregator.FillActionKeysAsync` 以 `Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null`
> 拼出 `"{MenuKey}:{ActionCode}"`，故 **RoleAction 必须挂在下表锚定 MenuId 上**（该行 MenuKey 已由 T2 显式设定）。
>
> 真相源：`docs/seeds/erp-permission-keys.md`（14 键 / §二）。接入正本：`CP6.WebApi/Seed/ErpMenuSeed.cs`（启动幂等，
> 置于 Program.cs RoutePath 回填块之前）。文档对照：`docs/seeds/erp-menu-seed.sql`（与本表 + C# 正本一致）。

## 锚定原则

- **一键一锚定行**：每个权限键恰由一个菜单行承载 `MenuKey`（join 在 MenuId 上取该行 key）。
- **一域两页共键只锚定「登録/主操作页」**：estimate-calc / quotation / product / order / business-partner /
  plate-mold 各有「一覧」+「登録」两页，**只锚定登録页**，一覧页 MenuKey 留 null（不承载权限、无 RoleAction 引用）。
  - **硬约束**：`Sys_Menus.MenuKey` 有 `IS NOT NULL` 过滤唯一索引
    （`CP6ContextModelSnapshot`: `HasIndex("MenuKey").IsUnique().HasFilter("[MenuKey] IS NOT NULL")`），
    **两行共赋同一非空 MenuKey 会撞唯一键**。故必须一键一锚定行（对齐 WMS：405 登録锚定 `wms-inbound-order`、
    404 一覧留 null）。⚠ 真相源 §六.2「两行同赋同一 key」的建议已被此唯一索引否决，本表以单锚定行落定。
- **显式设定，不靠 RoutePath 自动派生**：既有 201–215 的 RoutePath 为**裸路径**（`/order`、`/product`…无 `erp/` 段），
  自动派生得 `order`/`product`… 无 `erp-` 前缀，与权威键失配。故 9 个既有键均由 `ErpMenuSeed` 显式设 `erp-*`
  并含防御矫正块（历史被回填写坏就地纠回）。5 个孤儿键 RoutePath 为 `/erp/*`，派生虽已符 `erp-*`，仍显式锚定。

## 锚定映射表（14 键全落定）

| # | 权限 menu_key | 锚定 MenuId | 菜单名 | RoutePath | 备注 |
|---|---|---|---|---|---|
| 1 | `erp-estimate-calc` | 202 | 見積計算書 登録 | `/estimate-calc` | 主页；201 照会一覧不设 key |
| 2 | `erp-quotation` | 204 | 御見積書 登録 | `/quotation` | 主页；203 一覧不设 key |
| 3 | `erp-product` | 206 | 製品マスタ 登録 | `/product` | 主页；205 一覧不设 key |
| 4 | `erp-order` | 208 | 受注入力 | `/order` | 主页；207 受注一覧照会不设 key。含 UnshippedOrder 只读子视图（view） |
| 5 | `erp-order-price-correction` | 209 | 単価訂正 | `/order-price-correction` | 独立菜单行。**跨菜单**：OrderController.BatchUpdatePrice=correct 高危键归此，非 erp-order |
| 6 | `erp-fsc-checklist` | 210 | FSC チェックシート | `/fsc-checklist` | 独立行 |
| 7 | `erp-business-partner` | 212 | 取引先マスタ 登録 | `/business-partner` | 主页；211 一覧不设 key |
| 8 | `erp-sheet-unit-price` | 213 | シート単価マスタ | `/sheet-unit-price` | 独立行 |
| 9 | `erp-plate-mold` | 215 | 版型/木型 登録 | `/plate-mold` | 主页；214 一覧不设 key |
| 10 | `erp-order-trace` | **216** | 受注トレース | `/erp/order-trace` | **T2 孤儿收编新增**。仅 view（GET-only 控制器） |
| 11 | `erp-credit-note` | **217** | クレジットノート照会 | `/erp/credit-note` | **T2 孤儿收编新增**。仅 view（唯一端点为只读 POST 豁免） |
| 12 | `erp-backorder` | **218** | 欠品・残数管理 | `/erp/backorder` | **T2 孤儿收编新增**。含 close/split 两状态键 |
| 13 | `erp-otd-report` | **219** | OTD納期遵守レポート | `/erp/otd-report` | **T2 孤儿收编新增**。仅 view（两端点均只读 POST 豁免） |
| 14 | `erp-fx-rate` | **220** | 為替レートマスタ | `/erp/fx-rate` | **T2 孤儿收编新增**。含 add/edit/del |

> **全部 14 键均已落定锚定 MenuId。** 9 键锚定既有 202–215，5 键锚定新增 216–220（孤儿路由收编）。

## 非锚定内容页（不承载权限，MenuKey 由 RoutePath 自动回填或留空）

| MenuId | 菜单名 | RoutePath | 说明 |
|---|---|---|---|
| 200 | 販売管理(ERP) | (null) | 分组父节点，MenuKey 恒 null |
| 201 | 見積計算書 照会 | `/estimate-calc-list` | 与 202 共域，仅浏览（view 权限走 202 的 erp-estimate-calc） |
| 203 | 御見積書 一覧 | `/quotation-list` | 与 204 共域 |
| 205 | 製品マスタ 一覧 | `/product-list` | 与 206 共域 |
| 207 | 受注一覧照会 | `/order-list` | 与 208 共域 |
| 211 | 取引先マスタ 一覧 | `/business-partner-list` | 与 212 共域 |
| 214 | 版型/木型 一覧 | `/plate-mold-list` | 与 215 共域 |

## MenuId 取号依据

- ERP 现用 **200–215**（Program.cs 既有种子）。段位规约：100–199 系统 / 200–299 販売(ERP) / 300–399 MES /
  400–499 WMS / 600–699 财务 / 700–704 采购 / 900 段 Space。
- 5 孤儿行取 **216–220**：经全仓 `grep "MenuId = 21[6-9]|22[0-9]"` 扫描**无任何占用**（WMS backlog 波次拣货占的是
  418/429，在 400 段，不影响 200 段）。故取 215 之后最近连续段 216–220，OrderNo 同值使其紧随既有 ERP 菜单显示。
