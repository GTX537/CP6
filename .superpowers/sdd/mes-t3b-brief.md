# M-MES T3b 任务简报：逐租户 MenuAction/RoleAction 权限种子

## 背景与位置
M-MES 横切接线波第四任务。T2 锚定表+T3a 贴点(28 端点)已过审。本任务把 (key,action) 种成逐租户 Sys_MenuAction+Sys_RoleAction,admin(RoleId=1)每租户放行。

## 必读(按顺序)
1. `docs/seeds/mes-key-menu-anchor.md`(T2 锚定表——key→MenuId 唯一依据)
2. `docs/seeds/mes-permission-keys.md`(T1 真相源——(menu-key,action) 元组来源)
3. 样板: **ErpPermissionSeed**(CP6.WebApi/Seed/ErpPermissionSeed.cs+ErpPermissionSeedTests.cs,M-ERP T3b 已上线)——MES 同型复制,四要件照抄: 枚举 Sys_Tenants 显式 TenantId / IgnoreQueryFilters 查重(load-bearing) / MenuAction+RoleAction 双种 RoleId=1 / StampTenant 不覆盖显式值。

## 需求
1. 新建 `MesPermissionSeed.EnsureSeeded`,接入 Program.cs 紧随 MesMenuSeed 之后,每启动幂等。
2. 种子元组=28 写端点按 (menu-key,action) **去重**后的集合(真相源§五四处归并→去重数<28),经锚定表映射 (MenuId,ActionCode)。三数闭环(28 端点→去重 N 元组→N 种子)写入报告,漏 0 多 0。
3. 2 只读豁免不入种子。
4. 测试: MesPermissionSeedTests 照 ErpPermissionSeedTests——**ExpectedTuples 独立硬编码 oracle**(非引用种子常量)/逐租户逐元组集合相等/幂等行数级/RoleId=1+MenuId 来自锚定表。
5. SQL 对照 `docs/seeds/mes-permission-seed.sql`(CROSS JOIN Sys_Tenants+NOT EXISTS,文件头声明 C# 正本)。

## Global Constraints
- 基线 1722 不许跌;每 commit 立即 push;键连字符;RoleAction 逐租户。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t3b-report.md`(三数闭环对账+TDD 证据)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。