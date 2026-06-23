# 字段级审计回放（Field Audit）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `CP6Context.SaveChanges` 写入管道叠加**字段级 before/after 变更史**（who/when/what 全要素），补足现有"请求级操作日志 + 行级审计元字段"缺失的**数据变更前后值留痕 + 时间线回放**。标记式 opt-in（`IAuditable`）+ 字段 `[AuditIgnore]`/密钥拒名单 + 每变更一行 JSON diff + 两阶段原子落库 + 查询/回放 UI，全栈 + 五语 + gstack QA。

**Architecture:** 方案 = ①新实体 `Sys_FieldAuditLog : BaseTenantEntity`（自动纳入多租户过滤/盖章/索引遍历）；②空标记接口 `IAuditable`（实体 opt-in）+ `[AuditIgnore]`（字段排除）；③在 `CP6Context` 两个 `SaveChanges` 重写内（与 `StampTenant` 同处）遍历 `ChangeTracker.Entries<IAuditable>()` 捕获标量列 before/after，**两阶段事务**落库（业务行先存→键落定→审计行后存→同事务 Commit，relational 原子，InMemory 降级）；④密钥三重防护（`[AuditIgnore]` + 内建拒名单 + 跳过全部主键/TenantId/元字段）；⑤同步注入 `ICurrentUserAccessor`（读 JWT claims）记操作人；⑥`RoleController.Update`/`MenuController.Update` 由 attach-as-Modified 改"先查后改"令 diff 准确；⑦`FieldAuditController` 只读查询 + 时间线回放 + 权限点 + 五语 + 前端。命名空间 **Sys**，**无新错误码**（只读模块）。所有约束对齐 spec §1 现状锚点。

**Tech Stack:** .NET 8 + EF Core 8（`ChangeTracker`/`IProperty.PropertyInfo`/`Database.IsRelational()`/`BeginTransaction`）/ `System.Text.Json`（diff 序列化）/ `IHttpContextAccessor`（JWT claims）/ xUnit + EF InMemory / Vue 3.5 + element-plus + vue-i18n + axios。源 spec：`docs/superpowers/specs/2026-06-22-field-audit-design.md`（定稿 `17b72a5` + R8 `4a20a60`）。

---

## 关键既有约定（落码前必读，复核行号可能微移）

- **写入管道** `CP6.Core/EFDbContext/CP6Context.cs`：私有 `StampTenant()`（L1936–1945）遍历 `ChangeTracker.Entries<BaseTenantEntity>()` + 显式补盖 `Sys_OperLog`。重写 `SaveChanges(bool acceptAllChangesOnSuccess)`（L1947）/ `SaveChangesAsync(bool, CancellationToken)`（L1953）各先 `StampTenant()` 再转 `base`；**无参版未重写**（经 base 路由至 bool 重载，不重复盖章）。**字段审计捕获挂这两个重写**。当前全文**无任何 `BeginTransaction`/`CurrentTransaction`、SaveChanges 重写内除 `StampTenant` 外无副作用** → 两阶段事务为安全新增。
- **构造** `CP6Context(DbContextOptions<CP6Context> options, ITenantContext? tenant = null)`（L29，仅此一构造）；`CurrentTenantId => _tenant?.CurrentTenantId ?? TenantContext.DefaultTenant`（L35）。**加可选第三参** `ICurrentUserAccessor? user = null`——EF Core 自动构造注入（`Program.cs:41` 用 `AddDbContext`**非** `AddDbContextPool`，`ITenantContext` 即此机制注入；**不改 `AddDbContext` 注册**）。
- **多租户反射批量**（`OnModelCreating` L1877–1885 全局过滤 + L1894–1931 唯一索引补 TenantId 前缀）：对所有 `BaseTenantEntity`（`t.BaseType is null`）批量注册。`Sys_FieldAuditLog : BaseTenantEntity` + `DbSet` 即**自动**纳入过滤/盖章；其**非唯一**查询索引手注册于 `OnModelCreating`（仿 `Sys_OperLog` 块 L1865–1871）。
- **实体基类** `CP6.Entity/BaseEntity.cs`：`Id` 为 `Guid` `[DatabaseGenerated(Identity)]`（store-generated，Added 存前为 `Guid.Empty`/临时值，**存后**才落真值 → 审计 EntityKey 须存后取，两阶段不可省）；元字段 `Creator/CreateDate/Modifier/ModifyDate`。`BaseTenantEntity : BaseEntity` 加 `Guid TenantId`。
- **键形不一（R8）**：首批 9 实体为 `Guid Id`；**`Sys_Role`(int RoleId)/`Sys_Menu`(int MenuId) `[DatabaseGenerated(None)]`、不继承 BaseEntity、无 `.Id`/无 `TenantId`/无 `Creator/Modifier`（仅 `CreateDate`）**。故 EntityKey **勿写死 `.Id`**，经 `FindPrimaryKey().Properties` 提取（`|` 连接复合键）。
- **更新模式**：
  - `RoleController.Update`（L66–71）/ `MenuController.Update`（L46–51）= `_context.Entry(entity).State = Modified`（attach-as-Modified 断连 → `OriginalValues==CurrentValues` → diff 全落空）。**T4 改为先查后改**（仿 `UserController.Update` L77–105：`FindAsync` → 逐属性赋值 → 保 `CreateDate` → SaveChanges）。
  - `RepositoryBase.UpdateAsync`（`CP6.Core/BaseProvider/RepositoryBase.cs`）= 断连，广泛复用，**不动**（§8 局限）；Fin/Pur/Erp 主数据走 `BaseCrudService.UpdateAsync` 先查后改（diff 准确，无需改）。
- **分页范本** `SecurityLogController`（`api/sys/security-log`，`[Authorize]` 类级 + `[RequirePermission("sys-security-log","query")]` 方法级；`page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,200);`，`.OrderByDescending`，返 `Ok(new { rows, total })`）。测试 `SecurityLogControllerTests`：**直 `new SecurityLogController(db)` 调方法**（绕 `[RequirePermission]` 过滤器，不测 403），`Unwrap` 反射读匿名 `total`/`rows`。
- **权限点 seed 范本**（Program.cs L1031–1057）：`if(!db.Sys_Menus.Any(m=>m.MenuId==114)){ Add Sys_Menu{114,...,ParentId=100,...} + Sys_RoleMenu{RoleId=1,MenuId=114}; }` → MenuKey 本地回填 `menu.MenuKey = RoutePath.Trim('/').Replace('/','-')`（"/sys/security-log"→`sys-security-log`）→ `Sys_MenuAction{114,"query","查看",Sort=0}` + `Sys_RoleAction{RoleId=1,114,"query"}`。**MenuId=114 已被安全日志占 → 用 115**；父菜单 100"系统管理"已存在。`[RequirePermission(menuKey,action)]` 的 menuKey = 派生值（`sys-field-audit`）。
- **i18n seed** `I18nSecScreenSeed`（`CP6.WebApi/Seed/`，`public static readonly Sys_Lang[] Items`，词条 `new Sys_Lang{ LangKey=…, ZhCN=…, ZhTW=…, En=…, Ja=…, Ko=… }`，Program.cs L1559 经 `.Concat(...I18nSecScreenSeed.Items)` 接链）。
- **测试基建** `TestHelper.CreateInMemoryContext()` = `new CP6Context(options)`（单参 → 加第三参后仍兼容）。捕获测试需注入假 `ICurrentUserAccessor` → **T2 加重载** `CreateInMemoryContext(ICurrentUserAccessor? user, ITenantContext? tenant = null)`。
- **前端** `cp6.web/src`：`views/pms/OperLogView.vue`/`SecurityLogView.vue`（列表+筛选+分页范本）；`router/index.ts` 的 `viewModules`（加一行注册路由）；`api/sys/securityLog.ts`（含类型范本）。

### 关键类型签名（跨 Task 一致，勿改名）
```csharp
// CP6.Entity
public interface IAuditable { }                              // 空标记，实体 opt-in
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute { }      // 字段排除

public class Sys_FieldAuditLog : BaseTenantEntity {
    [MaxLength(100)][Required] public string EntityName { get; set; } = "";   // CLR 类型名
    [MaxLength(128)][Required] public string EntityKey  { get; set; } = "";   // 主键串(键形无关,|连接复合键)
    public int Operation { get; set; }                        // 1=Added 2=Modified 3=Deleted
    public string Changes { get; set; } = "[]";               // [{field,old,new}] JSON (nvarchar max)
    public Guid? UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    public DateTime ChangedAt { get; set; }
}

// CP6.Core/Services/Sys
public interface ICurrentUserAccessor { Guid? UserId { get; } string? UserName { get; } }

// CP6.Core/EFDbContext (CP6Context 内部)
internal sealed record FieldChange(string Field, string? Old, string? New);
private sealed record PendingAudit(EntityEntry Entry, string EntityName, int Operation,
                                   List<FieldChange> Changes, string KeyBeforeSave, Guid TenantId);
```

### 首批纳管清单（spec §2.2，均已核验存在）
- **opt-in 11 实体（实现 `IAuditable`）**：`Sys_User`、`Sys_Role`、`Sys_UserRole`、`Sys_RoleAction`、`Sys_RoleDataScope`、`Sys_RoleFieldPerm`、`Sys_Menu`、`Sys_Tenant`、`GlAccount`、`SupplierPrice`、`BusinessPartner`。
- **`[AuditIgnore]` 首批仅**：`Sys_User.Password`（#2/#3 字段随其落码后补标；**即使漏标也安全**——拒名单兜底）。
- **不硬依赖 #2/#3**：`Sys_TenantSsoConfig`（#3）/ `Sys_User.TwoFactorSecret`（#2）当前不存在，列"落地后补标"。

---

## Tasks 总览

| T | 范围 | 依赖 | 提交 |
|---|---|---|---|
| T1 | `Sys_FieldAuditLog` 实体 + `IAuditable`/`[AuditIgnore]` + DbSet + 3 索引 + 标注 11 实体 + `Password [AuditIgnore]` + EF 迁移 | — | 1 |
| T2 | `ICurrentUserAccessor` + 实现 + DI + `CP6Context` 第三参 + TestHelper 重载 | — | 1 |
| T3 | **捕获核心**：拒名单/跳过集/`ExtractKey`/`BuildChanges`/`ResolveAuditTenant`/两阶段原子 `SaveChanges(Async)` + 全套单测 | T1,T2 | 1 |
| T4 | R2 改造：`RoleController.Update`/`MenuController.Update` 先查后改 + 回归测试 | T1,T3 | 1 |
| T5 | `FieldAuditController`（列表 `changeCount` 投影 + 时间线 record）+ 权限点 seed（菜单 115）+ 控制器测试 | T1,T3 | 1 |
| T6 | i18n `I18nSecAuditScreenSeed` 五语 + Program.cs `.Concat` | T5 | 1 |
| T7 | 前端 `FieldAuditView.vue`（列表+筛选+时间线抽屉）+ api/types + router | T5,T6 | 1 |
| T8 | gstack 真浏览器 QA 全流程 | 全部 | — |

> 每 Task 先绿色构建 + 全量测试再本地 `git commit`（**不 push**，push 由用户监督）。subagent-driven：每 Task 派 subagent → spec审 + 质量审双过 → 先绿后 commit。基线现 **996 测**（首 Task `dotnet test` 核对）。

---

## Task T1：实体 + 标记 + DbSet + 索引 + 标注 + 迁移

**Files:** Create `CP6.Entity/IAuditable.cs`、`CP6.Entity/AuditIgnoreAttribute.cs`、`CP6.Entity/DomainModels/Sys/Sys_FieldAuditLog.cs`；Modify `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 索引）、11 实体类（加 `IAuditable`）、`Sys_User.cs`（`Password` 加 `[AuditIgnore]`）；Create EF 迁移。**本 Task 仅建模型，无捕获行为**（标记此刻是惰性的，无回归风险）。

- [ ] **Step 1: 标记 + 特性** `IAuditable.cs`/`AuditIgnoreAttribute.cs`（粘"关键类型签名"，`namespace CP6.Entity;`）。
- [ ] **Step 2: 实体** `Sys_FieldAuditLog.cs`（`namespace CP6.Entity.DomainModels.Sys;`，`: BaseTenantEntity`，粘签名 + XML 注释，标 `[MaxLength]/[Required]`）。
- [ ] **Step 3: DbSet + 索引**（CP6Context）：
  - `public DbSet<Sys_FieldAuditLog> Sys_FieldAuditLogs { get; set; }`。
  - `OnModelCreating` 内（反射批量**之前**，仿 `Sys_OperLog` 块 L1865）手注册 3 个**非唯一**查询索引（`BaseTenantEntity` 全局过滤由反射批量自动覆盖，无需手写过滤）：
    ```csharp
    modelBuilder.Entity<Sys_FieldAuditLog>(e =>
    {
        e.HasIndex(x => new { x.EntityName, x.EntityKey, x.ChangedAt });   // 单记录时间线回放
        e.HasIndex(x => new { x.UserId, x.ChangedAt });                    // 按人审计
        e.HasIndex(x => new { x.EntityName, x.ChangedAt });                // 按实体类型
        e.Property(x => x.Changes).HasColumnType("nvarchar(max)");         // 大文本
    });
    ```
- [ ] **Step 4: 标注 11 实体**（grep 定位每个类声明，加 `IAuditable`）：
  - `Sys_User`/`Sys_UserRole`/`Sys_RoleAction`/`Sys_RoleDataScope`/`Sys_RoleFieldPerm`/`Sys_Tenant`/`GlAccount`/`SupplierPrice`/`BusinessPartner` → 现有基类后追加 `, IAuditable`（如 `: BaseTenantEntity, IAuditable`）。
  - **`Sys_Role`/`Sys_Menu` 无基类** → `public class Sys_Role : IAuditable`（注意 `using CP6.Entity;`）。
  - `Sys_User.Password` 属性加 `[AuditIgnore]`（`using CP6.Entity;`）。
- [ ] **Step 5: 迁移** `dotnet ef migrations add FieldAudit --project CP6.Core --startup-project CP6.WebApi`，**核对仅新增 `Sys_FieldAuditLogs` 表 + 3 索引，无其它表/列变更**（`IAuditable`/`[AuditIgnore]` 不映射列；Sys_FieldAuditLog 的 TenantId 唯一索引前缀不涉及——本表无唯一索引）。
- [ ] **Step 6: 构建 + 提交** `dotnet build CP6.WebApi` + `dotnet test CP6.Tests`（全绿，标记惰性不改行为）。
```bash
git add -A && git commit -m "feat(sec): T1(字段审计) Sys_FieldAuditLog实体+IAuditable/[AuditIgnore]标记+DbSet+3索引+标注11实体+Password忽略+迁移
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T2：ICurrentUserAccessor + DI + CP6Context 第三参

**Files:** Create `CP6.Core/Services/Sys/ICurrentUserAccessor.cs`、`CurrentUserAccessor.cs`；Modify `CP6.Core/EFDbContext/CP6Context.cs`（构造第三参 + 私有字段）、`CP6.WebApi/Program.cs`（DI）、`CP6.Tests/TestHelper.cs`（重载）；Test `CP6.Tests/Sys/CurrentUserAccessorTests.cs`

- [ ] **Step 1: 失败测试** `CurrentUserAccessorTests.cs`（构造假 `IHttpContextAccessor` + `ClaimsPrincipal`）：
  - 有 `NameIdentifier`(Guid)+`Name` → `UserId`/`UserName` 落；
  - 无 `HttpContext`（`HttpContext=null`）→ 两者 null；
  - `NameIdentifier` 非 Guid → `UserId==null`。
  ```csharp
  using System.Security.Claims;
  using CP6.Core.Services.Sys;
  using Microsoft.AspNetCore.Http;
  using Xunit;
  namespace CP6.Tests.Sys;
  public class CurrentUserAccessorTests
  {
      private static ICurrentUserAccessor Make(ClaimsPrincipal? user)
      {
          var ctx = user == null ? null : new DefaultHttpContext { User = user };
          return new CurrentUserAccessor(new HttpContextAccessor { HttpContext = ctx });
      }
      [Fact] public void Reads_claims_when_present()
      {
          var id = Guid.NewGuid();
          var p = new ClaimsPrincipal(new ClaimsIdentity(new[]{
              new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "alice") }));
          var a = Make(p); Assert.Equal(id, a.UserId); Assert.Equal("alice", a.UserName);
      }
      [Fact] public void Null_when_no_httpcontext()
      { var a = Make(null); Assert.Null(a.UserId); Assert.Null(a.UserName); }
  }
  ```
  Run `dotnet test CP6.Tests --filter CurrentUserAccessorTests`，Expected 编译失败。
- [ ] **Step 2: 实现** `ICurrentUserAccessor.cs`（签名）；`CurrentUserAccessor.cs`：
  ```csharp
  using System.Security.Claims;
  using Microsoft.AspNetCore.Http;
  namespace CP6.Core.Services.Sys;
  public class CurrentUserAccessor : ICurrentUserAccessor
  {
      private readonly IHttpContextAccessor _http;
      public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;
      public Guid? UserId =>
          Guid.TryParse(_http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;
      public string? UserName => _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
  }
  ```
  （`CP6.Core` 已引 `Microsoft.AspNetCore.Http.Abstractions`？若否，`IHttpContextAccessor` 来自 `Microsoft.AspNetCore.Http`——`CP6.Core` 既有 JWT/认证服务已用，落码前 grep 核引用。）Run filter PASS。
- [ ] **Step 3: CP6Context 第三参**（构造 L29 加可选第三参，私有字段）：
  ```csharp
  private readonly ICurrentUserAccessor? _user;
  public CP6Context(DbContextOptions<CP6Context> options, ITenantContext? tenant = null, ICurrentUserAccessor? user = null) : base(options)
  { _tenant = tenant; _user = user; }
  ```
  （向后兼容：现有单/双参调用全部不变；EF Core 自动构造注入，**不改 `AddDbContext`**。）
- [ ] **Step 4: DI**（Program.cs，认证服务区）：`builder.Services.AddHttpContextAccessor();`（幂等，核对是否已注册）+ `builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();`。
- [ ] **Step 5: TestHelper 重载**（捕获测试注入假用户用）：
  ```csharp
  public static CP6Context CreateInMemoryContext(ICurrentUserAccessor? user, ITenantContext? tenant = null)
  {
      var options = new DbContextOptionsBuilder<CP6Context>()
          .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
      return new CP6Context(options, tenant, user);
  }
  // 测试假桩：
  // sealed class FakeUser(Guid? id, string? name) : ICurrentUserAccessor { public Guid? UserId => id; public string? UserName => name; }
  ```
- [ ] **Step 6: 全量 + 提交** `dotnet build CP6.WebApi` + `dotnet test CP6.Tests` 全绿。
```bash
git add -A && git commit -m "feat(sec): T2(字段审计) ICurrentUserAccessor(读JWT claims)+DI自动构造注入+CP6Context第三参+TestHelper重载
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T3：捕获核心 + 两阶段原子落库（heart）

**Files:** Modify `CP6.Core/EFDbContext/CP6Context.cs`（捕获助手 + 两阶段重写）；Test `CP6.Tests/Sys/FieldAuditCaptureTests.cs`、`FieldAuditPureTests.cs`

> **TDD：先写 Step 1 测试看红 → Step 2/3 实现转绿。** 捕获测试用 `TestHelper.CreateInMemoryContext(fakeUser)` 直构上下文操作（load→modify→save 天然先查后改 → int 键 Modified 在单测可验；控制器断连问题由 T4 解决）。InMemory 无事务 → 仅验"键落定 + 审计行随业务行同周期写入"，**真回滚仅 relational 可验**（§8，不在单测）。

- [ ] **Step 1: 失败测试**
  `FieldAuditPureTests.cs`（纯函数，无 DB）：
  - 拒名单 `IsSecretField`：`Password`/`xxSecret`/`xxHash`/`TokenHash`/`Salt`/`ClientSecretProtected`/`TwoFactorSecret` 大小写各形 → true；`Email`/`UserName` → false。
  - 值化：null→null；超 1000 字符 → 截断；`decimal`/`DateTime` 走 `InvariantCulture`（小数点 `.`，与区域无关）。
  `FieldAuditCaptureTests.cs`（InMemory + 假用户）：
  ```csharp
  [Fact] Modified_marked_entity_writes_one_row_with_only_changed_fields();   // GlAccount 改名→Op=2,Changes 仅含改的列 old/new 正确
  [Fact] Added_marked_entity_writes_op1_with_resolved_guid_key();            // Guid 实体 Added→Op=1,EntityKey==落定真 Id 串(非 Guid.Empty)
  [Fact] Deleted_marked_entity_writes_op3_with_key_before_save();            // Deleted→Op=3,EntityKey==删除前键
  [Fact] Int_key_entity_added_and_modified_uses_pk_value();                  // Sys_Menu(int) Added/Modified→EntityKey==MenuId 串(非"Id"/非空)  R8
  [Fact] Non_auditable_entity_change_writes_no_row();                        // 改未标 IAuditable 实体(如 Sys_Lang)→无审计行
  [Fact] Password_excluded_by_AuditIgnore();                                 // 改 Sys_User.Password→Changes 不含 password
  [Fact] Secret_field_excluded_by_denylist_even_without_attribute();         // 纯函数已覆盖；此处验 BuildChanges 跳过(合成实体或确认 Password 双命中)
  [Fact] Only_meta_field_change_writes_no_row();                            // 仅改 Modifier/ModifyDate→空改不记
  [Fact] PrimaryKey_and_TenantId_not_in_diff();                            // diff 不含 Id/RoleId/TenantId
  [Fact] User_attribution_filled_and_null_for_background();                 // 注入用户→UserId/UserName 落;注入 null→null
  [Fact] Audit_row_tenant_mirrors_business_entity();                       // 双租户:BaseTenantEntity 审计行 TenantId==该实体 TenantId  R4
  ```
  Run `dotnet test CP6.Tests --filter FieldAudit`，Expected 编译/断言失败。
- [ ] **Step 2: 捕获助手**（CP6Context 私有，**勿写死 `Id`**）：
  ```csharp
  private static readonly string[] _metaSkip = { "Creator","CreateDate","Modifier","ModifyDate" };
  private static bool IsSecretField(string name)
  {
      var n = name.ToLowerInvariant();
      return n == "password" || n.EndsWith("secret") || n.EndsWith("hash")
          || n == "tokenhash" || n == "salt" || n == "clientsecretprotected" || n == "twofactorsecret";
  }
  private static string? Stringify(object? v)
  {
      if (v == null) return null;
      var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
      return s.Length > 1000 ? s[..1000] : s;
  }
  private static string ExtractKey(EntityEntry e)
  {
      var pk = e.Metadata.FindPrimaryKey();
      if (pk == null) return "";
      return string.Join("|", pk.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString() ?? ""));
  }
  private List<FieldChange> BuildChanges(EntityEntry e)
  {
      var pkNames = e.Metadata.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet() ?? new();
      var list = new List<FieldChange>();
      foreach (var p in e.Properties)
      {
          var name = p.Metadata.Name;
          if (pkNames.Contains(name)) continue;                                   // 全部主键(Guid Id/int RoleId/MenuId)
          if (name == "TenantId" || _metaSkip.Contains(name)) continue;           // 租户 + 行级元字段
          if (p.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() != null) continue;  // [AuditIgnore]
          if (IsSecretField(name)) continue;                                      // 拒名单兜底
          switch (e.State)
          {
              case EntityState.Added:    list.Add(new(name, null, Stringify(p.CurrentValue))); break;
              case EntityState.Deleted:  list.Add(new(name, Stringify(p.OriginalValue), null)); break;
              case EntityState.Modified:
                  if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                      list.Add(new(name, Stringify(p.OriginalValue), Stringify(p.CurrentValue)));
                  break;
          }
      }
      return list;
  }
  private static int MapOp(EntityState s) => s == EntityState.Added ? 1 : s == EntityState.Deleted ? 3 : 2;
  ```
  （`using System.Reflection;` for `GetCustomAttribute`；`using Microsoft.EntityFrameworkCore.ChangeTracking;` for `EntityEntry`。）
- [ ] **Step 3: 两阶段重写**（CP6Context，捕获 + 原子落库）：
  ```csharp
  private List<PendingAudit> CaptureFieldAuditBeforeSave()
  {
      var list = new List<PendingAudit>();
      foreach (var e in ChangeTracker.Entries<IAuditable>())   // 触发 DetectChanges(AutoDetectChangesEnabled 默认 true)
      {
          if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
          var changes = BuildChanges(e);
          if (e.State == EntityState.Modified && changes.Count == 0) continue;     // 空改不记
          var tenant = e.Entity is BaseTenantEntity bt ? bt.TenantId : CurrentTenantId;  // 业务实体已 StampTenant;共享表回退
          list.Add(new PendingAudit(e, e.Metadata.ClrType.Name, MapOp(e.State), changes, ExtractKey(e), tenant));
      }
      return list;
  }
  private void WriteAuditRows(List<PendingAudit> pending)
  {
      foreach (var pa in pending)
      {
          var key = pa.Operation == 1 ? ExtractKey(pa.Entry) /*存后真值,仍 tracked*/ : pa.KeyBeforeSave;  // Modified/Deleted 用存前键(Deleted 已 Detached)
          Sys_FieldAuditLogs.Add(new Sys_FieldAuditLog {
              EntityName = pa.EntityName, EntityKey = key, Operation = pa.Operation,
              Changes = System.Text.Json.JsonSerializer.Serialize(pa.Changes),
              UserId = _user?.UserId, UserName = _user?.UserName,
              ChangedAt = DateTime.Now, TenantId = pa.TenantId        // step6 不经 StampTenant→显式设
          });
      }
  }
  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
      StampTenant();
      var pending = CaptureFieldAuditBeforeSave();
      if (pending.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);   // 零开销
      var useTx = Database.IsRelational() && Database.CurrentTransaction == null;   // InMemory 不开;有环境事务则参与
      var tx = useTx ? Database.BeginTransaction() : null;
      try
      {
          var result = base.SaveChanges(acceptAllChangesOnSuccess);   // 业务变更(Added 键落定)
          WriteAuditRows(pending);
          base.SaveChanges(acceptAllChangesOnSuccess: true);          // 审计行(非虚分发,不重入;审计行非 IAuditable)
          tx?.Commit();
          return result;                                              // 返业务影响行数(审计行不计入)
      }
      catch { tx?.Rollback(); throw; }
      finally { tx?.Dispose(); }
  }
  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
      StampTenant();
      var pending = CaptureFieldAuditBeforeSave();
      if (pending.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
      var useTx = Database.IsRelational() && Database.CurrentTransaction == null;
      var tx = useTx ? await Database.BeginTransactionAsync(cancellationToken) : null;
      try
      {
          var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
          WriteAuditRows(pending);
          await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
          if (tx != null) await tx.CommitAsync(cancellationToken);
          return result;
      }
      catch { if (tx != null) await tx.RollbackAsync(cancellationToken); throw; }
      finally { if (tx != null) await tx.DisposeAsync(); }
  }
  ```
  （`using Microsoft.EntityFrameworkCore;` 提供 `IsRelational()`/`BeginTransaction(Async)`；`Database.CurrentTransaction` 同命名空间。落码前确认 `CP6.Core` 已引 relational 包——SqlServer 已引，`IsRelational()` 可用且 InMemory 返 false。）Run `--filter FieldAudit` Expected PASS。
- [ ] **Step 4: 全量 + 提交** `dotnet build CP6.WebApi` + `dotnet test CP6.Tests` 全绿（重点：既有 996 测无回归——`StampTenant` 行为不变，仅 IAuditable 实体多落审计行）。
```bash
git add -A && git commit -m "feat(sec): T3(字段审计) 捕获核心(拒名单/跳过全主键TenantId元字段/ExtractKey键形无关/BuildChanges)+两阶段原子SaveChanges(Async)(relational事务/InMemory降级)+全套单测
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T4：RoleController/MenuController 先查后改（R2）

**Files:** Modify `CP6.WebApi/Controllers/Sys/RoleController.cs`（Update L66–71）、`MenuController.cs`（Update L46–51）；Test `CP6.Tests/Sys/FieldAuditR2RegressionTests.cs`

> attach-as-Modified（`Entry(entity).State=Modified`）令 `OriginalValues==CurrentValues` → diff 全落空。改"先查后改"令 `Sys_Role`/`Sys_Menu` 的 Modified 产生准确 diff（保 `CreateDate`，这两实体无 `Creator/Modifier`）。

- [ ] **Step 1: 失败测试** `FieldAuditR2RegressionTests.cs`（直 new 控制器 + InMemory + 假用户上下文）：
  - seed 一条 `Sys_Role`（先查后改语义下先存）→ 经 `RoleController.Update` 改 `RoleName` → 断言 ①返回 200、②生成一条 `Sys_FieldAuditLog{Operation=2}` 且 `Changes` 含 `RoleName` old→new、③`CreateDate` 未被覆盖。
  - `Sys_Menu` 同款（改 `MenuName`）。
  - （防回退：若仍 attach-as-Modified，diff 落空 → 无 Op=2 审计行 → 测试红。）
  Run `dotnet test CP6.Tests --filter FieldAuditR2Regression` Expected 失败（当前 attach-as-Modified）。
- [ ] **Step 2: 改造 RoleController.Update**（仿 `UserController.Update`）：
  ```csharp
  [HttpPut]
  [RequirePermission("role", "edit")]
  public async Task<IActionResult> Update([FromBody] Sys_Role entity)
  {
      var existing = await _context.Sys_Roles.FindAsync(entity.RoleId);
      if (existing == null) return NotFound();
      existing.RoleName = entity.RoleName;
      existing.Description = entity.Description;
      existing.Enable = entity.Enable;
      existing.OrderNo = entity.OrderNo;
      // CreateDate 不动(保留原值)
      await _context.SaveChangesAsync();
      return Ok(existing);
  }
  ```
- [ ] **Step 3: 改造 MenuController.Update**（同款，逐属性赋值 `MenuName/RoutePath/MenuKey/Icon/ParentId/OrderNo/Enable`，保 `CreateDate`；`FindAsync(entity.MenuId)`）。
- [ ] **Step 4: 转绿 + 全量 + 提交** Run filter PASS；`dotnet test CP6.Tests` 全绿（核对既有 Role/Menu 控制器测试——若有断言"更新整对象"语义无碍，先查后改语义等价）。
```bash
git add -A && git commit -m "feat(sec): T4(字段审计) RoleController/MenuController.Update 先查后改(替 attach-as-Modified)令 diff 准确+R2 回归测试
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T5：FieldAuditController + 权限点 seed

**Files:** Create `CP6.WebApi/Controllers/Sys/FieldAuditController.cs`；Modify `CP6.WebApi/Program.cs`（权限点 seed 块，仿 L1031–1057）；Test `CP6.Tests/Sys/FieldAuditControllerTests.cs`

- [ ] **Step 1: 失败测试** `FieldAuditControllerTests.cs`（仿 `SecurityLogControllerTests`，**直 `new FieldAuditController(db)` 调方法**，反射 `Unwrap` 读匿名 `total`/`rows`；`[RequirePermission]` 不在单测覆盖——移交 T8 gstack）：
  - 按 `entityName`/`userId`/日期段筛选；分页 + clamp（`page=0` 不抛，`pageSize` clamp 200）。
  - **列表 `rows` 项含 `changeCount` 不含完整 `changes`**（断言反射读不到 `changes` 属性、读得到 `changeCount` 且 == 该行 `Changes` JSON 反序列化长度）。
  - `record` 端点：同 `entityName`+`entityKey` 多条 → 按 `ChangedAt` **正序**返完整 `changes`（`[{field,old,new}]`）。
  Run `--filter FieldAuditController` Expected 编译失败。
- [ ] **Step 2: 控制器实现**：
  ```csharp
  [ApiController]
  [Route("api/sys/field-audit")]
  [Authorize]
  public class FieldAuditController : ControllerBase
  {
      private readonly CP6Context _db;
      public FieldAuditController(CP6Context db) => _db = db;

      [HttpGet]
      [RequirePermission("sys-field-audit", "query")]
      public async Task<IActionResult> GetList(
          [FromQuery] string? entityName, [FromQuery] string? entityKey, [FromQuery] Guid? userId,
          [FromQuery] DateTime? from, [FromQuery] DateTime? to,
          [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
      {
          page = Math.Max(1, page);
          pageSize = Math.Clamp(pageSize, 1, 200);
          var q = _db.Sys_FieldAuditLogs.AsQueryable();
          if (!string.IsNullOrWhiteSpace(entityName)) q = q.Where(x => x.EntityName == entityName);
          if (!string.IsNullOrWhiteSpace(entityKey))  q = q.Where(x => x.EntityKey == entityKey);
          if (userId.HasValue)                         q = q.Where(x => x.UserId == userId.Value);
          if (from.HasValue)                           q = q.Where(x => x.ChangedAt >= from.Value);
          if (to.HasValue)                             q = q.Where(x => x.ChangedAt < to.Value.AddDays(1));
          var total = await q.CountAsync();
          var raw = await q.OrderByDescending(x => x.ChangedAt)
              .Skip((page - 1) * pageSize).Take(pageSize)
              .Select(x => new { x.Id, x.EntityName, x.EntityKey, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
              .ToListAsync();
          // changeCount 摘要:不返完整 changes(防 200 行×大文本负载,评审 R7)。Count 在内存(JSON 反序列化)。
          var rows = raw.Select(x => new {
              x.Id, x.EntityName, x.EntityKey, x.Operation,
              changeCount = CountChanges(x.Changes),
              x.UserId, x.UserName, x.ChangedAt }).ToList();
          return Ok(new { rows, total });
      }

      [HttpGet("record")]
      [RequirePermission("sys-field-audit", "query")]
      public async Task<IActionResult> GetRecord([FromQuery] string entityName, [FromQuery] string entityKey)
      {
          var rows = await _db.Sys_FieldAuditLogs
              .Where(x => x.EntityName == entityName && x.EntityKey == entityKey)
              .OrderBy(x => x.ChangedAt)         // 时间线正序
              .Select(x => new { x.Id, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
              .ToListAsync();
          return Ok(new { rows });
      }

      private static int CountChanges(string json)
      {
          try { return System.Text.Json.JsonSerializer.Deserialize<List<FieldChangeDto>>(json)?.Count ?? 0; }
          catch { return 0; }
      }
      private sealed record FieldChangeDto(string field, string? old, string? @new);
  }
  ```
  （`entityName`/`entityKey` 经 EF 参数化，无注入；多租户全局过滤自动隔离本租户。注 `record` 端点返完整 `changes` 原 JSON 串供前端反序列化回放。）Run `--filter FieldAuditController` PASS。
- [ ] **Step 3: 权限点 seed**（Program.cs，仿安全日志块 L1031–1057；**MenuId=115**）：
  ```csharp
  if (!db.Sys_Menus.Any(m => m.MenuId == 115))
  {
      db.Sys_Menus.Add(new Sys_Menu { MenuId = 115, MenuName = "字段审计", RoutePath = "/sys/field-audit", Icon = "Document", ParentId = 100, OrderNo = 115, Enable = true });
      db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 115 });
      db.SaveChanges();
  }
  {
      var fa = db.Sys_Menus.FirstOrDefault(m => m.MenuId == 115);
      if (fa != null && string.IsNullOrEmpty(fa.MenuKey))
      { fa.MenuKey = fa.RoutePath!.Trim('/').Replace('/', '-'); db.SaveChanges(); }   // → sys-field-audit
      if (!db.Sys_MenuActions.Any(x => x.MenuId == 115 && x.ActionCode == "query"))
          db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 115, ActionCode = "query", ActionName = "查看", Sort = 0 });
      if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == 115 && x.ActionCode == "query"))
          db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 115, ActionCode = "query" });
      db.SaveChanges();
  }
  ```
  （核对 MenuId=115 未被占用；菜单驱动前端路由注册——无 Sys_Menu 则页面不可达。）
- [ ] **Step 4: 全量 + 提交** `dotnet build CP6.WebApi` + `dotnet test CP6.Tests` 全绿。
```bash
git add -A && git commit -m "feat(sec): T5(字段审计) FieldAuditController(列表changeCount投影+record时间线正序)+菜单115/sys-field-audit权限点seed授admin+控制器测试
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T6：i18n 五语

**Files:** Create `CP6.WebApi/Seed/I18nSecAuditScreenSeed.cs`；Modify `Program.cs`（i18n `.Concat`）

- [ ] **Step 1: seed**（仿 `I18nSecScreenSeed`，`public static readonly Sys_Lang[] Items`）：画面词条 `sec.audit.*` 五语：
  - `sec.audit.title`（字段审计）、`entityName`、`entityKey`、`operation`、`op.added`/`op.modified`/`op.deleted`（新增/修改/删除）、`operator`（操作人）、`changedAt`（变更时间）、`changeCount`（变更字段数）、`field`、`oldValue`/`newValue`（旧值/新值）、`viewTimeline`（查看时间线）、`timelineTitle`（变更时间线）、`noChanges`（无变更）、`filterEntity`/`filterUser`/`filterFrom`/`filterTo`。
  - （无错误码——只读模块；无新增 `sec.event.*`。）
- [ ] **Step 2: 接链** Program.cs i18n seed 链加 `.Concat(CP6.WebApi.Seed.I18nSecAuditScreenSeed.Items)`（仿 L1559）。
- [ ] **Step 3: 构建 + 提交** `dotnet build CP6.WebApi` + `dotnet test CP6.Tests` 全绿。
```bash
git add -A && git commit -m "feat(sec): T6(字段审计) sec.audit.* 画面词条五语seed(无错误码,只读模块)
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T7：前端

**Files:** Create `cp6.web/src/views/pms/FieldAuditView.vue`、`src/api/sys/fieldAudit.ts`、`src/types/sys/fieldAudit.ts`；Modify `src/router/index.ts`（viewModules 加一行）

- [ ] **Step 1: types + api** `types/sys/fieldAudit.ts`：`Operation` 枚举（1 Added/2 Modified/3 Deleted）、列表项（`id,entityName,entityKey,operation,changeCount,userId,userName,changedAt`）、`Change`（`field,old,new`）、时间线项。`api/sys/fieldAudit.ts`（仿 `securityLog.ts`）：`getList(params)` → `/api/sys/field-audit`、`getRecordTimeline(entityName, entityKey)` → `/api/sys/field-audit/record`。
- [ ] **Step 2: 视图** `FieldAuditView.vue`（系统设置内，仿 `SecurityLogView.vue`）：
  - 列表列：实体名 / 主键 / 操作（`op.added/modified/deleted` 标签着色）/ 操作人 / 时间 / **变更字段数 `changeCount`** / 操作（"查看时间线"）。
  - 筛选：实体名（下拉或文本）/ 操作人 / 日期段（from/to）+ 分页（`page`/`pageSize`）。
  - 行点"查看时间线" → 抽屉/对话框调 `getRecordTimeline(entityName, entityKey)`（返完整 `changes`），按 `ChangedAt` 正序逐次展示 `[{field, old→new}]`（回放 diff，旧值/新值并排）。
- [ ] **Step 3: 路由** `router/index.ts` 的 `viewModules` 加 `'/sys/field-audit': () => import('@/views/pms/FieldAuditView.vue')`。
- [ ] **Step 4: 前端校验** `cd cp6.web && npm run type-check && npx vitest run && npm run build`（全绿；`i18n:check` 留 T8 起后端 `i18n:pull` 重建快照后）。
```bash
git add -A && git commit -m "feat(sec): T7(字段审计) 前端FieldAuditView(列表+筛选+分页+时间线抽屉回放)+api/types+router
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T8：gstack 真浏览器 QA

> 本地后端 + 前端。新词须真起后端 + `npm run i18n:pull` 重建快照 + `i18n:gen-types` + `i18n:check` 绿。测试需 admin（有 `sys-field-audit:query`）+ 一个无该权限用户 + 2 租户（验隔离）。

- [ ] **Step 1: i18n 重建** 起后端 → `cd cp6.web && npm run i18n:pull` + `i18n:gen-types` + `i18n:check` 绿。
- [ ] **Step 2: 用户改字段留痕** 用户管理改某用户 `Email`/`RoleId` → 字段审计页见该变更行（`Operation=修改`，`changeCount` 正确）；点时间线见 `Email` 旧→新。
- [ ] **Step 3: R2 改造验证** 角色管理改 `RoleName` / 菜单管理改 `MenuName` → 字段审计页见 `Operation=修改` 审计行且含该字段 diff（验 T4 先查后改生效，非空 diff）。
- [ ] **Step 4: 增/删留痕** 新建一个角色 → `Operation=新增`（EntityKey=RoleId）；删除 → `Operation=删除`（EntityKey=删除前键）。
- [ ] **Step 5: 密钥护栏** 改某用户密码 → 字段审计该行 `changes` **不含** `Password`（值不泄露）。
- [ ] **Step 6: 时间线回放** 同一用户多次改字段 → record 端点按时间正序逐次回放 old→new。
- [ ] **Step 7: 权限 + 隔离** ①无 `sys-field-audit:query` 用户访问 → **403**（验 `[RequirePermission]`）；②A 租户登录仅见 A 租户审计行（多租户全局过滤目检）。
- [ ] **Step 8: 全量回归** `dotnet test CP6.Tests` + 前端四校验全绿。
```bash
git add -A && git commit -m "test(sec): T8(字段审计) gstack真浏览器QA全流程(改字段留痕/R2角色菜单改名/增删/密钥不泄露/时间线回放/403/多租户隔离)
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review（计划自审）

**1. Spec 覆盖**：§2.1 实体→T1；§2.2 标记/标注→T1；§2.3 迁移→T1；§3.1 拒名单/§3.2 跳过集/§3.3 用户访问器→T2/T3；§3.4 两阶段原子（ExtractKey/ResolveAuditTenant/键时序）→T3；§3.5 先查后改前置→T4；§4 查询/回放端点→T5；§5 授权/DI/多租户/权限点→T2(DI)/T5(权限点)/T1(多租户自动)；§6 i18n→T6/前端→T7；§7.1 单测→各 Task、§7.2 gstack→T8；§8 局限/§9 hardening=不做（文档化）。**无遗漏**。
**2. 评审 8 点（R1~R8）映射**：R1 首批 11 现存实体+不依赖#2/#3→T1 标注清单；R2 Role/Menu 先查后改→T4+T3 int 键单测；R3 `sys-field-audit` menuKey+`Sys_RoleMenu`→T5 seed；R4 审计行 TenantId 镜像/共享表回退→T3 `ResolveAuditTenant`+TenantId 单测；R5 删 403 单测改 `{total,rows}` 反射→T5（403 移交 T8）；R6 原子回滚 InMemory 不可验→T3 注记（仅验键落定+同周期）；R7 `Database.IsRelational()`/DI 自动注入/列表 `changeCount` 投影→T2/T3/T5；R8 EntityKey 键形无关（`ExtractKey` 走主键元数据+`KeyBeforeSave`）→T3。**全覆盖**。
**3. 类型一致**：`IAuditable`/`AuditIgnoreAttribute`/`Sys_FieldAuditLog`/`ICurrentUserAccessor`/`PendingAudit`/`FieldChange` 签名跨 T1~T5 一致；`EntityKey` 键形无关语义跨 T3（捕获）/T5（查询）一致；`Operation` 枚举 1/2/3 跨 T1/T3/T5/T7 一致；`changeCount` 摘要 vs 完整 `changes` 边界跨 T5/T7 一致。
**待落码前确认**（非阻塞，首 Task grep 核定）：①`CP6.Core` 是否已引 `Microsoft.AspNetCore.Http`（`IHttpContextAccessor`）与 relational 包（`IsRelational()`/`BeginTransaction`）——认证服务/SqlServer 已引，预期可用；②11 实体精确文件路径与现有基类声明（`Sys_Role`/`Sys_Menu` 无基类）；③`Program.cs` 是否已 `AddHttpContextAccessor()`（幂等，避免重复）；④`Sys_FieldAuditLogs` 的 MenuId=115 未被占用；⑤前端是否需为 `record` 端点完整 `changes` 反序列化加类型。

---

*生成于 2026-06-23。源 spec：`docs/superpowers/specs/2026-06-22-field-audit-design.md`（定稿 `17b72a5` + R8 `4a20a60`，全 §1 锚点经只读 subagent + 本会话实读核验）。执行：subagent-driven，每 Task spec审+质量审双过 + 先绿后本地 commit（不 push，用户监督）+ gstack QA。基线 996 测。底座 #1（已落码）；#4 自洽不硬依赖 #2/#3。*
