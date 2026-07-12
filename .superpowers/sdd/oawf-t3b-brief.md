# M-OA/WF T3b 任务简报：逐租户 MenuAction/RoleAction 权限种子

## 背景与位置
M-OA/WF 横切接线波第四任务。T2 锚定表+T3a 31 贴点已过审。样板=**MesPermissionSeed**(最新版,CP6.WebApi/Seed/MesPermissionSeed.cs+Tests)。

## 必读(按顺序)
1. `docs/seeds/oawf-key-menu-anchor.md`(T2 锚定表——key→MenuId 唯一依据,733-739)
2. `docs/seeds/oawf-permission-keys.md`(合一后真相源——(menu-key,action) 元组来源,22 资源键)
3. MesPermissionSeed 四要件照抄: 枚举 Sys_Tenants 显式 TenantId / IgnoreQueryFilters 查重 / MenuAction+RoleAction 双种 RoleId=1 / StampTenant 不覆盖。

## 需求
1. 新建 `OawfPermissionSeed.EnsureSeeded`,接入 Program.cs 紧随 OawfMenuSeed 之后,每启动幂等。
2. 种子元组=31 贴点按 (menu-key,action) 去重(归并含: 三 delegate 合一/新旧栈 SaveDef 同键等,以 grep 实际贴点为准),经锚定表映射 (MenuId,ActionCode)。三数闭环(31→N→N)写入报告,漏 0 多 0。
3. 2 豁免不入种子。
4. 测试: OawfPermissionSeedTests——ExpectedTuples 独立硬编码 oracle/逐租户逐元组集合相等/幂等行数级/RoleId=1+MenuId 来自锚定表。
5. SQL 对照 `docs/seeds/oawf-permission-seed.sql`(CROSS JOIN+NOT EXISTS,头声明 C# 正本)。

## Global Constraints
- 基线 1764 不许跌;每 commit 即 push;键连字符;RoleAction 逐租户。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\oawf-t3b-report.md`(三数闭环+TDD 证据)。回复只返回: 状态、commit sha、一行测试结论、三数闭环一行、concerns、报告路径(15 行内)。