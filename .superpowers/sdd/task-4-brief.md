### Task 4: 权限点接线（9 控制器 + 反射测试 + RoleAction 种子）

**Files:**
- Modify: `CP6.WebApi/Controllers/Space/` 全部 9 控制器——按 Global Constraints 映射表贴 `[RequirePermission]`（`using CP6.Core.Auth;`），控制器头注释写权限约定一行
- Create: `CP6.Tests/Space/SpacePermissionAttributeTests.cs`——反射断言：9 控制器的全部 POST/PUT/DELETE action（HttpPost/HttpPut/HttpDelete 特性）都带 RequirePermissionAttribute 且 (menu,action) 在映射白名单内；GET action 都不带（防误贴）。豁免清单显式列出（如无）。
- Create: `docs/seeds/space-roleaction-seed.sql`——①`Sys_MenuAction` 登记各菜单可授权动作（902:add/edit/delete；903:add/edit/delete/edit（编辑器域并入 edit）；904:add/edit/delete/generate；905:publish/deactivate/adopt；906 无）幂等 NOT EXISTS，**逐租户**（`CROSS JOIN (SELECT Id FROM Sys_Tenants) t` 或按现有租户循环——先看 Sys_MenuAction 的 TenantId 语义与既有数据惯例，qa 种子 02_seed_roleaction_BC.sql 是显式 TenantId 先例）；②`Sys_RoleAction` 给 RoleId=1 全动作授权，列序 `(Id,RoleId,MenuId,ActionCode,CreateDate,TenantId)`、Id=NEWID()；③验证查询+回滚段+头注释（连接串 CP6DB 套路）。

- [ ] Step 1: 反射测试先行（RED：0 个特性）→ 贴特性 → GREEN
- [ ] Step 2: 种子 SQL 人工双检；全量测试绿（RequirePermission 在单测无 HTTP 管道不触发——控制器单测若有直调 action 的，确认不受影响）
- [ ] Step 3: Commit `feat(space): 细粒度权限点——9 控制器 RequirePermission + MenuAction/RoleAction 种子`

---

