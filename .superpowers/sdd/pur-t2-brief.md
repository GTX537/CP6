# M-PUR T2 任务简报：贴点 + 菜单锚定 + 逐租户种子（一体）

## 背景与位置
M-PUR 横切接线波第二任务。T1 真相源已过审（opus 独立复核全绿）：`docs/seeds/pur-permission-keys.md` 的键面、豁免、§六两条硬前置均已亲验成立。T2 = 把真相源逐字落成代码。T3（反射测试+403 用例）在后，不归本任务。

## 必读（按顺序）
1. `docs/seeds/pur-permission-keys.md` —— **唯一真相源，键名/锚定/豁免逐字照抄，不得改判**（§五留的改判口径属用户裁决，本任务不动）。
2. `docs/00-横切接线规范.md`。
3. 逐租户种子先例：`CP6.WebApi/Seed/WmsPermissionSeed.cs`（模式照抄）及其测试；菜单种子先例参考 OawfMenuSeed。
4. Program.cs Pur 段现状：菜单插入（:1385–1414 一带）、局部回填（:1513 一带，只盖 701–704）、内联种子（:1518–1531，仅默认租户）。

## 需求
1. **贴点**：真相源 §一/§三 所列新键逐端点贴 `[RequirePermission]`（PurchaseRequest / Rfq / Subcontract），构造参数顺序对齐同目录已贴控制器。§四豁免端点按真相源裁定处理（归 view 的照真相源写法）。既有 10 贴点不动。
2. **菜单锚定（硬前置#1 修复）**：705/706/707 的 MenuKey **显式赋值**，且保证首启一遍就位（不得依赖 :922 全局回填——它早于 Pur 菜单插入执行）。局部回填块与显式赋值的关系照前波先例处理（显式赋值先于/取代回填派生），701–704 现状已正确不要动坏。
3. **逐租户种子（硬前置#2 修复）**：新建 `CP6.WebApi/Seed/PurPermissionSeed.cs`，照 WmsPermissionSeed 逐租户模式（运行时枚举 `Sys_Tenants` 全部 Id，勿硬编码租户数；显式 TenantId；`IgnoreQueryFilters()` 幂等判存），一次覆盖**全部 24 键 = 既有 10 + 新 14**（MenuAction + admin RoleAction）。旧内联 :1518–1531 块按真相源 §六 处理（由新 Seed 取代或收编，不得留下"新键仍只默认租户"的残口，也不得造成重复种子）。在 Program.cs 于 Pur 菜单+MenuKey 就位之后接线调用。
4. **测试**：为 PurPermissionSeed 写单元测试（照既有 Seed 测试先例）：幂等（跑两遍不翻倍）、逐租户覆盖（每租户 24 元组）、键面与真相源一致（oracle 独立写死于测试，勿引用生产常量）、误删/误改会红。菜单锚定如有可测面（MenuKey 显式值）一并断言。
5. **全量绿**：基线 1796 绿（T1 零代码未动）。迭代期跑聚焦测试，提交前跑全量一次。

## 全局约束（前波命门）
- 键一律连字符；资源键=锚定菜单 MenuKey；MenuKey 显式赋值先于任何回填派生。
- 种子逐租户，默认租户单份即缺陷。
- 每个 commit 立即 push（硬性纪律，可多 commit）。
- 不改 T3 范围（反射 fail-closed 测试、403 集成用例）；不动真相源正文（发现真相源与代码事实冲突→停下报 BLOCKED，勿自行改文档）。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\pur-t2-report.md`（实现清单、测试证据 RED/GREEN、自审、concerns）。回复只返回（15 行内）：状态、commits（短 SHA+题）、一行测试摘要（全量数）、concerns。
