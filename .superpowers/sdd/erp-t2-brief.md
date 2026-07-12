# M-ERP T2 任务简报：菜单 MenuKey 锚定 + 五孤儿路由收编

## 背景与位置
M-ERP 横切接线波(branch=feat/m-erp-crosscutting, base=0c8d4d3)第二任务。T1 已产出权限键真相源 `docs/seeds/erp-permission-keys.md`(15控制器/35真写端点+11只读POST豁免=46行/14个 erp-* menu-key)。本任务为 T3b 权限种子提供「键→菜单行」锚定,并收编计划点名的五条孤儿路由(原计划 T4 前置并入,因 T3b 种子依赖菜单行存在)。

## 需求(真相源=台账 2026-07-12 T1 条目 + 计划 M-ERP 段 + M-WMS T2 先例)

1. **ErpMenuSeed 启动幂等种子**,接入 Program.cs 且**必须先于 RoutePath 回填块执行**。
   - 🔴硬前置(T1 审查者独立实证): 既有 ERP 菜单 201-215 的 RoutePath 是裸路径(如 `/order`,无 `/erp/` 段),若被回填块先跑,派生键无 `erp-` 前缀,与 T1 的 erp-* 键全体失配 → 全 ERP 403。
2. **14 个 menu-key 各定唯一锚定菜单行**,显式赋 erp-* MenuKey。
   - 约束: Sys_Menu.MenuKey 有 IS NOT NULL 唯一(过滤)索引,禁两行共键;6 个域存在「一覧+登録」双菜单行,须择一为锚(另一行 MenuKey 留 null)。
3. **防御矫正块**: 对已被回填成错误键(无 erp- 前缀)的存量行就地纠回,作用域严限 ERP 锚定行,无其他副作用(照 M-WMS T2 先例)。
4. **五孤儿路由收编**: `/erp/order-trace`、`/erp/credit-note`、`/erp/backorder`、`/erp/otd-report`、`/erp/fx-rate` 补 Sys_Menu 行(含 erp-* MenuKey)+ RoleMenu 授管理员;新 MenuId 段位不得与既有行碰撞(M-WMS 曾有 429 避让先例)。
5. **输出锚定表** `docs/seeds/erp-key-menu-anchor.md`(14 键 → MenuId 映射,T3b 输入)+ SQL 对照 `docs/seeds/erp-menu-seed.sql`(与 C# 正本一致)。
6. **测试**: ErpMenuSeedTests 覆盖锚定/孤儿收编/唯一键/幂等/矫正等关键行为,断言真实(删实现会红)。
7. 键名一律**连字符**(erp-order 等),禁下划线——全仓约定,跨波命门。

## Global Constraints(计划原文)
- 基线不许跌(本波基线=1683 后端全绿);每 commit 立即 push。
- 权限贴点节奏: MenuKey 命名与 MenuId 段位先登记再播种。
- 每波结尾跑该模块 fail-closed 反射测试 + 全量回归(T4 兑现,非本任务)。
