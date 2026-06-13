# PUB B0 · 组织模型（章00 Sys_Dept）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。
>
> **取代声明**：本计划**取代** `2026-06-10-approval-stage0-org-model.md`。旧计划的组织主数据部分迁到这里并对齐 PUB 章00 定稿（DeptCode/DeptName 命名、Id 物化路径、删除三校验）；旧计划的 `IApproverResolver` 部分**不在本计划**——章00 §12 已明确审批人解析器归 **OA 阶段1（Wf 命名空间）**，由 OA 模块计划落地。旧计划保留作历史参考。

**Goal:** 落地 PUB 章00 组织模型——新建部门树 `Sys_Dept`（物化路径 `Path` + `LeaderId` 部门长）+ `Sys_User` 补 `DeptId/ManagerId/Email` 三字段 + 部门 CRUD/移动（子树 Path 重算·防成环）/删除三校验 + 部门管理 UI。它是**整条主链的根**：PUB 数据权限（章03"本部门及下级"用 `Path` 子树过滤）和 OA 审批路由（用 `LeaderId/ManagerId` 解析直属上级/部门长）的共同前置，一处建、两处用。

**Architecture:** EF Core Code-First。组织主数据落 `Sys` 命名空间（与 `Sys_User`/`Sys_Role` 同族，不改名）：`CP6.Entity/DomainModels/Sys/Sys_Dept.cs`、`CP6.Core/Services/Sys/DeptService.cs`、`CP6.WebApi/Controllers/Sys/DeptController.cs`、`cp6.web/src/views/pub/dept/`。物化路径 `Path = /{rootId}/.../{selfId}/`（Id 串，前缀 `LIKE` 命中整棵子树，查询层零递归）；移动部门重算子树 Path + 防成环。`Sys_Dept` 继承 `BaseEntity`（用 `Enable` 软停用，不引 `BaseBizEntity` 的 `IsDeleted`/`RowVersion`，与 Sys 家族一致）。审批人解析器**不在本计划**（OA 阶段1）。

**Tech Stack:** .NET 8 + EF Core 8 + SQL Server / xUnit + EF Core InMemory / Vue 3.5 + element-plus（`el-tree`）+ Pinia + axios。源文档：`docs/pub/00-org-model.md`（引用 PUB README）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 文档（章00）原意 | CP6 现状 | **本稿建议值** |
|---|---|---|---|---|
| **B0-D1** | **TenantId / 多租户** | 全表 `TenantId`，唯一索引 `(TenantId, DeptCode)`，EF 全局过滤 | **零多租户**；且 PUB 改的是**既有 Sys_ 家族**（Sys_User/Role/Menu 全无 TenantId） | **本阶段不引入 TenantId**（沿用 2026-06-10 旧计划判断）。理由：①Sys 家族整体无 TenantId，只给 Sys_Dept 加一个 = 半拉子多租户；②多租户是**系统级横切**，应在 **PUB 章09（多租户）一次性给 Sys 全族 + EF 全局过滤 + 租户解析**，而非组织模型这一章。唯一索引本阶段 = `UX_Sys_Dept_Code(DeptCode)`，章09 升级为 `(TenantId, DeptCode)`。**⚠️ 注意与 Space 后端计划不一致**：Space 是全新表用了方案A（现在就上 TenantId）；PUB 触既有 Sys 家族故延后——若你要全系统统一，请在此拍板（建议统一在章09 处理整个 Sys 族） |
| **B0-D2** | **物化路径键** | Id 串 `/{rootId}/{selfId}/`（章00 §3） | — | **用 Id 串**（章00 定稿）。服务建部门时 `Id = Guid.NewGuid()` 先生成再拼 Path。旧计划曾用 Code 串（`/HQ/SALES/`，更可读），但章00 已定 Id 串且 DeptCode 改为只读后两者都稳——**遵章00 用 Id 串**（spec 保真；Id 永不变，比 code 更稳） |
| **B0-D3** | **字段命名** | `DeptCode/DeptName`（章00 §2） | 旧计划用 `Code/Name` | **用 `DeptCode/DeptName`**（章00 定稿，与"租户内唯一"消息 E-PUB-001 一致） |
| **B0-D4** | **审计字段** | DDL 写 `CreateTime/Creator` | 真实 `BaseEntity`=`Creator/CreateDate/Modifier/ModifyDate` | **以代码为准**（同 Space 后端计划 D-B） |
| **B0-D5** | **ApproverResolver 归属** | 章00 §8 描述，§12 注"并入 OA 阶段1" | 旧计划放在阶段0 | **不在本计划**——归 OA 模块阶段1（Wf 命名空间）。本计划只产组织主数据，确保 OA 能消费 `LeaderId/ManagerId/DeptId` |

---

## File Structure

| 文件 | 职责 | 动作 |
|---|---|---|
| `CP6.Entity/DomainModels/Sys/Sys_Dept.cs` | 部门树实体（DeptCode/DeptName/ParentId/Path/LeaderId/Sort/Enable） | Create |
| `CP6.Entity/DomainModels/Sys/Sys_User.cs` | 补 `DeptId/ManagerId/Email` | Modify |
| `CP6.Entity/DTOs/Sys/DeptDtos.cs` | DeptDto / DeptTreeNode / MoveDeptReq / UserOrgDto | Create |
| `CP6.Core/EFDbContext/CP6Context.cs` | `DbSet<Sys_Dept>` + OnModelCreating 索引 | Modify |
| `CP6.Core/Services/Sys/IDeptService.cs` `DeptService.cs` | 部门 CRUD + Path 维护 + 移动 + 删除三校验 + 树查询 | Create |
| `CP6.WebApi/Controllers/Sys/DeptController.cs` | 部门 REST + 用户组织字段 | Create |
| `CP6.WebApi/Program.cs` | DI 注册 `IDeptService` | Modify |
| `CP6.Core/Migrations/*_PubB0OrgModel.cs` | EF 迁移（建 Sys_Dept + Sys_User 补列） | Create |
| `cp6.web/src/types/sys/dept.ts` | 类型 | Create |
| `cp6.web/src/api/sys/dept.ts` | API 封装 | Create |
| `cp6.web/src/views/pub/dept/DeptTreeView.vue` | 部门树管理页（左树+右表单） | Create |
| `cp6.web/src/router/` | 加 PUB 部门管理路由 | Modify |
| `CP6.Tests/DeptServiceTests.cs` | 部门服务测试（Path/移动/删除校验） | Create |

---

## 实施分两段

- **Phase A**（A-1..A-3）：实体 + 迁移 + 部门服务（CRUD/Path/移动/删除/树）+ 控制器 — 后端可用
- **Phase B**（B-1..B-2）：前端部门树管理页 + 用户组织字段维护

---

# Phase A — 后端组织模型

## Task A-1: Sys_Dept 实体 + Sys_User 补三字段 + DbSet/索引 + 迁移

**Files:** Create `Sys_Dept.cs`; Modify `Sys_User.cs`, `CP6Context.cs`; Create migration; Test `CP6.Tests/DeptServiceTests.cs`（落库往返）

- [ ] **Step 1: 写失败测试（落库往返 + Sys_User 三字段）** `[InMemory]`

```csharp
// DeptServiceTests.cs（首测）
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

public class DeptServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Dept_And_UserOrgFields_RoundTrip()
    {
        using var db = Db();
        var deptId = Guid.NewGuid();
        db.Sys_Depts.Add(new Sys_Dept { Id = deptId, DeptCode = "HQ", DeptName = "総本部", Path = $"/{deptId}/" });
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "u1", Password = "x", DeptId = deptId, Email = "u1@x.com" });
        await db.SaveChangesAsync();
        Assert.Equal("HQ", (await db.Sys_Depts.SingleAsync()).DeptCode);
        Assert.Equal(deptId, (await db.Sys_Users.SingleAsync()).DeptId);
    }
}
```

- [ ] **Step 2: 跑红** → Run: `dotnet test CP6.Tests --filter DeptServiceTests` → FAIL（`Sys_Depts`/`DeptId` 不存在）

- [ ] **Step 3: 写实体 + 补字段 + 注册**

```csharp
// Sys_Dept.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CP6.Entity.DomainModels.Sys;

/// <summary>部门（组织树节点）。PUB 数据权限子树过滤 + OA 审批路由部门长 共用。</summary>
[Table("Sys_Dept")]
public class Sys_Dept : BaseEntity
{
    public Guid?   ParentId { get; set; }              // 上级部门；根为 null
    [Required, MaxLength(50)]  public string DeptCode { get; set; } = "";   // 租户内唯一
    [Required, MaxLength(100)] public string DeptName { get; set; } = "";
    [MaxLength(900)]           public string Path     { get; set; } = "";   // 物化路径 /{rootId}/.../{selfId}/
    public Guid?   LeaderId { get; set; }              // 部门负责人 → Sys_User.Id
    public int     Sort     { get; set; }
    public bool    Enable   { get; set; } = true;
}
```

`Sys_User.cs` 末尾加（不动现有字段）：
```csharp
    /// <summary>所属部门 → Sys_Dept.Id（DataScope 本部门用）</summary>
    public Guid?   DeptId    { get; set; }
    /// <summary>直属上级 → Sys_User.Id（OA 直属上级路由用）</summary>
    public Guid?   ManagerId { get; set; }
    /// <summary>邮箱（通知用）</summary>
    [MaxLength(100)] public string? Email { get; set; }
```

`CP6Context.cs` DbSet 区（与 `Sys_Users` 相邻）：
```csharp
    /// <summary>部门（组织树）—— PUB 章00</summary>
    public DbSet<CP6.Entity.DomainModels.Sys.Sys_Dept> Sys_Depts { get; set; }
```
`OnModelCreating`（B0-D1：本阶段不带 TenantId）：
```csharp
    b.Entity<CP6.Entity.DomainModels.Sys.Sys_Dept>(e => {
        e.HasIndex(x => x.DeptCode).IsUnique();         // 章09 升级为 (TenantId, DeptCode)
        e.HasIndex(x => x.Path);                        // 子树前缀匹配
        e.HasIndex(x => x.ParentId);
    });
    b.Entity<CP6.Entity.DomainModels.Sys.Sys_User>().HasIndex(x => x.DeptId);
```

- [ ] **Step 4: 跑绿** → PASS

- [ ] **Step 5: 生成迁移**

Run: `dotnet ef migrations add PubB0OrgModel -p CP6.Core -s CP6.WebApi`
Expected: 迁移含 `CREATE TABLE Sys_Dept` + `ALTER TABLE Sys_User ADD DeptId/ManagerId/Email`。

- [ ] **Step 6: 提交**

```bash
git add CP6.Entity/DomainModels/Sys/Sys_Dept.cs CP6.Entity/DomainModels/Sys/Sys_User.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/DeptServiceTests.cs
git commit -m "feat(pub): Sys_Dept entity + Sys_User org fields + migration (ch00)"
```

---

## Task A-2: DeptService — CRUD + Path 维护 + 移动（子树重算·防成环）+ 删除三校验 + 树

**Files:** Create `IDeptService.cs` `DeptService.cs`, `CP6.Entity/DTOs/Sys/DeptDtos.cs`; Test `DeptServiceTests.cs`

- [ ] **Step 1: 追加失败测试（新增根/子 Path；移动重算子树 + 防成环 E-004；删有子 E-002 / 有用户 E-003；编码重复 E-001）**

```csharp
    private static (CP6Context, DeptService) Make()
    { var db = Db(); return (db, new DeptService(db)); }

    [Fact]
    public async Task Create_Root_And_Child_BuildsPath()
    {
        var (db, svc) = Make();
        var rootId = await svc.CreateAsync(new DeptDto { DeptCode = "HQ", DeptName = "本部" }, null, "u");
        var childId = await svc.CreateAsync(new DeptDto { DeptCode = "SALES", DeptName = "営業" }, rootId, "u");
        var root = await db.Sys_Depts.FindAsync(rootId);
        var child = await db.Sys_Depts.FindAsync(childId);
        Assert.Equal($"/{rootId}/", root!.Path);
        Assert.Equal($"/{rootId}/{childId}/", child!.Path);
    }

    [Fact]
    public async Task Move_RecomputesSubtreePaths()
    {
        var (db, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode="A", DeptName="A" }, null, "u");
        var b = await svc.CreateAsync(new DeptDto { DeptCode="B", DeptName="B" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode="A1", DeptName="A1" }, a, "u");   // /a/a1/
        await svc.MoveAsync(a, b, "u");                                                          // A 挂到 B 下
        Assert.Equal($"/{b}/{a}/", (await db.Sys_Depts.FindAsync(a))!.Path);
        Assert.Equal($"/{b}/{a}/{a1}/", (await db.Sys_Depts.FindAsync(a1))!.Path);              // 子孙整体平移
    }

    [Fact]
    public async Task Move_IntoOwnSubtree_Throws_E004()
    {
        var (db, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode="A", DeptName="A" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode="A1", DeptName="A1" }, a, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MoveAsync(a, a1, "u"));
        Assert.Equal("E-PUB-004", ex.Message);
    }

    [Fact]
    public async Task Delete_WithChildren_E002_WithUsers_E003()
    {
        var (db, svc) = Make();
        var a = await svc.CreateAsync(new DeptDto { DeptCode="A", DeptName="A" }, null, "u");
        var a1 = await svc.CreateAsync(new DeptDto { DeptCode="A1", DeptName="A1" }, a, "u");
        Assert.Equal("E-PUB-002", (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(a))).Message);
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName="x", Password="x", DeptId = a1 });
        await db.SaveChangesAsync();
        Assert.Equal("E-PUB-003", (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(a1))).Message);
    }
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现 DTO + DeptService**

```csharp
// DeptDtos.cs
namespace CP6.Entity.DTOs.Sys;
public class DeptDto { public Guid? Id; public Guid? ParentId; public string DeptCode=""; public string DeptName=""; public Guid? LeaderId; public int Sort; public bool Enable=true; }
public class DeptTreeNode { public Guid Id; public Guid? ParentId; public string DeptCode=""; public string DeptName=""; public Guid? LeaderId; public string? LeaderName; public int Sort; public bool Enable; public List<DeptTreeNode> Children = new(); }
public class UserOrgDto { public Guid? DeptId; public Guid? ManagerId; public string? Email; }
```

```csharp
// IDeptService.cs
public interface IDeptService
{
    Task<List<DeptTreeNode>> TreeAsync();
    Task<Guid> CreateAsync(DeptDto dto, Guid? parentId, string? user);
    Task UpdateAsync(Guid id, DeptDto dto, string? user);        // DeptCode 只读
    Task MoveAsync(Guid id, Guid? newParentId, string? user);
    Task DeleteAsync(Guid id);
    Task SetLeaderAsync(Guid id, Guid? leaderId, string? user);
    Task SetUserOrgAsync(Guid userId, UserOrgDto dto, string? user);
}
```

```csharp
// DeptService.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public class DeptService : IDeptService
{
    private readonly CP6Context _db;
    public DeptService(CP6Context db) => _db = db;

    public async Task<Guid> CreateAsync(DeptDto d, Guid? parentId, string? user)
    {
        if (await _db.Sys_Depts.AnyAsync(x => x.DeptCode == d.DeptCode))
            throw new InvalidOperationException("E-PUB-001");
        var id = Guid.NewGuid();
        var parentPath = "/";
        if (parentId is not null)
            parentPath = (await _db.Sys_Depts.FindAsync(parentId))?.Path
                         ?? throw new InvalidOperationException("E-PUB-001");
        var e = new Sys_Dept { Id = id, ParentId = parentId, DeptCode = d.DeptCode, DeptName = d.DeptName,
            Path = $"{parentPath}{id}/", LeaderId = d.LeaderId, Sort = d.Sort, Enable = d.Enable,
            Creator = user, CreateDate = DateTime.Now };
        _db.Sys_Depts.Add(e);
        await _db.SaveChangesAsync();
        return id;
    }

    public async Task MoveAsync(Guid id, Guid? newParentId, string? user)
    {
        var dept = await _db.Sys_Depts.FindAsync(id) ?? throw new InvalidOperationException("E-PUB-001");
        var newParentPath = "/";
        if (newParentId is not null)
        {
            var np = await _db.Sys_Depts.FindAsync(newParentId) ?? throw new InvalidOperationException("E-PUB-001");
            if (np.Path.StartsWith(dept.Path)) throw new InvalidOperationException("E-PUB-004");  // 防成环：目标在自身子树内
            newParentPath = np.Path;
        }
        var oldPrefix = dept.Path;
        var newPrefix = $"{newParentPath}{id}/";
        var subtree = await _db.Sys_Depts.Where(x => x.Path.StartsWith(oldPrefix)).ToListAsync();
        foreach (var s in subtree) s.Path = newPrefix + s.Path.Substring(oldPrefix.Length);   // 旧前缀→新前缀
        dept.ParentId = newParentId; dept.Modifier = user; dept.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _db.Sys_Depts.AnyAsync(x => x.ParentId == id))     throw new InvalidOperationException("E-PUB-002");
        if (await _db.Sys_Users.AnyAsync(x => x.DeptId == id))       throw new InvalidOperationException("E-PUB-003");
        var e = await _db.Sys_Depts.FindAsync(id);
        if (e != null) { _db.Sys_Depts.Remove(e); await _db.SaveChangesAsync(); }
    }

    public async Task<List<DeptTreeNode>> TreeAsync()
    {
        var all = await _db.Sys_Depts.OrderBy(x => x.Sort).ToListAsync();
        var leaders = await _db.Sys_Users.Where(u => all.Select(d => d.LeaderId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NickName ?? u.UserName);
        var nodes = all.ToDictionary(d => d.Id, d => new DeptTreeNode { Id = d.Id, ParentId = d.ParentId,
            DeptCode = d.DeptCode, DeptName = d.DeptName, LeaderId = d.LeaderId,
            LeaderName = d.LeaderId is Guid lid && leaders.TryGetValue(lid, out var n) ? n : null,
            Sort = d.Sort, Enable = d.Enable });
        var roots = new List<DeptTreeNode>();
        foreach (var node in nodes.Values)
            if (node.ParentId is Guid pid && nodes.TryGetValue(pid, out var p)) p.Children.Add(node);
            else roots.Add(node);
        return roots;
    }
    // UpdateAsync（DeptCode 只读，改 Name/Leader/Sort/Enable）/ SetLeaderAsync / SetUserOrgAsync 同模式实现
}
```

- [ ] **Step 4: 跑绿** → Run: `dotnet test CP6.Tests --filter DeptServiceTests` → PASS

- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(pub): DeptService CRUD + materialized path + move/delete guards (ch00 §3/§7)"`

---

## Task A-3: DeptController + DI

**Files:** Create `CP6.WebApi/Controllers/Sys/DeptController.cs`; Modify `Program.cs`

- [ ] **Step 1: 写控制器（仿 MachineController：`{code,message,data}`，业务异常→400）**

```csharp
// DeptController.cs
[ApiController]
[Route("api/pub/dept")]
[Authorize]
public class DeptController : ControllerBase
{
    private readonly IDeptService _svc;
    public DeptController(IDeptService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? d = null) => Ok(new { code = 0, message = "OK", data = d });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("tree")]          public async Task<IActionResult> Tree() => Ok2(await _svc.TreeAsync());
    [HttpPost]                 public async Task<IActionResult> Create([FromBody] DeptDto d) { try { return Ok2(new { id = await _svc.CreateAsync(d, d.ParentId, CurrentUser) }); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpPut("{id}")]          public async Task<IActionResult> Update(Guid id, [FromBody] DeptDto d) { try { await _svc.UpdateAsync(id, d, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpDelete("{id}")]       public async Task<IActionResult> Delete(Guid id) { try { await _svc.DeleteAsync(id); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpPost("{id}/move")]    public async Task<IActionResult> Move(Guid id, [FromBody] MoveReq r) { try { await _svc.MoveAsync(id, r.NewParentId, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpPut("{id}/leader")]   public async Task<IActionResult> Leader(Guid id, [FromBody] LeaderReq r) { try { await _svc.SetLeaderAsync(id, r.LeaderId, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    public record MoveReq(Guid? NewParentId);
    public record LeaderReq(Guid? LeaderId);
}
// 用户组织字段：在用户管理控制器加 PUT /api/pub/user/{id}/org → _svc.SetUserOrgAsync（或本控制器代理）
```

- [ ] **Step 2: DI 注册 + 构建**

`Program.cs` 服务区加：`builder.Services.AddScoped<CP6.Core.Services.Sys.IDeptService, CP6.Core.Services.Sys.DeptService>();`
Run: `dotnet build` → succeeded

- [ ] **Step 3: 提交** → `git add -A && git commit -m "feat(pub): dept controller + DI (ch00 §10)"`

---

# Phase B — 前端部门树管理

## Task B-1: 类型 + API + 部门树管理页（章00 §4）

**Files:** Create `cp6.web/src/types/sys/dept.ts`, `cp6.web/src/api/sys/dept.ts`, `cp6.web/src/views/pub/dept/DeptTreeView.vue`; Modify router

- [ ] **Step 1: 类型 + API**

```ts
// types/sys/dept.ts
export interface DeptTreeNode { id:string; parentId?:string|null; deptCode:string; deptName:string;
  leaderId?:string|null; leaderName?:string|null; sort:number; enable:boolean; children:DeptTreeNode[] }
export interface DeptDto { id?:string; parentId?:string|null; deptCode:string; deptName:string; leaderId?:string|null; sort:number; enable:boolean }
export type Envelope<T> = { code:number; message:string; data:T }
```
```ts
// api/sys/dept.ts
import http from '../http'
import type { Envelope, DeptTreeNode, DeptDto } from '@/types/sys/dept'
export const deptApi = {
  tree() { return http.get<any, Envelope<DeptTreeNode[]>>('/pub/dept/tree') },
  create(d: DeptDto) { return http.post<any, Envelope<{ id:string }>>('/pub/dept', d) },
  update(id: string, d: DeptDto) { return http.put<any, Envelope<any>>(`/pub/dept/${id}`, d) },
  remove(id: string) { return http.delete<any, Envelope<any>>(`/pub/dept/${id}`) },
  move(id: string, newParentId: string | null) { return http.post<any, Envelope<any>>(`/pub/dept/${id}/move`, { newParentId }) },
}
```

- [ ] **Step 2: 写 DeptTreeView.vue（左 el-tree + 右详情表单，章00 §4）**

```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { deptApi } from '@/api/sys/dept'
import type { DeptTreeNode, DeptDto } from '@/types/sys/dept'

const tree = ref<DeptTreeNode[]>([]); const current = ref<DeptDto | null>(null)
async function reload() { tree.value = (await deptApi.tree()).data }
onMounted(reload)
function selectNode(n: DeptTreeNode) { current.value = { ...n } }   // DeptCode 编辑态只读（见模板）
async function save() {
  if (!current.value) return
  try {
    if (current.value.id) await deptApi.update(current.value.id, current.value)
    else await deptApi.create(current.value)
    ElMessage.success('已保存'); await reload()
  } catch (e:any) { /* http.ts 已 toast 业务错误码 */ }
}
async function del(n: DeptTreeNode) {
  await ElMessageBox.confirm(`删除部门「${n.deptName}」？`, '确认')
  try { await deptApi.remove(n.id); await reload() } catch {}
}
// 拖拽移动：el-tree @node-drop → deptApi.move(dragId, dropParentId)（成环 E-PUB-004 由后端拦，toast 提示）
</script>
<template>
  <div class="dept-page">
    <aside class="tree">
      <div class="toolbar"><!-- 新增根/子部门、删除、刷新 --></div>
      <el-tree :data="tree" node-key="id" draggable
        :props="{ label:'deptName', children:'children' }"
        @node-click="selectNode" @node-drop="/* move */" />
    </aside>
    <section class="detail" v-if="current">
      <el-form label-width="90px">
        <el-form-item label="部门编码"><el-input v-model="current.deptCode" :disabled="!!current.id" /></el-form-item>
        <el-form-item label="部门名称"><el-input v-model="current.deptName" /></el-form-item>
        <el-form-item label="部门负责人"><!-- 用户选择弹出 → current.leaderId --></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="current.sort" /></el-form-item>
        <el-form-item label="启用"><el-switch v-model="current.enable" /></el-form-item>
        <el-button type="primary" @click="save">保存</el-button>
      </el-form>
    </section>
  </div>
</template>
```

- [ ] **Step 3: 加路由 + 冒烟**

路由加 `{ path:'/pub/dept', name:'pub-dept', component: () => import('@/views/pub/dept/DeptTreeView.vue') }`；e2e：打开页→树渲染→新增部门→出现在树。

- [ ] **Step 4: 提交** → `git add -A && git commit -m "feat(pub): dept tree management page (ch00 §4)"`

---

## Task B-2: 用户组织字段维护（DeptId/ManagerId/Email，章00 §5.2）

**Files:** Modify 用户管理页（或在 DeptTreeView 旁加用户分配）+ `api/sys`（user/{id}/org）

- [ ] **Step 1: 实现**——用户管理页加"所属部门(树选)/直属上级(用户选)/邮箱"三字段 → `PUT /pub/user/{id}/org`（后端 SetUserOrgAsync）。
- [ ] **Step 2: 冒烟 + 提交** → `git commit -m "feat(pub): user org fields (dept/manager/email) maintenance (ch00 §5.2)"`

---

## Self-Review（对照章00 覆盖）

- 数据模型 Sys_Dept + Sys_User 三字段(A-1) ✅ / 物化路径维护(A-2 Create/Move) ✅ / 部门管理画面(B-1) ✅ / 字段明细+控制矩阵(B-1 DeptCode 只读) ✅ / 处理详细 CRUD+Path重算+删除三校验+防成环(A-2) ✅ / API(A-3) ✅ / 消息 E-PUB-001~004(A-2) ✅ / 用户组织字段(B-2) ✅
- **审批人解析器 IApproverResolver（章00 §8）= 不在本计划**（B0-D5，归 OA 阶段1）。
- **DataScope 用法（章00 §9）= 仅保证 Path 正确**，查询注入在 PUB 权限引擎计划章03。

**已知缺口/推迟：**
1. TenantId（B0-D1）—— 推迟到 PUB 章09 统一给 Sys 全族 + EF 全局过滤。
2. IApproverResolver —— OA 阶段1。
3. 删除校验③"被审批流引用"（章00 §7.3）—— OA 落地后接入（本阶段只做①②）。

**Type 一致性：** `Sys_Dept.Path`(A-1) ↔ `DeptService.Move/Create` 前缀逻辑(A-2)；`DeptTreeNode/DeptDto`(A-2/B-1) 前后端对齐；`Sys_User.DeptId/ManagerId`(A-1) ↔ OA 审批路由（后续 OA 计划消费）。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-pub-b0-org-model.md`（**取代** 2026-06-10-approval-stage0-org-model.md）。这是 **PUB 第一份（B0 根）**。后续：
- PUB Plan 2 = `2026-06-13-pub-b1-permission.md`（章01 多角色 → 02 功能权限 → 03 数据权限 → 04 字段级，B1 权限底座）
- PUB Plan 3 = `2026-06-13-pub-common-modules.md`（章05~09 公共模组 + 集成）

**下一步按工作流是你修订**（拍板 B0-D1~D5，尤其 D1 TenantId 是否全系统统一）。定稿后建议先落 B0（一切的根），再 B1，再公共模组。

---

*初稿生成于 2026-06-13。源：docs/pub/00-org-model.md（引用 PUB README）。已勘察 CP6 真实代码：Sys_User(GUID 主键/RoleId int)、Sys_Role(int PK)、零多租户、BaseEntity 审计字段、xUnit+InMemory；并对账取代 2026-06-10 旧组织模型计划。*
