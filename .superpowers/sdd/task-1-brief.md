### Task 1: StandardRoleSeed 标准角色种子（TDD）

**Files:**
- Create: `CP6.WebApi/Seed/StandardRoleSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（注册，插在 OawfPermissionSeed 调用之后、与其同形态）
- Test: `CP6.Tests/StandardRoleSeedTests.cs`（新文件）

**Interfaces:**
- Consumes: `Sys_Role`（复合主键 TenantId+RoleId, int RoleId, RoleName）、`Sys_RoleMenu`、`Sys_RoleAction` 实体；`PurPermissionSeed`/`OawfPermissionSeed` 的逐租户播种模式（**先读这两个文件照抄其租户遍历/盖章/幂等形态**——包括它们怎么取租户清单、怎么处理 TenantId 列/全局过滤器）。
- Produces: 每租户 `Sys_Roles` 行 (RoleId=10, RoleName=一般用户)；`Sys_RoleMenus` 4 行/租户 (RoleId=10 × MenuId 740/733/735/737)；`Sys_RoleActions` 8 行/租户 (RoleId=10 × 上述最小键集)。

- [ ] **Step 1: 写失败测试（RED）**

新建 `CP6.Tests/StandardRoleSeedTests.cs`，照既有 `*PermissionSeedTests` 的测试harness形态（先读 `OawfPermissionSeedTests`——用它的 in-memory db + 租户构造方式）。断言面：

```csharp
// 形态照 OawfPermissionSeedTests 改；核心断言（每条一个 [Fact]）：
// 1. Seed_CreatesRole10_PerTenant：跑种子后每租户存在 (RoleId=10, RoleName="一般用户")。
// 2. Seed_GrantsExactly4Menus：RoleId=10 的 Sys_RoleMenus = {740,733,735,737}（集合相等，不多不少）。
// 3. Seed_GrantsExactly8Actions：RoleId=10 的 Sys_RoleActions 投影 (MenuId,ActionCode) 集合相等于
//    {(733,"read"),(733,"approve"),(733,"transfer"),(733,"sendback"),(733,"withdraw"),
//     (735,"submit"),(735,"favorite"),(737,"delegate")}。
// 4. Seed_IsIdempotent：连跑两遍，三表行数不变（无重复）。
// 5. Seed_DoesNotTouchAdminRole：跑种子前后 RoleId=1 的三表行零 diff。
// 6. Seed_ActionsSubsetOfCatalog：8 键均存在于 Sys_MenuActions 目录（先跑 OawfPermissionSeed 再跑本种子）。
// 7. Aggregator_UserWithRole10_GetsExactly8Keys：造用户挂 RoleId=10，PermissionAggregator 聚合出
//    恰好 8 个 "menu-key:action"（含 "oa-inbox:approve"），且不含 "oa-inbox:addsign"。
```

- [ ] **Step 2: 跑新测试确认 RED**

```
dotnet build CP6.Tests/CP6.Tests.csproj -m:1 --nologo -v q
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~StandardRoleSeedTests"
```
预期：`StandardRoleSeed` 类不存在编译失败 → 先建空壳类（SeedAsync 空体）让编译过，跑测试全 FAIL。记录 RED 证据。

- [ ] **Step 3: 实现种子**

`CP6.WebApi/Seed/StandardRoleSeed.cs`——骨架（**租户遍历/盖章细节照 PurPermissionSeed 原样，此处只定内容**）：

```csharp
/// <summary>
/// 标准角色种子（普通角色授权放开波 T1）：逐租户预置「一般用户」(RoleId=10) + OA 办理最小键集。
/// 幂等 insert-only：行存在即跳过，绝不更新/删除（admin 后续经 RolePermView 对该角色的手工调整不被重置）。
/// 依赖：菜单 740/733/735/737 与 OawfPermissionSeed 的 MenuActions 目录已播种（Program.cs 注册序保证）。
/// 蓄意不授：addsign / oa-designer / oa-flow-admin / oa-approver-map / oa-work-calendar（admin 手工放）。
/// </summary>
public static class StandardRoleSeed
{
    public const int GeneralRoleId = 10;
    private const string GeneralRoleName = "一般用户";

    private static readonly int[] Menus = { 740, 733, 735, 737 };

    private static readonly (int MenuId, string Code)[] Actions =
    {
        (733, "read"), (733, "approve"), (733, "transfer"), (733, "sendback"), (733, "withdraw"),
        (735, "submit"), (735, "favorite"),
        (737, "delegate"),
    };

    // SeedAsync(db)：照 PurPermissionSeed 的租户遍历形态——对每租户：
    //   ① Sys_Roles 无 (tenant, 10) 行 → 插 { RoleId=10, RoleName=GeneralRoleName }
    //   ② 对 Menus 每项：Sys_RoleMenus 无 (tenant, RoleId=10, MenuId) 行 → 插
    //   ③ 对 Actions 每项：Sys_RoleActions 无 (tenant, RoleId=10, MenuId, ActionCode) 行 → 插
    //   TenantId 盖章方式与 PurPermissionSeed 逐字同款。
}
```

`Program.cs`：在 OawfPermissionSeed（及其三个附加种子）调用之后追加 `await StandardRoleSeed.SeedAsync(db);`（调用形态照前后邻居）。

- [ ] **Step 4: 跑新测试确认 GREEN**

```
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~StandardRoleSeedTests"
```
预期 7/7 PASS。

- [ ] **Step 5: 全量回归**

```
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo
```
预期 **2220 绿（2213+7）/5 skip/0 fail**。

- [ ] **Step 6: Commit + push**

```
git add -A
git commit -m "feat(auth): 标准角色种子——逐租户一般用户(RoleId=10)+OA办理最小键集8键4菜单, 幂等insert-only"
git push
```

---

