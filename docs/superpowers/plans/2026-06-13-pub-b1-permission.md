# PUB B1 · 权限引擎四粒度（章01~04）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**PUB 第二份计划**，依赖 **B0 组织模型计划已落地**（`Sys_Dept` + `Sys_User.DeptId`，章03"及下级"子树过滤要用 `Path`）。

**Goal:** 落地 PUB 权限引擎四粒度（痛点核心）——章01 多角色 RBAC（`Sys_UserRole` + `UserPermissionContext` 聚合框架）、章02 功能权限（`[RequirePermission]` 后端强校验）、章03 数据权限（`IDataScopeFilter` 查询注入 + 部门树子树过滤）、章04 字段权限（`[FieldMask]` 序列化掩码 + 只读拒写）。完成后 CP6 从"前端藏菜单"升级为"**后端三权强校验**"：操作=特性 403、数据=查询注入、字段=序列化掩码，多角色按"操作并集、数据最宽、字段最宽"合并。

**Architecture:** 全落 `Sys` 命名空间（权限新表 `Sys_` 前缀），不改名既有 `Sys_User/Role/Menu/RoleMenu`。核心是**会话级聚合上下文 `UserPermissionContext`**：登录时 `PermissionAggregator.BuildAsync(userId)` 把多角色的菜单/操作/数据/字段权限聚合（并集/最宽/最宽）缓存，后端三个强校验点（`RequirePermissionAttribute`/`IDataScopeFilter`/`FieldMaskAttribute`）零额外查库读它。角色权限变更 → 失效缓存 → 下次重建。`ICurrentPermissionContext` 按请求解析当前用户的上下文（缓存命中或重建）。

**Tech Stack:** .NET 8 + EF Core 8（IQueryable 注入 + Authorization/Result Filter + 反射掩码）+ SQL Server / xUnit + EF Core InMemory / Vue 3.5 + element-plus + Pinia。源文档：`docs/pub/01~04`。

---

## 关键前置决策（待你修订时确认 —— ⚠️ B1-D1 是全局性的，强烈建议先拍板）

| # | 议题 | 文档（章01~04）原意 | CP6 现状（已勘察） | **本稿建议值** |
|---|---|---|---|---|
| **B1-D1** | **角色/菜单 主键类型** | 章01~04 一律假设 **GUID**（`Sys_UserRole.RoleId` Guid→`Sys_Role.Id`、`Sys_RoleAction.MenuId` Guid、聚合 join `m.Id`） | **实际是 int**：`Sys_Role.RoleId`(int PK, `DatabaseGenerated.None`, 不继承 BaseEntity)、`Sys_Menu.MenuId`(int PK)、`Sys_RoleMenu(RoleId,MenuId)` int、`Sys_User.RoleId` int? | **保留 int 键**（现状优先，避免迁角色/菜单到 GUID 的全表大改 + 牵动所有消费方）。**所有新权限表 RoleId/MenuId 用 int**：`Sys_UserRole.RoleId` int、`Sys_MenuAction.MenuId` int、`Sys_RoleAction.{RoleId,MenuId}` int、`Sys_RoleDataScope.RoleId` int、`Sys_RoleFieldPerm.RoleId` int；`UserPermissionContext.RoleIds` = `List<int>`。`Sys_UserRole.UserId` 仍 Guid（→ Sys_User.Id）。spec 的 Guid 视为与现状不符、以现状为准 |
| **B1-D2** | **菜单稳定业务键 MenuKey** | 章02 资源键 `menuKey:actionCode`，取 `Sys_Menu.MenuKey` | `Sys_Menu` **无 MenuKey** 字段（只有 MenuId/MenuName/RoutePath） | **给 `Sys_Menu` 补 `MenuKey` 字符串列**（稳定业务键，如 `order`），唯一。资源键 = `MenuKey:ActionCode`。需为现有菜单回填 MenuKey（迁移/种子） |
| **B1-D3** | **TenantId** | 全表 TenantId | 零多租户（同 B0） | **本阶段不引入 TenantId**（与 B0 一致）；唯一索引去掉 TenantId 前缀，章09 统一升级。**⚠️ 与 Space 后端计划方案A 不一致**，请在 B0 一并拍板（建议 Sys 全族统一在章09 处理） |
| **B1-D4** | **审计字段** | DDL `CreateTime/Creator` | 真实 `Creator/CreateDate/Modifier/ModifyDate` | 以代码为准（新权限表继承 `BaseEntity`，得 GUID Id + 真实审计字段） |
| **B1-D5** | **上下文缓存与当前用户解析** | 章01 "缓存到会话/Redis"，`_current.GetContextAsync()` | CP6 有 `CacheService`（见 CacheServiceTests）、JWT `[Authorize]`、控制器 `User?.Identity?.Name` | 新增 `ICurrentPermissionContext`：经 `IHttpContextAccessor` 取当前用户名 → `Sys_User` → 按 `userId` 查缓存（`IMemoryCache` 或复用 `CacheService`），命中返回、未命中 `PermissionAggregator.BuildAsync` 后缓存。失效 = 按 userId 移除（角色变更）或按 roleId 批量移除（角色权限变更） |

> **测试基建**：xUnit + InMemory（同 Space/B0）。`IDataScopeFilter` 的 `Path.StartsWith` 子树过滤在 InMemory 可测（内存字符串），但真实 SQL 翻译需一组 `[需真库]` 集成测兜底。`[RequirePermission]`/`[FieldMask]` Filter 用单元测 + WebApplicationFactory 集成测。

---

## File Structure

### 实体（`CP6.Entity/DomainModels/Sys/`，均继承 BaseEntity）
- `Sys_UserRole.cs`（UserId Guid + RoleId int）、`Sys_MenuAction.cs`（MenuId int + ActionCode + ActionName + Sort）、`Sys_RoleAction.cs`（RoleId int + MenuId int + ActionCode）、`Sys_RoleDataScope.cs`（RoleId int + ResourceKey + ScopeType + CustomDeptIds）、`Sys_RoleFieldPerm.cs`（RoleId int + ResourceKey + FieldName + Access）
- 修改 `Sys_Menu.cs` — 补 `MenuKey` string
- `CP6.Entity/IDataScoped.cs` — `Creator` + `DeptId` 接口（章03）

### 核心服务（`CP6.Core/Services/Sys/` + `CP6.Core/Auth/`）
- `UserPermissionContext.cs` — 聚合上下文（RoleIds/MenuKeys/ActionKeys/DataScopes/CustomDeptIds/FieldPerms + UserName/DeptId/DeptPath）
- `PermissionAggregator.cs` / `IPermissionAggregator.cs` — `BuildAsync(userId)` 聚合四类权限
- `ICurrentPermissionContext.cs` / `CurrentPermissionContext.cs` — 请求级解析 + 缓存 + 失效（B1-D5）
- `IPermissionService.cs` / `PermissionService.cs` — `HasActionAsync`/`HasMenuAsync`（章02）
- `IDataScopeFilter.cs` / `DataScopeFilter.cs` + `DataScopeRegistry.cs`（章03）
- `IFieldPermService.cs` / `FieldPermService.cs`（MaskHidden/StripReadOnly）+ `FieldRegistry.cs`（章04）
- `IUserRoleService.cs` / `UserRoleService.cs`（分配/迁移，章01）；`IRolePermService.cs` / `RolePermService.cs`（菜单+按钮+数据+字段授权配置，章02-04）
- `CP6.Core/Auth/RequirePermissionAttribute.cs`（章02）、`CP6.Core/Auth/FieldMaskAttribute.cs`（章04）

### 控制器（`CP6.WebApi/Controllers/Pub/`）+ Program.cs DI + 迁移
- `UserRoleController`（`/api/pub/user-role`）、`RolePermController`（`/api/pub/role-perm`、`/data-scope`、`/field-perm`、`/my-actions`）

### 前端（`cp6.web/`）
- `src/directives/permission.ts`（`v-permission`）、`src/stores/permission.ts`（actionKeys + readonly fields）
- `src/views/pub/role/`（角色权限配置：菜单+按钮 Tab / 数据权限 Tab / 字段权限 Tab）、用户角色分配（并入用户管理）

### 测试（`CP6.Tests/`）
- `PermissionAggregatorTests`（多角色并集/最宽）、`RequirePermissionFilterTests`、`DataScopeFilterTests`（五范围 + 子树）、`FieldPermServiceTests`（掩码/拒写）、`UserRoleServiceTests`（分配/主角色/迁移）

---

## 实施分四阶段（对应章01~04）

- **Phase A**（A-1..A-4）：章01 多角色 + 聚合框架 + 当前上下文缓存 — **权限引擎地基**
- **Phase B**（B-1..B-4）：章02 功能权限 + `[RequirePermission]` 后端强校验（M1 ★）
- **Phase C**（C-1..C-4）：章03 数据权限 + 查询注入 + 子树过滤
- **Phase D**（D-1..D-3）：章04 字段权限 + 序列化掩码 + 只读拒写 → 四粒度齐

---

# Phase A — 章01 多角色 + 聚合框架（地基）

## Task A-1: Sys_UserRole 实体 + Sys_Menu 补 MenuKey + 迁移（B1-D1/D2）

**Files:** Create `Sys_UserRole.cs`; Modify `Sys_Menu.cs`, `CP6Context.cs`; migration; Test `CP6.Tests/UserRoleServiceTests.cs`（落库往返）

- [ ] **Step 1: 失败测试**（Sys_UserRole 落库 RoleId int + UserId Guid；Sys_Menu.MenuKey 可存）`[InMemory]`

```csharp
public class UserRoleServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task UserRole_RoundTrips_WithIntRoleId()
    {
        using var db = Db();
        var uid = Guid.NewGuid();
        db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = 7 });
        await db.SaveChangesAsync();
        var r = await db.Sys_UserRoles.SingleAsync();
        Assert.Equal(7, r.RoleId); Assert.Equal(uid, r.UserId);
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现实体 + 补字段 + 注册**

```csharp
// Sys_UserRole.cs
[Table("Sys_UserRole")]
public class Sys_UserRole : BaseEntity   // Id GUID + 审计
{
    public Guid UserId { get; set; }     // → Sys_User.Id
    public int  RoleId { get; set; }     // → Sys_Role.RoleId（int，B1-D1）
}
```
`Sys_Menu.cs` 补：
```csharp
    /// <summary>稳定业务键（资源键前缀，如 order）。PUB 章02 权限资源键用，不用易变的 MenuId/名称。</summary>
    [MaxLength(100)] public string? MenuKey { get; set; }
```
`CP6Context` 加 `DbSet<Sys_UserRole> Sys_UserRoles` + `OnModelCreating`：
```csharp
    b.Entity<Sys_UserRole>(e => {
        e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();   // 防重复授予（章09 加 TenantId）
        e.HasIndex(x => x.UserId);
    });
    b.Entity<Sys_Menu>().HasIndex(x => x.MenuKey).IsUnique();     // MenuKey 唯一（过滤 null）
```

- [ ] **Step 4: 跑绿 → Step 5: 迁移 `dotnet ef migrations add PubB1Multirole` + 提交**

```bash
git commit -m "feat(pub): Sys_UserRole(int RoleId) + Sys_Menu.MenuKey + migration (ch01/02)"
```

---

## Task A-2: UserPermissionContext + PermissionAggregator（菜单并集，章01 §3/§9）

**Files:** Create `UserPermissionContext.cs`, `IPermissionAggregator.cs`, `PermissionAggregator.cs`; Test `PermissionAggregatorTests.cs`

- [ ] **Step 1: 失败测试**（多角色取全角色=Sys_UserRole ∪ 主角色去重；MenuKeys 并集）

```csharp
[Fact]
public async Task Build_MergesAllRoles_UnionMenus()
{
    using var db = Db();
    var uid = Guid.NewGuid();
    db.Sys_Users.Add(new Sys_User { Id = uid, UserName="u", Password="x", RoleId = 1 });   // 主角色 1
    db.Sys_UserRoles.Add(new Sys_UserRole { Id=Guid.NewGuid(), UserId=uid, RoleId=2 });    // 附加角色 2
    db.Sys_Menus.Add(new Sys_Menu { MenuId=10, MenuName="订单", MenuKey="order" });
    db.Sys_RoleMenus.AddRange(new Sys_RoleMenu{RoleId=1,MenuId=10});
    await db.SaveChangesAsync();
    var agg = MakeAggregator(db);
    var ctx = await agg.BuildAsync(uid);
    Assert.Equal(new[]{1,2}, ctx.RoleIds.OrderBy(x=>x));   // 主角色并入
    Assert.Contains("order", ctx.MenuKeys);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
// UserPermissionContext.cs
public class UserPermissionContext
{
    public Guid       UserId  { get; set; }
    public string?    UserName{ get; set; }          // 章03 本人范围
    public Guid?      DeptId  { get; set; }          // 章03 本部门
    public string     DeptPath{ get; set; } = "";    // 章03 及下级（子树前缀）
    public List<int>  RoleIds { get; set; } = new();
    public HashSet<string> MenuKeys   { get; set; } = new();
    public HashSet<string> ActionKeys { get; set; } = new();                       // "menuKey:action"（章02）
    public Dictionary<string,int> DataScopes { get; set; } = new();                // resourceKey→最宽 ScopeType（章03）
    public Dictionary<string,List<Guid>> CustomDeptIds { get; set; } = new();      // 章03 自定义并集
    public Dictionary<string,Dictionary<string,int>> FieldPerms { get; set; } = new(); // 章04 最宽
}
```
```csharp
// PermissionAggregator.cs（菜单并集；ActionKeys/DataScopes/FieldPerms 在 B/C/D 阶段填充同一方法）
public async Task<UserPermissionContext> BuildAsync(Guid userId)
{
    var user = await _db.Sys_Users.FindAsync(userId) ?? throw new InvalidOperationException("E-PUB-404");
    var roleIds = await GetAllRoleIdsAsync(userId);
    var ctx = new UserPermissionContext { UserId = userId, UserName = user.UserName, RoleIds = roleIds };
    if (user.DeptId is Guid did) {
        var dept = await _db.Sys_Depts.FindAsync(did);     // 章00（B0 计划）
        ctx.DeptId = did; ctx.DeptPath = dept?.Path ?? "";
    }
    ctx.MenuKeys = (await _db.Sys_RoleMenus.Where(rm => roleIds.Contains(rm.RoleId))
        .Join(_db.Sys_Menus, rm => rm.MenuId, m => m.MenuId, (rm, m) => m.MenuKey)
        .Where(k => k != null).ToListAsync())!.ToHashSet()!;
    await FillActionKeysAsync(ctx, roleIds);    // B 阶段实现
    await FillDataScopesAsync(ctx, roleIds);    // C 阶段实现
    await FillFieldPermsAsync(ctx, roleIds);    // D 阶段实现
    return ctx;
}

private async Task<List<int>> GetAllRoleIdsAsync(Guid userId)
{
    var roles = await _db.Sys_UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
    var primary = (await _db.Sys_Users.FindAsync(userId))?.RoleId;
    if (primary is int p && !roles.Contains(p)) roles.Add(p);
    return roles.Distinct().ToList();
}
// B/C/D 阶段前先给 Fill* 写空实现（no-op），随各阶段填充
```

- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): UserPermissionContext + aggregator (menu union) (ch01 §3/§9)"`

---

## Task A-3: ICurrentPermissionContext 缓存 + 失效（B1-D5，章01 §8.2/8.3）

**Files:** Create `ICurrentPermissionContext.cs`, `CurrentPermissionContext.cs`; Test

- [ ] **Step 1: 失败测试**（首次 build 并缓存；同 userId 二次命中缓存不重 build；Invalidate(userId) 后重 build）
- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
public interface ICurrentPermissionContext
{
    Task<UserPermissionContext> GetAsync();            // 当前请求用户（IHttpContextAccessor 解析）
    void Invalidate(Guid userId);                      // 用户角色变更
    void InvalidateByRole(int roleId);                 // 角色权限变更 → 该角色全部用户
}
```
```csharp
// CurrentPermissionContext.cs
public class CurrentPermissionContext : ICurrentPermissionContext
{
    private readonly IHttpContextAccessor _http; private readonly IMemoryCache _cache;
    private readonly CP6Context _db; private readonly IPermissionAggregator _agg;
    public CurrentPermissionContext(IHttpContextAccessor http, IMemoryCache cache, CP6Context db, IPermissionAggregator agg)
    { _http = http; _cache = cache; _db = db; _agg = agg; }

    public async Task<UserPermissionContext> GetAsync()
    {
        var name = _http.HttpContext?.User?.Identity?.Name ?? throw new InvalidOperationException("未登录");
        var user = await _db.Sys_Users.FirstOrDefaultAsync(u => u.UserName == name)
                   ?? throw new InvalidOperationException("用户不存在");
        return await _cache.GetOrCreateAsync(CacheKey(user.Id), async e => {
            e.SlidingExpiration = TimeSpan.FromMinutes(30);
            return await _agg.BuildAsync(user.Id);
        }) ?? throw new InvalidOperationException("权限上下文构建失败");
    }
    public void Invalidate(Guid userId) => _cache.Remove(CacheKey(userId));
    public void InvalidateByRole(int roleId)
    {
        // 该角色全部用户（Sys_UserRole.RoleId==roleId ∪ Sys_User.RoleId==roleId）逐个 Remove
        var users = _db.Sys_UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId)
            .Union(_db.Sys_Users.Where(u => u.RoleId == roleId).Select(u => u.Id)).Distinct().ToList();
        foreach (var uid in users) _cache.Remove(CacheKey(uid));
    }
    private static string CacheKey(Guid uid) => $"perm-ctx:{uid}";
}
```

> **实现者注**：用 `IMemoryCache`（单机）；多实例部署改 Redis（CP6 有缓存基建）。`InvalidateByRole` 在 InMemory 测试中 `_cache` 用真实 MemoryCache 实例即可测命中/失效。

- [ ] **Step 4: 跑绿 → Step 5: DI（`IHttpContextAccessor`/`IMemoryCache`/`IPermissionAggregator`/`ICurrentPermissionContext`）+ 提交**

```bash
git commit -m "feat(pub): current permission context cache + invalidation (ch01 §8)"
```

---

## Task A-4: UserRoleService（分配/主角色/迁移）+ 控制器 + 前端分配 UI（章01 §5/§8）

**Files:** Create `IUserRoleService.cs`/`UserRoleService.cs`, `UserRoleController.cs`; 前端用户角色分配（并入用户管理）; Test

- [ ] **Step 1: 失败测试**（保存 diff 增删 Sys_UserRole；primaryRoleId ∉ roleIds → E-PUB-011；保存后 Invalidate(userId)；migrate 幂等：单角色→中间表）
- [ ] **Step 2: 跑红 → Step 3: 实现**（`SaveAsync(userId, roleIds, primaryRoleId)`：校验主角色 ∈ roleIds，diff 增删中间表，写 `Sys_User.RoleId=primary`，调 `Invalidate`；`MigrateAsync`：遍历有 RoleId 的用户插 Sys_UserRole 幂等）
- [ ] **Step 4: 跑绿 → Step 5: 控制器 `/api/pub/user-role/{userId}` GET/PUT + `/migrate` POST + 前端 el-transfer 角色分配 + 主角色单选 + DI + 提交**

```bash
git commit -m "feat(pub): user-role assignment + migration + UI (ch01 §5/§8)"
```

---

# Phase B — 章02 功能权限（M1 ★ 后端强校验）

## Task B-1: Sys_MenuAction + Sys_RoleAction 实体 + 迁移（章02 §2）

**Files:** Create `Sys_MenuAction.cs`, `Sys_RoleAction.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体（int 键，B1-D1）+ 注册 + 唯一索引**

```csharp
[Table("Sys_MenuAction")]
public class Sys_MenuAction : BaseEntity { public int MenuId {get;set;} public string ActionCode {get;set;}=""; public string ActionName {get;set;}=""; public int Sort {get;set;} }
[Table("Sys_RoleAction")]
public class Sys_RoleAction : BaseEntity { public int RoleId {get;set;} public int MenuId {get;set;} public string ActionCode {get;set;}=""; }
```
索引：`UX_Sys_MenuAction(MenuId,ActionCode)`、`UX_Sys_RoleAction(RoleId,MenuId,ActionCode)`、`IX_Sys_RoleAction_Role(RoleId)`。
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(pub): menu-action + role-action entities (ch02 §2)"`

## Task B-2: ActionKeys 聚合 + IPermissionService（章02 §5）

**Files:** Modify `PermissionAggregator.cs`（`FillActionKeysAsync`）; Create `IPermissionService.cs`/`PermissionService.cs`; Test

- [ ] **Step 1: 失败测试**（多角色 RoleAction join Sys_Menu.MenuKey → ActionKeys 并集含 "order:export"；HasActionAsync 命中/不命中）
- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
// PermissionAggregator.FillActionKeysAsync
private async Task FillActionKeysAsync(UserPermissionContext ctx, List<int> roleIds)
{
    ctx.ActionKeys = (await _db.Sys_RoleActions.Where(ra => roleIds.Contains(ra.RoleId))
        .Join(_db.Sys_Menus, ra => ra.MenuId, m => m.MenuId, (ra, m) => m.MenuKey + ":" + ra.ActionCode)
        .Where(k => k != null).ToListAsync())!.ToHashSet()!;
}
```
```csharp
// PermissionService.cs
public class PermissionService : IPermissionService
{
    private readonly ICurrentPermissionContext _cur;
    public PermissionService(ICurrentPermissionContext cur) => _cur = cur;
    public async Task<bool> HasActionAsync(string menu, string action) => (await _cur.GetAsync()).ActionKeys.Contains($"{menu}:{action}");
    public async Task<bool> HasMenuAsync(string menu) => (await _cur.GetAsync()).MenuKeys.Contains(menu);
}
```
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): action keys aggregation + IPermissionService (ch02 §5)"`

## Task B-3: [RequirePermission] 特性 + 授权管线（章02 §4）

**Files:** Create `CP6.Core/Auth/RequirePermissionAttribute.cs`; Test `RequirePermissionFilterTests.cs`

- [ ] **Step 1: 失败测试**（命中放行；不命中 → context.Result = 403 ObjectResult）—— mock `IPermissionService`/请求服务。
- [ ] **Step 2: 跑红 → Step 3: 实现**（照章02 §4 `IAsyncAuthorizationFilter`，不命中置 403；`[Authorize]` 仍在前验登录）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): [RequirePermission] authorization filter — 403 enforcement (ch02 §4)"`

## Task B-4: 角色授权 UI（菜单+按钮）+ v-permission + my-actions（章02 §6/§7）

**Files:** Create `RolePermController`(部分)、`RolePermService`(部分)、前端 `src/directives/permission.ts`、`src/stores/permission.ts`、`src/views/pub/role/MenuActionTab.vue`

- [ ] **Step 1: 实现**——后端：`/api/pub/role-perm/menu-action/{menuId}` GET/PUT（维护操作点）、`/{roleId}` GET/PUT（保存 diff RoleMenu/RoleAction，校验操作 ⊆ 已授菜单 E-PUB-021，`InvalidateByRole`）、`/my-actions` GET（当前用户 ActionKeys）。前端：`v-permission` 指令（actionKeys.has 否则移除元素）+ permission store（登录拉 my-actions）+ 角色权限配置页（菜单树 checkbox + 操作点 checkbox，未授菜单禁其操作点）。
- [ ] **Step 2: 集成测（WebApplicationFactory）**：贴 `[RequirePermission("order","export")]` 的测试端点，无权 403 / 有权 200。
- [ ] **Step 3: 提交** → `git commit -m "feat(pub): role menu/button authorization UI + v-permission (ch02 §6/§7)"`

---

# Phase C — 章03 数据权限（查询注入）

## Task C-1: Sys_RoleDataScope + IDataScoped + 迁移 + ctx 扩展（章03 §3/§4/§6）

**Files:** Create `Sys_RoleDataScope.cs`, `CP6.Entity/IDataScoped.cs`; Modify `CP6Context.cs`, `PermissionAggregator.cs`(`FillDataScopesAsync`); migration

- [ ] **Step 1: 失败测试**（FillDataScopesAsync：多角色同资源取 MAX ScopeType；ScopeType=4 自定义部门取并集）
- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
[Table("Sys_RoleDataScope")]
public class Sys_RoleDataScope : BaseEntity { public int RoleId{get;set;} public string ResourceKey{get;set;}=""; public int ScopeType{get;set;} public string? CustomDeptIds{get;set;} }
```
```csharp
// IDataScoped.cs
public interface IDataScoped { string? Creator { get; } Guid? DeptId { get; } }
```
```csharp
// PermissionAggregator.FillDataScopesAsync
private async Task FillDataScopesAsync(UserPermissionContext ctx, List<int> roleIds)
{
    var rows = await _db.Sys_RoleDataScopes.Where(ds => roleIds.Contains(ds.RoleId)).ToListAsync();
    ctx.DataScopes = rows.GroupBy(d => d.ResourceKey).ToDictionary(g => g.Key, g => g.Max(x => x.ScopeType));
    ctx.CustomDeptIds = rows.Where(d => d.ScopeType == 4).GroupBy(d => d.ResourceKey)
        .ToDictionary(g => g.Key, g => g.SelectMany(x => ParseGuids(x.CustomDeptIds)).Distinct().ToList());
}
```
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(pub): Sys_RoleDataScope + IDataScoped + datascope aggregation (ch03 §3/§6)"`

## Task C-2: IDataScopeFilter 查询注入（五范围 + 子树，章03 §5）★

**Files:** Create `IDataScopeFilter.cs`/`DataScopeFilter.cs`, `DataScopeRegistry.cs`; Test `DataScopeFilterTests.cs`

- [ ] **Step 1: 失败测试**（1本人=Creator==user；2本部门=DeptId==ctx.DeptId；3及下级=记录部门 Path 以 ctx.DeptPath 为前缀；4自定义=DeptId in ids；5全部=不过滤）`[InMemory 可测逻辑，真库 §C-4 兜底]`

```csharp
[Fact]
public void Apply_Subtree_FiltersByPathPrefix()
{
    using var db = Db();
    var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid();
    db.Sys_Depts.AddRange(new Sys_Dept{Id=d1,DeptCode="A",DeptName="A",Path=$"/{d1}/"},
                          new Sys_Dept{Id=d2,DeptCode="A1",DeptName="A1",Path=$"/{d1}/{d2}/"});
    db.SaveChanges();
    var orders = new[]{ new FakeOrder{DeptId=d2}, new FakeOrder{DeptId=Guid.NewGuid()} }.AsQueryable();
    var ctx = new UserPermissionContext{ DeptPath=$"/{d1}/", DataScopes={["order"]=3} };
    var f = new DataScopeFilter(db);
    var r = f.Apply(orders, "order", ctx).ToList();
    Assert.Single(r);   // 只 d2（在 A 子树内）
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照章03 §5 switch；3 及下级用 `_db.Sys_Depts.Any(d => d.Id==x.DeptId && d.Path.StartsWith(ctx.DeptPath))`；DataScopeRegistry.Register(resource, type, supports, default)）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): IDataScopeFilter query injection (5 scopes + subtree) (ch03 §5)"`

## Task C-3: 数据权限配置 UI + 资源注册（章03 §7/§8）

**Files:** Modify `RolePermController`/`RolePermService`; Create `src/views/pub/role/DataScopeTab.vue`

- [ ] **Step 1-3: 实现**——`/api/pub/data-scope/resources` GET（注册表）、`/{roleId}` GET/PUT（upsert + 校验 ScopeType ∈ supports E-PUB-031 / 自定义须选部门 E-PUB-032 + InvalidateByRole）；前端资源列表 + 范围下拉（仅 supports）+ 自定义部门树多选。提交。

## Task C-4: 数据权限真库集成测（`[需真库]`）

**Files:** Create `CP6.Tests/DataScopeSqlIntegrationTests.cs`（SQLite/LocalDB）

- [ ] **Step 1-2: 验证 `Path.StartsWith` 子树过滤的真实 SQL 翻译 + 一条 service 端到端注入** → 提交

---

# Phase D — 章04 字段权限（序列化掩码）→ 四粒度齐

## Task D-1: Sys_RoleFieldPerm + 聚合 + FieldRegistry（章04 §3/§6/§7）

**Files:** Create `Sys_RoleFieldPerm.cs`, `FieldRegistry.cs`; Modify `CP6Context.cs`, `PermissionAggregator.cs`(`FillFieldPermsAsync`); migration; Test

- [ ] **Step 1: 失败测试**（FillFieldPermsAsync：多角色同字段取 MIN Access=最可见）
- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
[Table("Sys_RoleFieldPerm")]
public class Sys_RoleFieldPerm : BaseEntity { public int RoleId{get;set;} public string ResourceKey{get;set;}=""; public string FieldName{get;set;}=""; public int Access{get;set;} }
```
```csharp
private async Task FillFieldPermsAsync(UserPermissionContext ctx, List<int> roleIds)
{
    ctx.FieldPerms = (await _db.Sys_RoleFieldPerms.Where(fp => roleIds.Contains(fp.RoleId)).ToListAsync())
        .GroupBy(fp => fp.ResourceKey)
        .ToDictionary(g => g.Key, g => g.GroupBy(x => x.FieldName).ToDictionary(fg => fg.Key, fg => fg.Min(x => x.Access)));
}
```
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(pub): Sys_RoleFieldPerm + field perm aggregation (widest=MIN) (ch04 §3/§6)"`

## Task D-2: IFieldPermService（MaskHidden 反射置空 + StripReadOnly）+ [FieldMask]（章04 §4/§5）★

**Files:** Create `IFieldPermService.cs`/`FieldPermService.cs`, `CP6.Core/Auth/FieldMaskAttribute.cs`; Test `FieldPermServiceTests.cs`

- [ ] **Step 1: 失败测试**（MaskHidden：Access=3 字段反射置 null（单对象 + 集合每项）；Access=1/2 不动；StripReadOnly：Access=2 用原值覆盖）

```csharp
[Fact]
public void MaskHidden_NullsAccess3_OnObjectAndList()
{
    var ctx = new UserPermissionContext{ FieldPerms = { ["order"] = new(){ ["Cost"]=3, ["Price"]=1 } } };
    var svc = new FieldPermService(StubCurrent(ctx));
    var dto = new OrderDto{ Cost=100, Price=50 };
    svc.MaskHidden(dto, "order");
    Assert.Null(dto.Cost); Assert.Equal(50, dto.Price);
    var list = new List<OrderDto>{ new(){Cost=9} };
    svc.MaskHidden(list, "order"); Assert.Null(list[0].Cost);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（MaskHidden 反射：`AsEnumerable` 把单对象/集合统一，Access==3 的属性 `SetValue(null)`，可空值类型/引用类型置 null，值类型置 default 或脱敏；StripReadOnly Access==2 用 DB 原值覆盖；`FieldMaskAttribute : IAsyncResultFilter` 照章04 §4）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pub): field mask (serialization) + readonly strip-write (ch04 §4/§5)"`

## Task D-3: 字段权限配置 UI + my-readonly + 三权合一冒烟（章04 §8 + §13）

**Files:** Modify `RolePermController`/`RolePermService`; Create `src/views/pub/role/FieldPermTab.vue`; DI 全装配

- [ ] **Step 1: 实现**——`/api/pub/field-perm/fields/{resourceKey}` GET（字段注册表）、`/{roleId}` GET/PUT（upsert，可读=1 删记录回默认，校验 ∈ 注册表 E-PUB-041，InvalidateByRole）、`/my-readonly/{resourceKey}` GET；前端字段列表 + 访问级单选。
- [ ] **Step 2: DI 全装配 + 三权合一集成测**——一个测试 Controller 同时贴 `[Authorize][RequirePermission("order","query")][FieldMask("order")]` + service 用 `IDataScopeFilter`：验证①无操作权 403 ②有操作权但数据范围限本部门只返本部门行 ③隐藏字段为 null。这条即**四粒度齐**验证。
- [ ] **Step 3: 全量构建 + 全测 + 提交**

```bash
dotnet build && dotnet test CP6.Tests
git commit -m "feat(pub): field perm UI + three-power integration (4-granularity complete) (ch04 §13)"
```

---

## Self-Review（对照章01~04 覆盖）

- **章01**：Sys_UserRole 多角色(A-1) ✅ / UserPermissionContext + 聚合框架(A-2) ✅ / 合并口径并集·最宽·最宽(A-2/B-2/C-1/D-1) ✅ / 缓存+失效(A-3) ✅ / 分配+主角色+迁移 UI(A-4) ✅ / 权限并集求解(A-2 GetAllRoleIds) ✅
- **章02**：MenuAction/RoleAction(B-1) ✅ / 资源键 MenuKey:action(A-1 补 MenuKey + B-2) ✅ / [RequirePermission] 403(B-3) ✅ / IPermissionService(B-2) ✅ / v-permission + 授权 UI(B-4) ✅ / ActionCode 字典(B-4 操作点维护) ✅
- **章03**：Sys_RoleDataScope(C-1) ✅ / IDataScoped(C-1) ✅ / 五范围 + 子树注入(C-2) ✅ / 最宽聚合 + 自定义并集(C-1) ✅ / 资源注册(C-2/C-3) ✅ / 配置 UI(C-3) ✅ / ctx 扩展 UserName/DeptId/DeptPath(A-2/C-1) ✅
- **章04**：Sys_RoleFieldPerm(D-1) ✅ / 序列化掩码 MaskHidden(D-2) ✅ / 只读拒写 StripReadOnly(D-2) ✅ / 最宽=MIN 聚合(D-1) ✅ / 字段注册(D-1) ✅ / [FieldMask](D-2) ✅ / 配置 UI(D-3) ✅ / 三权合一(D-3 集成测) ✅

**已知缺口/推迟（已标注）：**
1. **TenantId**（B1-D3）—— 章09 统一给 Sys 全族。
2. **现有菜单 MenuKey 回填**（B1-D2）—— 需迁移/种子给现有 Sys_Menu 行补 MenuKey（A-1 留注，建议种子脚本）。
3. **业务实体接 IDataScoped**（章03 §4）—— 各业务模块自己补 DeptId + 实现接口（属业务模块改造，B3 阶段），本计划只立框架 + 测试用 FakeOrder。
4. **脱敏规则**（章04 §4 手机号打码）—— v1 置 null，脱敏规则留扩展。
5. **真库集成测**（C-4）—— Path 子树 SQL 翻译 + RowVersion 同 Space D-9。

**Type 一致性：** `UserPermissionContext`(A-2) 字段被 B/C/D 的 Fill*/校验点一致消费；`int RoleId/MenuId`(B1-D1) 贯穿全部新表 + 聚合 join；`ICurrentPermissionContext.GetAsync`(A-3) 被 PermissionService/FieldPermService/DataScope 调用方一致用；`ActionKeys` 键 `menuKey:action`(A-2/B-2/B-3) 三处同套。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-pub-b1-permission.md`。**PUB 第二份（B1 痛点核心）**。后续：
- PUB Plan 3 = `2026-06-13-pub-common-modules.md`（章05~09 公共模组：字典/采番纳管 + 附件 + 导入导出 + codegen + 集成）

**下一步按工作流是你修订**（尤其 B1-D1 int 键、B1-D2 MenuKey、B1-D3 TenantId 与 B0 一并拍板）。定稿后执行：B0 → **B1** → 公共模组；B1 内 Phase A→B→C→D 顺序（A 是 B/C/D 的聚合地基）。

---

*初稿生成于 2026-06-13。源：docs/pub/01·02·03·04。已勘察 CP6 真实代码：Sys_Role/Sys_Menu int 自定义键（非 GUID，与 spec 不符 → B1-D1 对账）、Sys_RoleMenu(int)、Sys_Menu 无 MenuKey（→ B1-D2 新增）、Sys_User.RoleId int、BaseEntity 审计字段、零多租户、CacheService/JWT 现成、xUnit+InMemory。*
