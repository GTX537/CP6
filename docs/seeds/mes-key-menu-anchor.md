# MES menu-key → 锚定 MenuId 映射（M-MES Task 2 交付，T3b 输入）

> 生成于 2026-07-12。**正本 = `CP6.WebApi/Seed/MesMenuSeed.cs`**（本表与 `mes-menu-seed.sql` 均为其对照/派生）。
> 依据真相源 `docs/seeds/mes-permission-keys.md` §二（10 menu-key）/§六（硬前置）。
> 用途：T3b `Sys_MenuAction`/`Sys_RoleAction` 逐租户种子以本表「键→MenuId」为锚。

## 10 键锚定表

| # | menu-key | 锚定 MenuId | RoutePath | 菜单名 | 裁决 |
|---|---|---|---|---|---|
| 1 | `mes-planning-board` | 301 | `/mes/planning-board` | 生産計画ボード | 单页，回填即一致，仍显式锚定规避时序 |
| 2 | `mes-work-order` | 302 | `/mes/work-order` | 製造指図 入力 | 入力页锚定；303 一覧留 null |
| 3 | `mes-production-result` | 304 | `/mes/production-result` | 製造実績 入力 | 入力页锚定；305 一覧留 null |
| 4 | `mes-quality-inspection` | 306 | `/mes/quality-inspection` | 品質検査 入力 | 入力页锚定；307 一覧留 null。键取菜单域名，非控制器路由 `inspections` |
| 5 | `mes-defect` | 308 | `/mes/defect` | 不良品管理 | 单页 |
| 6 | `mes-machine` | 310 | `/mes/machine-list` | 設備管理 | ★命门2：RoutePath 回填得 `mes-machine-list`，**显式赋 `mes-machine`** + 防御矫正 |
| 7 | `mes-oee` | 311 | `/mes/oee` | OEE 分析 | 单页 |
| 8 | `mes-plan-achievement` | 313 | `/mes/plan-achievement` | 生産計画達成率 | 单页，仅 view |
| 9 | `mes-work-center` | 314 | `/mes/work-center` | 工作中心 | 单页，主数据 |
| 10 | `mes-process-cost-rate` | 315 | `/mes/process-cost-rate` | 工序费率 | 单页，主数据 |

## 非锚定行（MenuKey 留 null，由回填派生后缀键，不承载权限）

| MenuId | 菜单名 | RoutePath | 类别 |
|---|---|---|---|
| 300 | 製造執行(MES) | (null) | 父行 |
| 303 | 製造指図 一覧 | `/mes/work-order-list` | 一覧页 |
| 305 | 製造実績 一覧 | `/mes/production-result-list` | 一覧页 |
| 307 | 品質検査 一覧 | `/mes/quality-inspection-list` | 一覧页 |
| 309 | MESダッシュボード | `/mes/dashboard` | GET-only 看板 |
| 312 | Control Tower 大屏 | `/mes/control-tower` | GET-only 可视化 |

## 段位与孤儿

- MenuId 段位 = 既有 300–315（16 行），**全部已由 Program.cs 播种，无新建缺行**。
- MES **零孤儿路由**（T1 已证 15 前端页全映射到 300 段），本任务纯锚定、无收编（对比 ERP 216–220 收编 5 孤儿）。
- 唯一索引安全：10 锚定键互不相同（真相源 §二，MES 回填按 RoutePath 天然差异化，一域两页仅锚入力页），
  不撞 `Sys_Menus.MenuKey IS NOT NULL` 过滤唯一索引。

## 硬前置落实

1. **回填时序**：`MesMenuSeed.EnsureSeeded` 接入 Program.cs `~:844`（WmsPermissionSeed 之后），
   **先于回填块 :894** 执行 → 洁净首启即赋 MenuKey，无 null-全-403 窗口。
2. **machine 键错配**：310 显式 `mes-machine`（非回填 `mes-machine-list`），防御矫正块严限 10 锚定行就地纠回。
