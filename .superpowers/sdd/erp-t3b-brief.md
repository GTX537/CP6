# M-ERP T3b 任务简报：逐租户 MenuAction/RoleAction 权限种子

## 背景与位置
M-ERP 横切接线波第四任务。T2 已锚定 14 个 erp-* MenuKey 并产出锚定表;T3a 已把 35 个写端点贴上 [RequirePermission]。本任务把「键:action」种成逐租户的 Sys_MenuAction + Sys_RoleAction 行,使 admin(RoleId=1)在每个租户都放行。

## 必读(按顺序)
1. `docs/seeds/erp-key-menu-anchor.md`(T2 锚定表——key→MenuId 映射的唯一依据)
2. `docs/seeds/erp-permission-keys.md`(T1 真相源——(menu-key, action) 元组清单来源)
3. 样板: **WmsPermissionSeed**(`git show 358ee7e` / CP6.WebApi/Seed/WmsPermissionSeed.cs)——本任务是其 ERP 同型复制,四要件照抄:
   - 枚举 Sys_Tenants 逐租户播种,显式赋 TenantId
   - `IgnoreQueryFilters()` 查重(load-bearing,否则查询被租户过滤器遮蔽导致重复插入)
   - MenuAction + RoleAction 双种,RoleAction 挂 RoleId=1
   - StampTenant 不得覆盖显式 TenantId 值

## 需求
1. 新建 `ErpPermissionSeed.EnsureSeeded`,接入 Program.cs 于 ErpMenuSeed 之后(菜单行须先存在),每启动幂等。
2. 种子元组=T1 真相源 35 写端点按 (menu-key, action) **去重**后的集合,经锚定表映射到 (MenuId, ActionCode)。交付对账: 控制器 35 端点 → 去重 (key,action) 元组数 → 种子元组数,三数闭环写入报告(漏种 0 多种 0;M-WMS 先例为 125 端点→112 元组)。
3. 只读 POST 豁免 11 条与 AllowAnonymous 裁决点**不入种子**(未贴点即无键可种)。
4. 测试: ErpPermissionSeedTests——至少覆盖 幂等(二次调用行数不变)/逐租户(两租户各得全套)/元组闭环计数/RoleAction 挂 RoleId=1 且 MenuId 来自锚定表。断言真实,删实现会红。
5. SQL 对照文件 `docs/seeds/erp-permission-seed.sql`(照 space-roleaction-seed.sql 的 CROSS JOIN Sys_Tenants 风格,文件头声明 C# 为正本)。

## Global Constraints
- 基线不许跌(T3a 后基线以其报告为准);每 commit 立即 push。
- 键一律连字符;资源键=锚定菜单行 MenuKey;RoleAction 逐租户(存量 Fin/Sys 仅默认租户的做法是已知缺口,勿模仿)。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\erp-t3b-report.md`(三数闭环对账+测试输出摘要)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。
