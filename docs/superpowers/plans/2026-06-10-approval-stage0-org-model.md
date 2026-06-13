# 审批引擎 阶段0 · 组织模型 Implementation Plan

> **⚠️ 归属移交（2026-06-12 复审定稿）**：组织模型已划归 **[PUB 公共平台 章00](../../pub/README.md)**（PUB 数据权限与 OA 审批路由的共同前置，做一次两处用）。本计划据此**拆分**：
> - **组织主数据部分 → PUB**：`Sys_Dept`（物化路径树 + LeaderId）+ `Sys_User` 补 `DeptId/ManagerId/Email` + 部门管理 UI（落 `Sys` 命名空间不变），作为 PUB 章00 / M0 前置先落。
> - **审批人解析部分 → OA**：`IApproverResolver`（`Wf` 命名空间）并入 OA **阶段1**（OA 从阶段1 起步，不再有自建组织模型的阶段0）。
> 本文件保留为实现参考，按上述归属分别落地。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 CP6 补上审批引擎的硬前置——部门树 + 用户上级/部门 + 审批人解析器，让"找直属上级 / 部门负责人 / 角色 / 指定人"在运行时算得出来。

**Architecture:** EF Core Code-First 新增 `Sys_Dept`（物化路径部门树）并给 `Sys_User` 补三字段；组织主数据落 `Sys` 命名空间，审批人解析逻辑（`IApproverResolver`）落新的 `Wf` 命名空间。解析器纯查询 `CP6Context`，返回"已解析审批人列表"或"缺位原因"（供上层挂起人工指派），不抛异常。

**Tech Stack:** .NET 8 / EF Core / xUnit + EF InMemory（测试）/ Vue 3 + Element Plus（部门管理 UI）。

---

## 与总纲（docs/approval/README.md）的偏离说明（已确认）

1. **不引入 `TenantId`**：现有代码库全库无多租户字段（`grep TenantId` = 0）。多租户属 ch10/阶段4，阶段0 不污染表结构。
2. **`Sys_Dept` 落 `CP6.Entity/DomainModels/Sys/`**（`Sys_` 前缀组织主数据，与 `Sys_User`/`Sys_Role` 同族），CRUD 服务落 `CP6.Core/Services/Sys/`；**审批人解析器落 `CP6.Core/Services/Wf/`**（引擎逻辑）。总纲笼统写的"全落 Wf"在此细化。
3. **`Sys_Dept` 增 `Code` 字段**：物化路径用部门编码拼接（`/HQ/SALES/`），比用 DB 生成的 Guid 更稳、可读，且建树时无需先拿到 Id。
4. `Sys_Dept` 继承 `BaseEntity`（与 `Sys_User` 一致，用 `Enable` 软停用，不引入 `BaseBizEntity` 的 RowVersion/IsDeleted——保持 Sys 家族一致）。

---

## File Structure

| 文件 | 职责 | 动作 |
|---|---|---|
| `CP6.Entity/DomainModels/Sys/Sys_Dept.cs` | 部门树实体（Code/Name/ParentId/Path/LeaderId/Sort/Enable） | Create |
| `CP6.Entity/DomainModels/Sys/Sys_User.cs` | 补 DeptId/ManagerId/Email | Modify |
| `CP6.Core/EFDbContext/CP6Context.cs` | 注册 `DbSet<Sys_Dept>` | Modify |
| `CP6.Core/Services/Sys/IDeptService.cs` | 部门 CRUD + 物化路径维护 接口 | Create |
| `CP6.Core/Services/Sys/DeptService.cs` | 实现 | Create |
| `CP6.Core/Services/Wf/IApproverResolver.cs` | 审批人解析接口 + 规则/结果类型 | Create |
| `CP6.Core/Services/Wf/ApproverResolver.cs` | 四种策略 + 缺位兜底 实现 | Create |
| `CP6.WebApi/Controllers/Sys/DeptController.cs` | 部门 REST | Create |
| `CP6.WebApi/Program.cs` | DI 注册 IDeptService / IApproverResolver | Modify |
| `CP6.Tests/GlobalUsings.cs` | 补 `CP6.Core.Services.Wf` using | Modify |
| `CP6.Tests/DeptServiceTests.cs` | 部门服务测试 | Create |
| `CP6.Tests/ApproverResolverTests.cs` | 解析器测试（★核心） | Create |
| `cp6.web/src/api/sys/dept.ts` | 部门 API 封装 | Create |
| `cp6.web/src/views/sys/DeptTreeView.vue` | 部门树管理页 | Create |

EF 迁移：`CP6.Core/Migrations/`（由 `dotnet ef` 生成）。

---

### Task 1: `Sys_Dept` 实体 + DbSet 注册

**Files:**
- Create: `CP6.Entity/DomainModels/Sys/Sys_Dept.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`

- [ ] **Step 1: 写实体**

`CP6.Entity/DomainModels/Sys/Sys_Dept.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 部门（组织树节点）。审批路由的"部门负责人/直属部门"靠它解析。
/// </summary>
[Table("Sys_Dept")]
public class Sys_Dept : BaseEntity
{
    /// <summary>部门编码（物化路径用，全局唯一，如 HQ / SALES）</summary>
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    /// <summary>部门名称</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>上级部门 Id（根部门为 null）</summary>
    public Guid? ParentId { get; set; }

    /// <summary>物化路径，形如 /HQ/SALES/ ，用于子树查询</summary>
    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;

    /// <summary>部门负责人 → Sys_User.Id</summary>
    public Guid? LeaderId { get; set; }

    /// <summary>同级排序</summary>
    public int Sort { get; set; } = 0;

    /// <summary>是否启用（软停用，不物理删除）</summary>
    public bool Enable { get; set; } = true;
}
```

- [ ] **Step 2: 注册 DbSet**

在 `CP6.Core/EFDbContext/CP6Context.cs` 的 DbSet 区块（与 `Sys_Users` 相邻）追加一行：
```csharp
public DbSet<Sys_Dept> Sys_Depts { get; set; }
```
（若该文件顶部未 `using CP6.Entity.DomainModels.Sys;`，确认已存在——`Sys_User` 已在用，通常已 using。）

- [ ] **Step 3: 编译验证**

Run: `dotnet build CP6.Core`
Expected: 编译通过（0 error）。

- [ ] **Step 4: Commit**

```bash
git add CP6.Entity/DomainModels/Sys/Sys_Dept.cs CP6.Core/EFDbContext/CP6Context.cs
git commit -m "feat(wf): add Sys_Dept 部门树实体 + DbSet 注册"
```

---

### Task 2: `Sys_User` 补 DeptId / ManagerId / Email

**Files:**
- Modify: `CP6.Entity/DomainModels/Sys/Sys_User.cs`

- [ ] **Step 1: 加三个字段**

在 `Sys_User` 类内 `Enable` 字段后追加：
```csharp
    /// <summary>所属部门 → Sys_Dept.Id</summary>
    public Guid? DeptId { get; set; }

    /// <summary>直属上级 → Sys_User.Id（审批"找上级"靠它逐级上溯）</summary>
    public Guid? ManagerId { get; set; }

    /// <summary>邮箱（通知用）</summary>
    [MaxLength(200)]
    public string? Email { get; set; }
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build CP6.Core`
Expected: 编译通过。

- [ ] **Step 3: Commit**

```bash
git add CP6.Entity/DomainModels/Sys/Sys_User.cs
git commit -m "feat(wf): Sys_User 补 DeptId/ManagerId/Email 三字段"
```

---

### Task 3: `DeptService` — CRUD + 物化路径维护（TDD）

**Files:**
- Create: `CP6.Core/Services/Sys/IDeptService.cs`
- Create: `CP6.Core/Services/Sys/DeptService.cs`
- Test: `CP6.Tests/DeptServiceTests.cs`

- [ ] **Step 1: 写接口**

`CP6.Core/Services/Sys/IDeptService.cs`:
```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

public interface IDeptService
{
    /// <summary>取全部启用部门（按 Path 排序，前端自行组树）</summary>
    Task<List<Sys_Dept>> GetAllAsync();

    /// <summary>新建部门：自动按上级 Path 拼接物化路径，返回新 Id</summary>
    Task<Guid> CreateAsync(Sys_Dept dept, string? userName);

    /// <summary>改名/换负责人/排序（不改父级）</summary>
    Task UpdateAsync(Guid id, Sys_Dept dept, string? userName);

    /// <summary>软停用（Enable=false）</summary>
    Task DisableAsync(Guid id, string? userName);
}
```

- [ ] **Step 2: 写失败测试**

`CP6.Tests/DeptServiceTests.cs`:
```csharp
using CP6.Core.Services.Sys;

namespace CP6.Tests;

public class DeptServiceTests
{
    private static DeptService Svc(out CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new DeptService(db);
    }

    [Fact]
    public async Task Create_Root_BuildsRootPath()
    {
        var svc = Svc(out var db);
        var id = await svc.CreateAsync(new Sys_Dept { Code = "HQ", Name = "总部" }, "u1");

        var d = await db.Sys_Depts.FindAsync(id);
        Assert.NotNull(d);
        Assert.Equal("/HQ/", d!.Path);
        Assert.Equal("u1", d.Creator);
    }

    [Fact]
    public async Task Create_Child_AppendsParentPath()
    {
        var svc = Svc(out var db);
        var hq = await svc.CreateAsync(new Sys_Dept { Code = "HQ", Name = "总部" }, "u1");
        var sales = await svc.CreateAsync(new Sys_Dept { Code = "SALES", Name = "销售部", ParentId = hq }, "u1");

        var d = await db.Sys_Depts.FindAsync(sales);
        Assert.Equal("/HQ/SALES/", d!.Path);
    }

    [Fact]
    public async Task Create_UnknownParent_Throws()
    {
        var svc = Svc(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new Sys_Dept { Code = "X", Name = "X", ParentId = Guid.NewGuid() }, "u1"));
    }

    [Fact]
    public async Task Disable_SetsEnableFalse()
    {
        var svc = Svc(out var db);
        var id = await svc.CreateAsync(new Sys_Dept { Code = "HQ", Name = "总部" }, "u1");
        await svc.DisableAsync(id, "u1");
        Assert.False((await db.Sys_Depts.FindAsync(id))!.Enable);
    }
}
```

- [ ] **Step 3: 运行测试，确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~DeptServiceTests"`
Expected: FAIL —— `DeptService` 类型不存在 / 编译失败。

- [ ] **Step 4: 写实现**

`CP6.Core/Services/Sys/DeptService.cs`:
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public class DeptService : IDeptService
{
    private readonly CP6Context _db;
    public DeptService(CP6Context db) => _db = db;

    public async Task<List<Sys_Dept>> GetAllAsync() =>
        await _db.Sys_Depts.Where(d => d.Enable).OrderBy(d => d.Path).ThenBy(d => d.Sort).ToListAsync();

    public async Task<Guid> CreateAsync(Sys_Dept dept, string? userName)
    {
        if (dept.ParentId != null)
        {
            var parent = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == dept.ParentId)
                ?? throw new InvalidOperationException("上级部门不存在");
            dept.Path = $"{parent.Path}{dept.Code}/";
        }
        else
        {
            dept.Path = $"/{dept.Code}/";
        }
        dept.Creator = userName;
        dept.CreateDate = DateTime.Now;
        _db.Sys_Depts.Add(dept);
        await _db.SaveChangesAsync();   // Id 由 provider 在插入后回填
        return dept.Id;
    }

    public async Task UpdateAsync(Guid id, Sys_Dept dept, string? userName)
    {
        var entity = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException("部门不存在");
        entity.Name = dept.Name;
        entity.LeaderId = dept.LeaderId;
        entity.Sort = dept.Sort;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DisableAsync(Guid id, string? userName)
    {
        var entity = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException("部门不存在");
        entity.Enable = false;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
```

> 注：物化路径用 `Code` 拼接，不依赖 `Id`，因此**不手动赋 `Id`**——`BaseEntity.Id` 标了 `DatabaseGeneratedOption.Identity`，由 provider 在插入后回填，`SaveChangesAsync` 后 `dept.Id` 即为有效值。

- [ ] **Step 5: 运行测试，确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~DeptServiceTests"`
Expected: PASS（4 passed）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Sys/IDeptService.cs CP6.Core/Services/Sys/DeptService.cs CP6.Tests/DeptServiceTests.cs
git commit -m "feat(wf): DeptService 部门CRUD + 物化路径维护"
```

---

### Task 4: `ApproverResolver` — 审批人解析（★核心，TDD）

**Files:**
- Create: `CP6.Core/Services/Wf/IApproverResolver.cs`
- Create: `CP6.Core/Services/Wf/ApproverResolver.cs`
- Modify: `CP6.Tests/GlobalUsings.cs`
- Test: `CP6.Tests/ApproverResolverTests.cs`

- [ ] **Step 1: 写接口 + 规则/结果类型**

`CP6.Core/Services/Wf/IApproverResolver.cs`:
```csharp
namespace CP6.Core.Services.Wf;

/// <summary>审批人解析策略</summary>
public enum ApproverRuleType
{
    DirectManager = 1,  // 直属上级（逐级 N）
    DeptLeader = 2,     // 部门负责人/部门长
    Role = 3,           // 指定角色/岗位
    SpecifiedUser = 4,  // 发起人/表单字段指定的具体人
}

/// <summary>来自流程节点 schema 的一条解析规则</summary>
public class ApproverRule
{
    public ApproverRuleType Type { get; set; }
    /// <summary>DirectManager：逐级上溯 N 级（&lt;1 视为 1）</summary>
    public int Levels { get; set; } = 1;
    /// <summary>Role：目标角色</summary>
    public int? RoleId { get; set; }
    /// <summary>SpecifiedUser：指定的人</summary>
    public Guid? UserId { get; set; }
    /// <summary>DeptLeader：指定部门（null = 取发起人所属部门）</summary>
    public Guid? DeptId { get; set; }
}

/// <summary>解析上下文</summary>
public class ApproverResolveContext
{
    public Guid StarterUserId { get; set; }
}

/// <summary>解析结果：要么有审批人，要么给出缺位原因供上层挂起人工指派</summary>
public class ApproverResolveResult
{
    public List<Guid> ApproverIds { get; set; } = new();
    public bool Resolved => ApproverIds.Count > 0;
    public string? UnresolvedReason { get; set; }
}

public interface IApproverResolver
{
    Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx);
}
```

- [ ] **Step 2: 补测试全局 using**

在 `CP6.Tests/GlobalUsings.cs` 末尾追加：
```csharp
global using CP6.Core.Services.Wf;
global using CP6.Core.Services.Sys;
```

- [ ] **Step 3: 写失败测试**

`CP6.Tests/ApproverResolverTests.cs`:
```csharp
namespace CP6.Tests;

public class ApproverResolverTests
{
    // 造组织：CEO ← MGR ← EMP（EMP 的上级是 MGR，MGR 的上级是 CEO）
    private static (ApproverResolver svc, CP6Context db, Guid emp, Guid mgr, Guid ceo, Guid deptId)
        Setup()
    {
        var db = TestHelper.CreateInMemoryContext();
        var ceo = new Sys_User { UserName = "ceo", Password = "x", Enable = true };
        var mgr = new Sys_User { UserName = "mgr", Password = "x", Enable = true };
        db.Sys_Users.AddRange(ceo, mgr);
        db.SaveChanges();
        mgr.ManagerId = ceo.Id;

        var dept = new Sys_Dept { Id = Guid.NewGuid(), Code = "SALES", Name = "销售部", Path = "/SALES/", LeaderId = mgr.Id, Enable = true };
        db.Sys_Depts.Add(dept);

        var emp = new Sys_User { UserName = "emp", Password = "x", Enable = true, ManagerId = mgr.Id, DeptId = dept.Id };
        db.Sys_Users.Add(emp);
        db.SaveChanges();

        return (new ApproverResolver(db), db, emp.Id, mgr.Id, ceo.Id, dept.Id);
    }

    [Fact]
    public async Task DirectManager_Level1_ReturnsImmediateManager()
    {
        var (svc, _, emp, mgr, _, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DirectManager, Levels = 1 },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.True(r.Resolved);
        Assert.Equal(new[] { mgr }, r.ApproverIds);
    }

    [Fact]
    public async Task DirectManager_Level2_ReturnsGrandManager()
    {
        var (svc, _, emp, _, ceo, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DirectManager, Levels = 2 },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.Equal(new[] { ceo }, r.ApproverIds);
    }

    [Fact]
    public async Task DirectManager_ChainShorterThanN_ReturnsTopOfChain()
    {
        var (svc, _, emp, _, ceo, _) = Setup();
        // 想要 5 级，但链只有 EMP→MGR→CEO，取链顶 CEO
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DirectManager, Levels = 5 },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.Equal(new[] { ceo }, r.ApproverIds);
    }

    [Fact]
    public async Task DirectManager_NoManager_Unresolved()
    {
        var (svc, _, _, _, ceo, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DirectManager, Levels = 1 },
            new ApproverResolveContext { StarterUserId = ceo }); // CEO 无上级
        Assert.False(r.Resolved);
        Assert.NotNull(r.UnresolvedReason);
    }

    [Fact]
    public async Task DeptLeader_FromStarterDept_ReturnsLeader()
    {
        var (svc, _, emp, mgr, _, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DeptLeader },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.Equal(new[] { mgr }, r.ApproverIds);
    }

    [Fact]
    public async Task DeptLeader_NoLeader_WalksUpToParentLeader()
    {
        var (svc, db, _, mgr, _, _) = Setup();
        // 子部门无负责人，应沿父部门 SALES（负责人 mgr）兜底
        var child = new Sys_Dept { Id = Guid.NewGuid(), Code = "SALES1", Name = "销售一组",
            ParentId = db.Sys_Depts.First().Id, Path = "/SALES/SALES1/", LeaderId = null, Enable = true };
        db.Sys_Depts.Add(child);
        var sub = new Sys_User { UserName = "sub", Password = "x", Enable = true, DeptId = child.Id };
        db.Sys_Users.Add(sub);
        db.SaveChanges();

        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DeptLeader },
            new ApproverResolveContext { StarterUserId = sub.Id });
        Assert.Equal(new[] { mgr }, r.ApproverIds);
    }

    [Fact]
    public async Task DeptLeader_NoDept_Unresolved()
    {
        var (svc, db, _, _, _, _) = Setup();
        var orphan = new Sys_User { UserName = "orphan", Password = "x", Enable = true, DeptId = null };
        db.Sys_Users.Add(orphan);
        db.SaveChanges();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.DeptLeader },
            new ApproverResolveContext { StarterUserId = orphan.Id });
        Assert.False(r.Resolved);
    }

    [Fact]
    public async Task Role_ReturnsAllEnabledUsersInRole()
    {
        var (svc, db, _, _, _, _) = Setup();
        var a = new Sys_User { UserName = "a", Password = "x", Enable = true, RoleId = 7 };
        var b = new Sys_User { UserName = "b", Password = "x", Enable = true, RoleId = 7 };
        var c = new Sys_User { UserName = "c", Password = "x", Enable = false, RoleId = 7 }; // 停用，应排除
        db.Sys_Users.AddRange(a, b, c);
        db.SaveChanges();

        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.Role, RoleId = 7 },
            new ApproverResolveContext { StarterUserId = a.Id });
        Assert.Equal(2, r.ApproverIds.Count);
        Assert.Contains(a.Id, r.ApproverIds);
        Assert.Contains(b.Id, r.ApproverIds);
    }

    [Fact]
    public async Task Role_Empty_Unresolved()
    {
        var (svc, _, emp, _, _, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.Role, RoleId = 999 },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.False(r.Resolved);
    }

    [Fact]
    public async Task SpecifiedUser_ReturnsThatUser()
    {
        var (svc, _, emp, mgr, _, _) = Setup();
        var r = await svc.ResolveAsync(
            new ApproverRule { Type = ApproverRuleType.SpecifiedUser, UserId = mgr },
            new ApproverResolveContext { StarterUserId = emp });
        Assert.Equal(new[] { mgr }, r.ApproverIds);
    }
}
```

- [ ] **Step 4: 运行测试，确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ApproverResolverTests"`
Expected: FAIL —— `ApproverResolver` 不存在 / 编译失败。

- [ ] **Step 5: 写实现**

`CP6.Core/Services/Wf/ApproverResolver.cs`:
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public class ApproverResolver : IApproverResolver
{
    private readonly CP6Context _db;
    public ApproverResolver(CP6Context db) => _db = db;

    public async Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        return rule.Type switch
        {
            ApproverRuleType.DirectManager => await ResolveDirectManagerAsync(rule, ctx),
            ApproverRuleType.DeptLeader    => await ResolveDeptLeaderAsync(rule, ctx),
            ApproverRuleType.Role          => await ResolveRoleAsync(rule),
            ApproverRuleType.SpecifiedUser => ResolveSpecifiedUser(rule),
            _ => Unresolved($"未知审批人规则类型 {rule.Type}")
        };
    }

    // 直属上级：逐级上溯，链短于 N 时取可达链顶；发起人无上级则缺位
    private async Task<ApproverResolveResult> ResolveDirectManagerAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        var levels = rule.Levels < 1 ? 1 : rule.Levels;
        var current = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        if (current == null) return Unresolved("发起人不存在");

        Sys_User? manager = null;
        for (int i = 0; i < levels; i++)
        {
            if (current.ManagerId == null) break;
            var next = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == current.ManagerId && u.Enable);
            if (next == null) break;
            manager = next;
            current = next;
        }
        return manager == null
            ? Unresolved("发起人无直属上级，需人工指派")
            : Resolved(manager.Id);
    }

    // 部门负责人：取发起人部门（或指定部门），沿父部门向上找第一个有效负责人
    private async Task<ApproverResolveResult> ResolveDeptLeaderAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        Guid? deptId = rule.DeptId;
        if (deptId == null)
        {
            var starter = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
            if (starter == null) return Unresolved("发起人不存在");
            deptId = starter.DeptId;
        }
        if (deptId == null) return Unresolved("发起人未分配部门，无法定位部门负责人");

        var dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == deptId && d.Enable);
        while (dept != null)
        {
            if (dept.LeaderId != null)
            {
                var leader = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == dept.LeaderId && u.Enable);
                if (leader != null) return Resolved(leader.Id);
            }
            if (dept.ParentId == null) break;
            dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == dept.ParentId && d.Enable);
        }
        return Unresolved("沿部门树未找到有效的部门负责人，需人工指派");
    }

    // 指定角色：该角色下所有启用用户
    private async Task<ApproverResolveResult> ResolveRoleAsync(ApproverRule rule)
    {
        if (rule.RoleId == null) return Unresolved("未指定角色");
        var ids = await _db.Sys_Users
            .Where(u => u.RoleId == rule.RoleId && u.Enable)
            .Select(u => u.Id)
            .ToListAsync();
        return ids.Count > 0 ? Resolved(ids.ToArray()) : Unresolved($"角色 {rule.RoleId} 下无启用用户");
    }

    // 指定人
    private static ApproverResolveResult ResolveSpecifiedUser(ApproverRule rule) =>
        rule.UserId == null ? Unresolved("未指定审批人") : Resolved(rule.UserId.Value);

    private static ApproverResolveResult Resolved(params Guid[] ids) => new() { ApproverIds = ids.ToList() };
    private static ApproverResolveResult Unresolved(string reason) => new() { UnresolvedReason = reason };
}
```

- [ ] **Step 6: 运行测试，确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ApproverResolverTests"`
Expected: PASS（10 passed）。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Wf/IApproverResolver.cs CP6.Core/Services/Wf/ApproverResolver.cs CP6.Tests/ApproverResolverTests.cs CP6.Tests/GlobalUsings.cs
git commit -m "feat(wf): ApproverResolver 审批人解析(四策略+缺位兜底)"
```

---

### Task 5: REST Controller + DI 注册

**Files:**
- Create: `CP6.WebApi/Controllers/Sys/DeptController.cs`
- Modify: `CP6.WebApi/Program.cs`

- [ ] **Step 1: 写 Controller**

`CP6.WebApi/Controllers/Sys/DeptController.cs`:
```csharp
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Authorize]
[Route("api/sys/dept")]
public class DeptController : ControllerBase
{
    private readonly IDeptService _svc;
    public DeptController(IDeptService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Sys_Dept dept)
        => Ok(new { id = await _svc.CreateAsync(dept, CurrentUser) });

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Sys_Dept dept)
    {
        await _svc.UpdateAsync(id, dept, CurrentUser);
        return Ok();
    }

    [HttpPost("{id}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        await _svc.DisableAsync(id, CurrentUser);
        return Ok();
    }
}
```

- [ ] **Step 2: DI 注册**

在 `CP6.WebApi/Program.cs` 的服务注册区（与其它 `AddScoped<I...Service...>` 相邻，约 145 行附近）追加：
```csharp
// 审批引擎 阶段0 · 组织模型
builder.Services.AddScoped<CP6.Core.Services.Sys.IDeptService, CP6.Core.Services.Sys.DeptService>();
builder.Services.AddScoped<CP6.Core.Services.Wf.IApproverResolver, CP6.Core.Services.Wf.ApproverResolver>();
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build CP6.WebApi`
Expected: 编译通过。

- [ ] **Step 4: Commit**

```bash
git add CP6.WebApi/Controllers/Sys/DeptController.cs CP6.WebApi/Program.cs
git commit -m "feat(wf): DeptController + DI 注册组织模型服务"
```

---

### Task 6: EF 迁移

**Files:**
- Create: `CP6.Core/Migrations/*_Wf_Stage0_OrgModel.cs`（由工具生成）

- [ ] **Step 1: 生成迁移**

Run（在解决方案根 `D:\CP6`）:
```bash
dotnet ef migrations add Wf_Stage0_OrgModel --project CP6.Core --startup-project CP6.WebApi
```
Expected: 在 `CP6.Core/Migrations/` 生成迁移文件，含新增 `Sys_Dept` 表 + `Sys_User` 加 `DeptId/ManagerId/Email` 列。
> 若机器未装 EF 工具：`dotnet tool install --global dotnet-ef`。

- [ ] **Step 2: 核对迁移内容**

打开生成的迁移 `Up()`，确认：`CreateTable("Sys_Dept", ...)` 与 `AddColumn<Guid>("DeptId"/"ManagerId", "Sys_Users")`、`AddColumn<string>("Email", ...)` 都在，无多余表改动。

- [ ] **Step 3: 应用到本地库（可选，需连库）**

Run: `dotnet ef database update --project CP6.Core --startup-project CP6.WebApi`
Expected: 迁移成功应用。
> 测试用 EF InMemory，不依赖此步；此步仅为真实库。

- [ ] **Step 4: Commit**

```bash
git add CP6.Core/Migrations/
git commit -m "feat(wf): EF 迁移 Wf_Stage0_OrgModel(Sys_Dept + Sys_User三字段)"
```

---

### Task 7: 前端部门树管理页

**Files:**
- Create: `cp6.web/src/api/sys/dept.ts`
- Create: `cp6.web/src/views/sys/DeptTreeView.vue`
- Modify: 路由表（`cp6.web/src/router/index.ts` 或既有按模块的路由文件——按现有 wms 视图的注册方式照做）

> 前端无单元测试（与代码库一致：现有测试全在 CP6.Tests 后端）。验收靠 `npm run build` 类型通过 + 手动开页。

- [ ] **Step 1: 写 API 封装**

`cp6.web/src/api/sys/dept.ts`:
```typescript
import http from '@/api/http'

export interface Dept {
  id?: string
  code: string
  name: string
  parentId?: string | null
  path?: string
  leaderId?: string | null
  sort?: number
  enable?: boolean
}

export const deptApi = {
  getAll() {
    return http.get<any, Dept[]>('/sys/dept')
  },
  create(dto: Dept) {
    return http.post<any, { id: string }>('/sys/dept', dto)
  },
  update(id: string, dto: Dept) {
    return http.put<any, void>(`/sys/dept/${id}`, dto)
  },
  disable(id: string) {
    return http.post<any, void>(`/sys/dept/${id}/disable`)
  },
}
```
> 注：`@/api/http` 的默认导出与响应包装以现有 `src/api/http.ts` 为准（参考 `src/api/wms/inboundOrder.ts` 的 import 与返回类型写法，若现有用具名导出/`WmsApi<T>` 包装则对齐之）。

- [ ] **Step 2: 写视图**

`cp6.web/src/views/sys/DeptTreeView.vue`:
```vue
<template>
  <div class="sys-dept">
    <el-card shadow="never">
      <div style="margin-bottom: 12px">
        <el-button type="primary" size="small" @click="openCreate(null)">新建根部门</el-button>
        <el-button size="small" @click="reload" :loading="loading">刷新</el-button>
      </div>
      <el-tree
        :data="tree"
        node-key="id"
        :props="{ label: 'name', children: 'children' }"
        default-expand-all
      >
        <template #default="{ data }">
          <span>{{ data.name }}（{{ data.code }}）</span>
          <span style="margin-left: 12px">
            <el-button link type="primary" size="small" @click="openCreate(data)">加子部门</el-button>
            <el-button link type="primary" size="small" @click="openEdit(data)">编辑</el-button>
            <el-button link type="danger" size="small" @click="onDisable(data)">停用</el-button>
          </span>
        </template>
      </el-tree>
    </el-card>

    <el-dialog v-model="dlg" :title="editing.id ? '编辑部门' : '新建部门'" width="420px">
      <el-form :model="editing" label-width="90px">
        <el-form-item label="编码"><el-input v-model="editing.code" :disabled="!!editing.id" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="editing.name" /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="editing.sort" :min="0" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { ElMessage } from 'element-plus'
import { deptApi, type Dept } from '@/api/sys/dept'

const loading = ref(false)
const flat = ref<Dept[]>([])
const tree = ref<any[]>([])
const dlg = ref(false)
const editing = reactive<Dept>({ code: '', name: '', sort: 0, parentId: null })

function buildTree(list: Dept[]) {
  const map = new Map<string, any>()
  list.forEach(d => map.set(d.id!, { ...d, children: [] }))
  const roots: any[] = []
  map.forEach(node => {
    if (node.parentId && map.has(node.parentId)) map.get(node.parentId).children.push(node)
    else roots.push(node)
  })
  return roots
}

async function reload() {
  loading.value = true
  try {
    flat.value = (await deptApi.getAll()) || []
    tree.value = buildTree(flat.value)
  } finally {
    loading.value = false
  }
}

function openCreate(parent: Dept | null) {
  Object.assign(editing, { id: undefined, code: '', name: '', sort: 0, parentId: parent?.id ?? null })
  dlg.value = true
}
function openEdit(d: Dept) {
  Object.assign(editing, { ...d })
  dlg.value = true
}
async function save() {
  if (!editing.code || !editing.name) { ElMessage.warning('编码与名称必填'); return }
  if (editing.id) await deptApi.update(editing.id, editing)
  else await deptApi.create(editing)
  dlg.value = false
  await reload()
}
async function onDisable(d: Dept) {
  await deptApi.disable(d.id!)
  await reload()
}

reload()
</script>
```

- [ ] **Step 3: 注册路由**

按现有 wms 视图在路由表中的注册方式（参考 `InboundOrderListView` 的路由项），新增：
```
{ path: '/sys/dept', name: 'SysDept', component: () => import('@/views/sys/DeptTreeView.vue') }
```
（具体落在哪个路由文件以现有结构为准。）

- [ ] **Step 4: 构建验证**

Run（在 `cp6.web`）: `npm run build`
Expected: 类型检查 + 构建通过。

- [ ] **Step 5: Commit**

```bash
git add cp6.web/src/api/sys/dept.ts cp6.web/src/views/sys/DeptTreeView.vue cp6.web/src/router
git commit -m "feat(wf): 部门树管理页 + dept API"
```

---

## 全量验收

- [ ] Run: `dotnet test CP6.Tests` → 全绿（新增 14 个测试通过，存量测试不破）。
- [ ] Run: `dotnet build` → 解决方案 0 error。
- [ ] Run（`cp6.web`）: `npm run build` → 通过。
- [ ] 手动：登录后开 `/sys/dept`，建"总部→销售部"，给销售部设负责人，确认树渲染与保存正常。

完成后阶段0 闭合：审批人"找直属上级/部门负责人/角色/指定人"算得出来，且缺位有兜底——为阶段1（流程引擎运行时调用 `IApproverResolver` 建 FlowTask）扫清前置。
