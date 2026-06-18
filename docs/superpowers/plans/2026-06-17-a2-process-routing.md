# A2 工艺路线完善（标准工时 + 工序费率 + 实绩工时 + 成本做真）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把制造成本的 工(Labor)/费(Overhead) 从"传入估算"做真——标准机时/人工工时打底、ProductionResult 派生实际机时/人工工时、WorkCenter 费率算真实 Labor/Overhead，结转仍走实际成本法、差异只展示不入 GL，并给 MRP P4 留 CRP 产能地基。

**Architecture:** 新建 Mes 主数据 `WorkCenter`/`ProcessCostRate`（工/费双率 + 生效区间）；扩展 `ProductProcess`（段取/单件/标准人数）、`WorkOrderProcess`（机时/人工工时 + 来源标记）、`ProductionResult`（显式工时）、`CostSheet/CostSheetLine`（实际/标准 + 追溯）。`ProductionResultService` 报工后派生物化双工时；`CostCollectService` 改造为 工时×费率（缺工时→标准回退、缺费率→严格阻断/迁移回退双模式）；`CostSettleService` 贷实际额。前端补主数据维护 + 成本单展示。全程跨模块同步直读（Fin→Mes，同既有 Fin→Erp 读 ProductMaterial）。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory / Vue 3.5 + element-plus。spec：`docs/superpowers/specs/2026-06-17-a2-process-routing-design.md`（A2-D1~D5，全量 v2）。

---

## 关键既有约定（落码前必读）

- **测试基建**：`TestHelper.CreateInMemoryContext()` 建独立 InMemory DB；每测独立。`CP6.Tests/GlobalUsings.cs` 含 `CP6.Entity.DomainModels.Erp/Plan/Sys/...` 与 `DTOs.Mes`，但**不含** `CP6.Entity.DomainModels.Mes` / `CP6.Core.Services.Mes` / `CP6.Core.Services.Fin`——测试文件按需 `using`（参 `MrpEngineTests` 用 `using CP6.Entity.DomainModels.Mes;`）。
- **迁移**：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`（**会先构建**；不要带 `--no-build`，否则用旧程序集生成空迁移）。
- **多租户**：新实体继承 `BaseBizEntity`（自带 `Id`/审计/`IsDeleted`/`RowVersion`/`TenantId`）；唯一索引在 `CP6Context.OnModelCreating` 末尾反射重写自动补 `TenantId` 前缀，声明单列/复合唯一即可（参 A1 `Plan_ItemPlanningPolicy`）。
- **控制器**：`[ApiController]`+`[Route]`+`[Authorize]`+`ControllerBase`；私有 `Ok2(data)=>{code:0,message:"OK",data}`、`Err(ioe)=>BadRequest{code:400,message}`、`CurrentUser=>User?.Identity?.Name`（参 `MrpController`/`ItemPlanningPolicyController`）。
- **CRUD/Resolve 样板**：`ItemPlanningPolicyService`（A1）、`SupplierPriceService`（生效日取价）现成可照搬。
- **成本服务现状**：`CostCollectService.CollectAsync(workOrderNo, laborStd, overheadStd, user)`——料真、工/费传入估算；`CostSettleService.SettleAsync` 贷 `INVENTORY`/`DIRECT_LABOR`(=LaborStd)/`MFG_OVERHEAD`(=OverheadStd)，WIP→FG 按 `TotalActual`。`FinResult.Pass()/Fail(code)` 在 `CP6.Core/Services/Fin/FinResult.cs`。

---

## File Structure

### 新建（Mes 实体 + 服务 + 控制器）
- `CP6.Entity/DomainModels/Mes/WorkCenter.cs`、`ProcessCostRate.cs`
- `CP6.Core/Services/Mes/IWorkCenterService.cs`/`WorkCenterService.cs`、`IProcessCostRateService.cs`/`ProcessCostRateService.cs`
- `CP6.WebApi/Controllers/Mes/WorkCenterController.cs`、`ProcessCostRateController.cs`

### 扩展（实体）
- `CP6.Entity/DomainModels/Erp/ProductProcess.cs`（+3）、`Mes/WorkOrderProcess.cs`（+6）、`Mes/ProductionResult.cs`（+2）、`Fin/CostSheet.cs`（改 4 字段 + NotMapped）、`Fin/CostSheet.cs` 内 `CostSheetLine`（+9）

### 扩展（服务）
- `CP6.Core/Services/Mes/ProductionResultService.cs`（报工后派生物化双工时）
- `CP6.Core/Services/Fin/CostCollectService.cs`（工费做真）、`CostSettleService.cs`（贷实际额）

### 前端 + 装配 + 迁移
- `cp6.web/src/{api,types}/mes/processCost.ts`、`views/mes/{WorkCenterView,ProcessCostRateView}.vue`；改 `views/fin/CostSheetView.vue`、`views/erp/ProductMaster*`（工程页 3 字段）
- `CP6.WebApi/Program.cs`（DI + 菜单 + i18n 接入）、`CP6.WebApi/Seed/I18nA2ScreenSeed.cs`
- 迁移 `*_A2ProcessRoutingCostTruth`

### 测试
- `CP6.Tests/{WorkCenterServiceTests,ProcessCostRateServiceTests,ProductionResultHourTests,CostCollectLaborOverheadTests,CostSettleActualTests}.cs`

---

## 实施分五阶段

- **Phase A**（A-1..A-2）：Mes 主数据 WorkCenter + ProcessCostRate（含费率期间重叠校验 + Resolve）
- **Phase B**（B-1）：ProductProcess 工时字段 + 迁移
- **Phase C**（C-1..C-2）：WorkOrderProcess/ProductionResult 字段 + ProductionResultService 双工时派生
- **Phase D**（D-1..D-3）：CostSheet/Line 改造 + CostCollect 做真（双模式）+ CostSettle 贷实际
- **Phase E**（E-1..E-2）：前端主数据/成本单 + DI/菜单/i18n + gstack QA 收口

---

# Phase A — Mes 主数据（WorkCenter + ProcessCostRate）

## Task A-1: WorkCenter 实体 + 服务 + 控制器 + 迁移（spec §3.2/§4.1）

**Files:**
- Create: `CP6.Entity/DomainModels/Mes/WorkCenter.cs`、`CP6.Core/Services/Mes/IWorkCenterService.cs`、`WorkCenterService.cs`、`CP6.WebApi/Controllers/Mes/WorkCenterController.cs`、`CP6.Tests/WorkCenterServiceTests.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 索引）、`CP6.WebApi/Program.cs`（DI）

- [ ] **Step 1: 写失败测试** `CP6.Tests/WorkCenterServiceTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class WorkCenterServiceTests
{
    private static WorkCenterService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new WorkCenterService(db);
    }

    [Fact]
    public async Task Upsert_Insert_ThenUpdate_SameWgCd()
    {
        var svc = Create(out var db);
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT", WgName = "印刷", DailyCapacityHours = 16 }, "admin");
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT", WgName = "印刷機", DailyCapacityHours = 20 }, "admin");

        var rows = await db.WorkCenters.Where(x => x.WgCd == "PRINT" && !x.IsDeleted).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("印刷機", rows[0].WgName);
        Assert.Equal(20m, rows[0].DailyCapacityHours);
    }

    [Fact]
    public async Task Upsert_NegativeCapacity_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(new WorkCenter { WgCd = "X", DailyCapacityHours = -1 }, "admin"));
    }

    [Fact]
    public async Task List_FiltersKeyword_ExcludesDeleted()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(new WorkCenter { WgCd = "PRINT" }, "admin");
        await svc.UpsertAsync(new WorkCenter { WgCd = "DIECUT" }, "admin");
        await svc.DeleteAsync("DIECUT", "admin");

        var all = await svc.ListAsync(null);
        Assert.Single(all);
        Assert.Equal("PRINT", all[0].WgCd);
    }
}
```

- [ ] **Step 2: 跑红** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~WorkCenterService" --nologo`，预期编译失败（类型缺失）。

- [ ] **Step 3: 实现实体** `CP6.Entity/DomainModels/Mes/WorkCenter.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>工作中心主数据（A2 · spec §3.2）。费率与产能挂载点；产能字段=CRP 地基。</summary>
[Table("T_WorkCenter")]
public class WorkCenter : BaseBizEntity
{
    /// <summary>工作中心CD（业务键，唯一；= ProductProcess.WgCd / WorkOrderProcess.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>工作中心名称</summary>
    [MaxLength(100)] public string? WgName { get; set; }
    /// <summary>日可用产能（h/日）——CRP 入参地基，A2 只维护不消费</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? DailyCapacityHours { get; set; }
    /// <summary>启用</summary>
    public bool Enable { get; set; } = true;
}
```

- [ ] **Step 4: 实现服务**（照 `ItemPlanningPolicyService` CRUD 模式）

`IWorkCenterService.cs`：
```csharp
using CP6.Entity.DomainModels.Mes;
namespace CP6.Core.Services.Mes;

public interface IWorkCenterService
{
    Task<List<WorkCenter>> ListAsync(string? keyword);
    Task<WorkCenter?> GetAsync(string wgCd);
    Task UpsertAsync(WorkCenter dto, string? user);
    Task DeleteAsync(string wgCd, string? user);
}
```

`WorkCenterService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public class WorkCenterService : IWorkCenterService
{
    private readonly CP6Context _db;
    public WorkCenterService(CP6Context db) => _db = db;

    public async Task<List<WorkCenter>> ListAsync(string? keyword)
    {
        var q = _db.WorkCenters.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(x => x.WgCd.Contains(keyword) || (x.WgName != null && x.WgName.Contains(keyword)));
        return await q.OrderBy(x => x.WgCd).ToListAsync();
    }

    public Task<WorkCenter?> GetAsync(string wgCd)
        => _db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(x => x.WgCd == wgCd && !x.IsDeleted);

    public async Task UpsertAsync(WorkCenter dto, string? user)
    {
        if (string.IsNullOrWhiteSpace(dto.WgCd))
            throw new InvalidOperationException("E-A2-WC-001: 工作中心CD必填");
        if (dto.DailyCapacityHours is < 0m)
            throw new InvalidOperationException("E-A2-WC-003: 日可用产能不可为负");

        var existing = await _db.WorkCenters.FirstOrDefaultAsync(x => x.WgCd == dto.WgCd && !x.IsDeleted);
        if (existing == null)
        {
            dto.Creator = user; dto.CreateDate = DateTime.Now;
            _db.WorkCenters.Add(dto);
        }
        else
        {
            existing.WgName = dto.WgName;
            existing.DailyCapacityHours = dto.DailyCapacityHours;
            existing.Enable = dto.Enable;
            existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string wgCd, string? user)
    {
        var row = await _db.WorkCenters.FirstOrDefaultAsync(x => x.WgCd == wgCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("E-A2-WC-001: 工作中心不存在");
        row.IsDeleted = true; row.Modifier = user; row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: 注册 DbSet + 索引** `CP6.Core/EFDbContext/CP6Context.cs`

在文件顶部确认 `using CP6.Entity.DomainModels.Mes;` 已存在（已存在）。新增 DbSet（采购/计划 DbSet 区域附近）：
```csharp
// ───── 工艺路线/成本（A2）─────
public DbSet<WorkCenter> WorkCenters { get; set; }
```
`OnModelCreating` 内（计划索引附近）：
```csharp
modelBuilder.Entity<WorkCenter>(e =>
    e.HasIndex(x => x.WgCd).IsUnique().HasDatabaseName("UX_Mes_WorkCenter_Wg"));
```

- [ ] **Step 6: DI 注册** `CP6.WebApi/Program.cs`（计划服务 DI 区域附近）：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Mes.IWorkCenterService, CP6.Core.Services.Mes.WorkCenterService>();
```

- [ ] **Step 7: 控制器** `CP6.WebApi/Controllers/Mes/WorkCenterController.cs`
```csharp
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController]
[Route("api/mes/work-center")]
[Authorize]
public class WorkCenterController : ControllerBase
{
    private readonly IWorkCenterService _svc;
    public WorkCenterController(IWorkCenterService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet] public async Task<IActionResult> List([FromQuery] string? keyword) => Ok2(await _svc.ListAsync(keyword));
    [HttpGet("{wgCd}")] public async Task<IActionResult> Get(string wgCd) => Ok2(await _svc.GetAsync(wgCd));

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] WorkCenter dto)
    { try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }

    [HttpDelete("{wgCd}")]
    public async Task<IActionResult> Delete(string wgCd)
    { try { await _svc.DeleteAsync(wgCd, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
}
```

- [ ] **Step 8: 迁移**
Run: `dotnet ef migrations add A2WorkCenter --project CP6.Core --startup-project CP6.WebApi`
验证生成的 `*_A2WorkCenter.cs` 含 `CreateTable("T_WorkCenter")` + 唯一索引 `columns: new[] { "TenantId", "WgCd" }`。

- [ ] **Step 9: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~WorkCenterService" --nologo`，预期 3 passed。

- [ ] **Step 10: 提交** → `git commit -m "feat(mes): WorkCenter master (capacity groundwork) + CRUD + migration (A2 §3.2)"`

---

## Task A-2: ProcessCostRate 实体 + 服务（Resolve + 期间重叠校验）+ 控制器 + 迁移（spec §3.3/§4.2）

**Files:**
- Create: `CP6.Entity/DomainModels/Mes/ProcessCostRate.cs`、`CP6.Core/Services/Mes/IProcessCostRateService.cs`、`ProcessCostRateService.cs`、`CP6.WebApi/Controllers/Mes/ProcessCostRateController.cs`、`CP6.Tests/ProcessCostRateServiceTests.cs`
- Modify: `CP6Context.cs`、`Program.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/ProcessCostRateServiceTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class ProcessCostRateServiceTests
{
    private static ProcessCostRateService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.WorkCenters.Add(new WorkCenter { WgCd = "PRINT", Enable = true });
        db.SaveChanges();
        return new ProcessCostRateService(db);
    }

    private static ProcessCostRate Rate(string wg, decimal l, decimal o, DateTime from, DateTime? to = null)
        => new() { WgCd = wg, LaborRate = l, OverheadRate = o, ValidFrom = from, ValidTo = to };

    [Fact]
    public async Task Resolve_TakesLatestEffective()
    {
        var svc = Create(out var db);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 5, 31)), "admin");
        await svc.UpsertAsync(Rate("PRINT", 90, 130, new(2026, 6, 1)), "admin");

        var r = await svc.ResolveAsync("PRINT", new(2026, 7, 1));
        Assert.NotNull(r);
        Assert.Equal(90m, r!.LaborRate);
        Assert.Equal(130m, r.OverheadRate);
    }

    [Fact]
    public async Task Resolve_Expired_NotTaken()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 5, 31)), "admin");
        Assert.Null(await svc.ResolveAsync("PRINT", new(2026, 7, 1)));
    }

    [Fact]
    public async Task Upsert_OverlappingPeriod_Throws()
    {
        var svc = Create(out _);
        await svc.UpsertAsync(Rate("PRINT", 80, 120, new(2026, 1, 1), new(2026, 6, 30)), "admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("PRINT", 90, 130, new(2026, 6, 1)), "admin"));   // 与上条 [1/1,6/30] 重叠
    }

    [Fact]
    public async Task Upsert_NegativeRate_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("PRINT", -1, 120, new(2026, 1, 1)), "admin"));
    }

    [Fact]
    public async Task Upsert_UnknownWorkCenter_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertAsync(Rate("NOPE", 80, 120, new(2026, 1, 1)), "admin"));
    }
}
```

- [ ] **Step 2: 跑红** → `--filter "FullyQualifiedName~ProcessCostRateService"`，预期编译失败。

- [ ] **Step 3: 实现实体** `CP6.Entity/DomainModels/Mes/ProcessCostRate.cs`
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>工序费率（A2 · spec §3.3）。工作中心×生效区间 的 工/费双率（元/h）。</summary>
[Table("T_ProcessCostRate")]
public class ProcessCostRate : BaseBizEntity
{
    /// <summary>工作中心CD（业务键 → WorkCenter.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>人工费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal LaborRate { get; set; }
    /// <summary>制造费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal OverheadRate { get; set; }
    /// <summary>生效日（Resolve 取 ≤ 基准日最新有效版本）</summary>
    public DateTime ValidFrom { get; set; }
    /// <summary>失效日（null = 长期）</summary>
    public DateTime? ValidTo { get; set; }
}
```

- [ ] **Step 4: 实现服务**

`IProcessCostRateService.cs`：
```csharp
using CP6.Entity.DomainModels.Mes;
namespace CP6.Core.Services.Mes;

public interface IProcessCostRateService
{
    Task<List<ProcessCostRate>> ListAsync(string? wgCd);
    Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate);
    Task UpsertAsync(ProcessCostRate dto, string? user);
    Task DeleteAsync(Guid id, string? user);
}
```

`ProcessCostRateService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public class ProcessCostRateService : IProcessCostRateService
{
    private readonly CP6Context _db;
    public ProcessCostRateService(CP6Context db) => _db = db;

    public async Task<List<ProcessCostRate>> ListAsync(string? wgCd)
    {
        var q = _db.ProcessCostRates.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(wgCd)) q = q.Where(x => x.WgCd == wgCd);
        return await q.OrderBy(x => x.WgCd).ThenByDescending(x => x.ValidFrom).ToListAsync();
    }

    public Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate)
        => _db.ProcessCostRates.AsNoTracking()
            .Where(x => !x.IsDeleted && x.WgCd == wgCd
                        && x.ValidFrom <= onDate
                        && (x.ValidTo == null || x.ValidTo >= onDate))
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(ProcessCostRate dto, string? user)
    {
        if (dto.LaborRate < 0m || dto.OverheadRate < 0m)
            throw new InvalidOperationException("E-A2-RATE-003: 费率不可为负");
        if (dto.ValidTo is { } vt && vt < dto.ValidFrom)
            throw new InvalidOperationException("E-A2-RATE-004: 失效日早于生效日");
        var wc = await _db.WorkCenters.AnyAsync(x => x.WgCd == dto.WgCd && !x.IsDeleted);
        if (!wc) throw new InvalidOperationException("E-A2-WC-001: 工作中心不存在");

        // 期间重叠校验：[newFrom,newTo??Max] 与同 WgCd 其它行 [oldFrom,oldTo??Max] 不得重叠
        var newTo = dto.ValidTo ?? DateTime.MaxValue;
        var siblings = await _db.ProcessCostRates
            .Where(x => !x.IsDeleted && x.WgCd == dto.WgCd && x.Id != dto.Id).ToListAsync();
        foreach (var s in siblings)
        {
            var sTo = s.ValidTo ?? DateTime.MaxValue;
            if (dto.ValidFrom <= sTo && s.ValidFrom <= newTo)
                throw new InvalidOperationException("E-A2-RATE-001: 费率生效区间重叠");
        }

        var existing = dto.Id != Guid.Empty
            ? await _db.ProcessCostRates.FirstOrDefaultAsync(x => x.Id == dto.Id && !x.IsDeleted)
            : null;
        if (existing == null)
        {
            dto.Creator = user; dto.CreateDate = DateTime.Now;
            _db.ProcessCostRates.Add(dto);
        }
        else
        {
            existing.LaborRate = dto.LaborRate; existing.OverheadRate = dto.OverheadRate;
            existing.ValidFrom = dto.ValidFrom; existing.ValidTo = dto.ValidTo;
            existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id, string? user)
    {
        var row = await _db.ProcessCostRates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new InvalidOperationException("E-A2-RATE-001: 费率不存在");
        row.IsDeleted = true; row.Modifier = user; row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: DbSet + 索引** `CP6Context.cs`
```csharp
public DbSet<ProcessCostRate> ProcessCostRates { get; set; }
```
```csharp
modelBuilder.Entity<ProcessCostRate>(e =>
{
    e.HasIndex(x => new { x.WgCd, x.ValidFrom }).HasDatabaseName("IX_Mes_ProcessCostRate_Wg_ValidFrom");
});
```
> 注：唯一(WgCd,ValidFrom) 由业务校验 + 期间重叠校验覆盖；不加 DB 唯一索引以免多租户重写叠加复杂度（与期间校验等效）。

- [ ] **Step 6: DI** `Program.cs`：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Mes.IProcessCostRateService, CP6.Core.Services.Mes.ProcessCostRateService>();
```

- [ ] **Step 7: 控制器** `CP6.WebApi/Controllers/Mes/ProcessCostRateController.cs`
```csharp
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController]
[Route("api/mes/process-cost-rate")]
[Authorize]
public class ProcessCostRateController : ControllerBase
{
    private readonly IProcessCostRateService _svc;
    public ProcessCostRateController(IProcessCostRateService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet] public async Task<IActionResult> List([FromQuery] string? wgCd) => Ok2(await _svc.ListAsync(wgCd));
    [HttpGet("resolve")] public async Task<IActionResult> Resolve([FromQuery] string wgCd, [FromQuery] DateTime onDate)
        => Ok2(await _svc.ResolveAsync(wgCd, onDate));
    [HttpPost("upsert")] public async Task<IActionResult> Upsert([FromBody] ProcessCostRate dto)
    { try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(Guid id)
    { try { await _svc.DeleteAsync(id, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
}
```

- [ ] **Step 8: 迁移** → `dotnet ef migrations add A2ProcessCostRate --project CP6.Core --startup-project CP6.WebApi`；验证含 `T_ProcessCostRate` + 索引。

- [ ] **Step 9: 跑绿** → `--filter "FullyQualifiedName~ProcessCostRateService"`，预期 5 passed。

- [ ] **Step 10: 提交** → `git commit -m "feat(mes): ProcessCostRate (labor/overhead dual rate, effective period + overlap guard) + Resolve (A2 §3.3)"`

---

# Phase B — 路线工时字段

## Task B-1: ProductProcess +SetupHour/CycleTime/StandardCrewSize + 迁移（spec §3.1）

**Files:** Modify `CP6.Entity/DomainModels/Erp/ProductProcess.cs`；迁移。

- [ ] **Step 1: 加字段**（追加到 `ProductProcess` 末尾，`LeadTime` 字段附近）
```csharp
// ───── A2 标准工时（spec §3.1）─────
/// <summary>段取工时（h，固定/批；与数量无关）。标准机时 = SetupHour + 数量 × CycleTime。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? SetupHour { get; set; }
/// <summary>单件加工工时（h/件）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? CycleTime { get; set; }
/// <summary>标准作业人数（标准人工工时 = 标准机时 × 人数；空按 1）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? StandardCrewSize { get; set; }
```

- [ ] **Step 2: 迁移** → `dotnet ef migrations add A2ProductProcessHours --project CP6.Core --startup-project CP6.WebApi`；验证含 `AddColumn SetupHour/CycleTime/StandardCrewSize` to `T_ProductProcess`。

- [ ] **Step 3: 跑全量回归** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿（仅加字段，不破坏）。

- [ ] **Step 4: 提交** → `git commit -m "feat(erp): ProductProcess standard-time fields (setup/cycle/crew) + migration (A2 §3.1)"`

---

# Phase C — 实绩工时采集

## Task C-1: WorkOrderProcess +6 / ProductionResult +2 字段 + 迁移（spec §3.4/§3.5）

**Files:** Modify `CP6.Entity/DomainModels/Mes/WorkOrderProcess.cs`、`ProductionResult.cs`；迁移。

- [ ] **Step 1: WorkOrderProcess 加字段**（末尾，`LeadTime` 附近）
```csharp
// ───── A2 实绩工时（spec §3.4）─────
/// <summary>实际机时（h，用于制造费用；按机器区间合并派生，可覆盖）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? ActualMachineHour { get; set; }
/// <summary>实际人工工时（h，用于直接人工；按作业者累加派生，可覆盖）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? ActualLaborHour { get; set; }
/// <summary>工时来源：Derived/Manual/Import/StandardFallback/LegacyFallback。</summary>
[MaxLength(30)] public string? ActualHourSource { get; set; }
/// <summary>是否人工覆盖（覆盖后重算跳过）。</summary>
public bool IsHourOverridden { get; set; } = false;
/// <summary>工时覆盖/回退/异常说明。</summary>
[MaxLength(500)] public string? HourRemark { get; set; }
/// <summary>工时最近计算时间。</summary>
public DateTime? HourCalculatedTime { get; set; }
```

- [ ] **Step 2: ProductionResult 加字段**（末尾）
```csharp
// ───── A2 显式工时覆盖（spec §3.5）─────
/// <summary>本次报工人工工时（h，可选；填则本行人工工时用此值）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? LaborHour { get; set; }
/// <summary>本次报工机时（h，可选；填则本行机时用此值）。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? MachineHour { get; set; }
```

- [ ] **Step 3: 迁移** → `dotnet ef migrations add A2WorkOrderProcessHours --project CP6.Core --startup-project CP6.WebApi`；验证两表 AddColumn。

- [ ] **Step 4: 跑全量回归** → 全绿。

- [ ] **Step 5: 提交** → `git commit -m "feat(mes): WorkOrderProcess actual machine/labor hours + ProductionResult explicit hours (A2 §3.4/§3.5)"`

## Task C-2: ProductionResultService 双工时派生物化（spec §4.3）★

**Files:** Modify `CP6.Core/Services/Mes/ProductionResultService.cs`；Test `CP6.Tests/ProductionResultHourTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/ProductionResultHourTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class ProductionResultHourTests
{
    private static ProductionResultService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new ProductionResultService(db);
    }

    private static ProductionResult R(string wo, string proc, string op, string? mc,
        DateTime s, DateTime e, int type = 4)
        => new() { ResultNo = Guid.NewGuid().ToString("N")[..8], WorkOrderNo = wo, ProcessCd = proc,
                   OperatorCd = op, MachineCd = mc, ActualStartTime = s, ActualEndTime = e, ResultType = type };

    private static async Task SeedWop(CP6.Core.EFDbContext.CP6Context db, string wo, string proc)
    {
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = wo, ProcessCd = proc, TaskCd = "T1" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MachineHour_MergesIntervals_LaborHour_AccumulatesByOperator()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO1", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        // 张三、李四 同机 M1 同区间 08:00-10:00
        db.Set<ProductionResult>().AddRange(
            R("WO1", "P1", "z3", "M1", d, d.AddHours(2)),
            R("WO1", "P1", "l4", "M1", d, d.AddHours(2)));
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO1", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO1" && x.ProcessCd == "P1");
        Assert.Equal(2m, wop.ActualMachineHour);   // 机器区间合并：2h，非 4h
        Assert.Equal(4m, wop.ActualLaborHour);     // 人工按作业者累加：4h
        Assert.Equal("Derived", wop.ActualHourSource);
    }

    [Fact]
    public async Task ExplicitHours_OverrideTimestamps()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO2", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        var r = R("WO2", "P1", "z3", "M1", d, d.AddHours(2));
        r.LaborHour = 1.5m; r.MachineHour = 1.0m;   // 显式工时优先
        db.Set<ProductionResult>().Add(r);
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO2", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO2");
        Assert.Equal(1.0m, wop.ActualMachineHour);
        Assert.Equal(1.5m, wop.ActualLaborHour);
    }

    [Fact]
    public async Task Overridden_NotRecalculated()
    {
        var svc = Create(out var db);
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess
        { WorkOrderNo = "WO3", ProcessCd = "P1", TaskCd = "T1",
          ActualMachineHour = 9, ActualLaborHour = 9, IsHourOverridden = true, ActualHourSource = "Manual" });
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        db.Set<ProductionResult>().Add(R("WO3", "P1", "z3", "M1", d, d.AddHours(2)));
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO3", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO3");
        Assert.Equal(9m, wop.ActualMachineHour);   // 覆盖态不动
        Assert.Equal("Manual", wop.ActualHourSource);
    }

    [Fact]
    public async Task Interrupt_ClosedPair_Deducted()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO4", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        // 运行 08:00-12:00（4h），中断 09:00-10:00（1h）→ 净 3h
        db.Set<ProductionResult>().AddRange(
            R("WO4", "P1", "z3", "M1", d, d.AddHours(4)),
            R("WO4", "P1", "z3", "M1", d.AddHours(1), d.AddHours(2), type: 2)); // 2=中断
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO4", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO4");
        Assert.Equal(3m, wop.ActualLaborHour);
    }
}
```

- [ ] **Step 2: 跑红** → `--filter "FullyQualifiedName~ProductionResultHour"`，预期 `RecalculateProcessHoursAsync` 不存在。

- [ ] **Step 3: 实现** `ProductionResultService.RecalculateProcessHoursAsync`

在类内加（公共方法 + 私有区间助手）：
```csharp
/// <summary>报工后重算并物化工序双工时（spec §4.3）。覆盖态(IsHourOverridden)跳过。</summary>
public async Task RecalculateProcessHoursAsync(string workOrderNo, string processCd, string? taskCd, string? user)
{
    var wop = await _db.Set<WorkOrderProcess>()
        .FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && x.ProcessCd == processCd
                                  && (taskCd == null || x.TaskCd == taskCd) && !x.IsDeleted);
    if (wop == null || wop.IsHourOverridden) return;

    var results = await _db.Set<ProductionResult>().AsNoTracking()
        .Where(r => r.WorkOrderNo == workOrderNo && r.ProcessCd == processCd
                    && (taskCd == null || r.TaskCd == taskCd) && !r.IsDeleted)
        .ToListAsync();

    // 中断区间（2=中断 起，3=中断解除；本行 Start..End 视为中断区间；type=2 带 End 直接成对）
    var interrupts = results.Where(r => r.ResultType == 2 && r.ActualStartTime != null && r.ActualEndTime != null)
        .Select(r => (Start: r.ActualStartTime!.Value, End: r.ActualEndTime!.Value))
        .Where(iv => iv.End > iv.Start).ToList();

    decimal Net(DateTime s, DateTime e)
    {
        var gross = (decimal)(e - s).TotalHours;
        foreach (var iv in interrupts)
        {
            var os = iv.Start > s ? iv.Start : s;
            var oe = iv.End < e ? iv.End : e;
            if (oe > os) gross -= (decimal)(oe - os).TotalHours;
        }
        return gross < 0 ? 0 : gross;
    }

    // 运行行：完了(4)/数量報告(5)，排除中断行
    var runRows = results.Where(r => r.ResultType is 4 or 5).ToList();

    // 人工工时：按作业者累加（多人重复计）
    decimal laborHours = 0m;
    foreach (var r in runRows)
    {
        if (r.LaborHour is { } lh) { laborHours += lh; continue; }
        if (r.ActualStartTime is { } s && r.ActualEndTime is { } e && e > s) laborHours += Net(s, e);
    }

    // 机时：按机器区间合并（同机重叠只算一次）
    var machineIntervals = new List<(DateTime S, DateTime E)>();
    decimal explicitMachine = 0m;
    foreach (var r in runRows)
    {
        if (r.MachineHour is { } mh) { explicitMachine += mh; continue; }
        if (r.ActualStartTime is { } s && r.ActualEndTime is { } e && e > s)
            machineIntervals.Add((s, e));
    }
    decimal machineHours = explicitMachine + MergedHours(machineIntervals, interrupts);

    wop.ActualLaborHour = decimal.Round(laborHours, 8);
    wop.ActualMachineHour = decimal.Round(machineHours, 8);
    wop.ActualHourSource = "Derived";
    wop.IsHourOverridden = false;
    wop.HourCalculatedTime = DateTime.Now;
    wop.Modifier = user; wop.ModifyDate = DateTime.Now;
    await _db.SaveChangesAsync();
}

/// <summary>区间并集时长（扣中断）。</summary>
private static decimal MergedHours(List<(DateTime S, DateTime E)> ivs, List<(DateTime Start, DateTime End)> interrupts)
{
    if (ivs.Count == 0) return 0m;
    var sorted = ivs.OrderBy(x => x.S).ToList();
    decimal total = 0m;
    var curS = sorted[0].S; var curE = sorted[0].E;
    foreach (var iv in sorted.Skip(1))
    {
        if (iv.S <= curE) { if (iv.E > curE) curE = iv.E; }
        else { total += Span(curS, curE, interrupts); curS = iv.S; curE = iv.E; }
    }
    total += Span(curS, curE, interrupts);
    return total;
}

private static decimal Span(DateTime s, DateTime e, List<(DateTime Start, DateTime End)> interrupts)
{
    var gross = (decimal)(e - s).TotalHours;
    foreach (var iv in interrupts)
    {
        var os = iv.Start > s ? iv.Start : s;
        var oe = iv.End < e ? iv.End : e;
        if (oe > os) gross -= (decimal)(oe - os).TotalHours;
    }
    return gross < 0 ? 0 : gross;
}
```

并在接口 `IProductionResultService` 增 `Task RecalculateProcessHoursAsync(string workOrderNo, string processCd, string? taskCd, string? user);`。在 `CompleteAsync`/`ReportAsync` 落库后调用一次 `await RecalculateProcessHoursAsync(req.WorkOrderNo, req.ProcessCd, req.TaskCd, userName);`（参现有 Complete/Report 实现的入参字段名调整）。

- [ ] **Step 4: 跑绿** → `--filter "FullyQualifiedName~ProductionResultHour"`，预期 4 passed。

- [ ] **Step 5: 提交** → `git commit -m "feat(mes): derive actual machine/labor hours from production results (merge/accumulate/interrupt/override) (A2 §4.3)"`

---

# Phase D — 成本做真

## Task D-1: CostSheet 四字段 + CostSheetLine +9 + 迁移（含历史回填）（spec §3.6/§3.7）

**Files:** Modify `CP6.Entity/DomainModels/Fin/CostSheet.cs`（含 `CostSheetLine`）；迁移。

- [ ] **Step 1: 改 CostSheet**——删 `LaborStd`/`OverheadStd`，加四字段，改 NotMapped
```csharp
[Column(TypeName = "decimal(18,2)")] public decimal LaborActual { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal LaborStandard { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal OverheadActual { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal OverheadStandard { get; set; }

[NotMapped] public decimal TotalActual => MaterialActual + LaborActual + OverheadActual;
[NotMapped] public decimal StandardCost => MaterialStandard + LaborStandard + OverheadStandard;
[NotMapped] public decimal Variance => TotalActual - StandardCost;
[NotMapped] public decimal FgUnitCost => CompletedQty > 0m ? Math.Round(TotalActual / CompletedQty, 4, MidpointRounding.AwayFromZero) : 0m;
```

- [ ] **Step 2: 改 CostSheetLine**——加 9 字段
```csharp
/// <summary>工时（h）。Labor 行=人工工时；Overhead 行=机时；Material 行空。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? Hours { get; set; }
/// <summary>标准工时（h）。Labor 行=标准人工工时；Overhead 行=标准机时。</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? StandardHours { get; set; }
[MaxLength(10)] public string? WgCd { get; set; }
[MaxLength(50)] public string? TaskCd { get; set; }
/// <summary>费率生效日（追溯用了哪版费率）。</summary>
public DateTime? RateValidFrom { get; set; }
/// <summary>小时来源：Derived/Manual/StandardFallback/LegacyFallback。</summary>
[MaxLength(30)] public string? HourSource { get; set; }
/// <summary>计算/回退/警告说明。</summary>
[MaxLength(500)] public string? CalcNote { get; set; }
/// <summary>警告码（无为空）。</summary>
[MaxLength(50)] public string? WarningCode { get; set; }
```
> `ProcessCd` 已存在于 CostSheetLine（料行用），Labor/Overhead 行复用；`UnitPrice` 复用承载费率。

- [ ] **Step 3: 迁移** → `dotnet ef migrations add A2CostSheetTruth --project CP6.Core --startup-project CP6.WebApi`。
  打开生成的 `*_A2CostSheetTruth.cs`，在 `Up()` **末尾**追加历史回填 SQL（在 DropColumn LaborStd/OverheadStd **之前**先搬数据）：
```csharp
// A2 历史回填：旧估算额 → 标准额且实际额=标准额（历史成本单总额不变）
migrationBuilder.Sql(@"UPDATE [T_CostSheet]
  SET [LaborActual]=[LaborStd],[LaborStandard]=[LaborStd],
      [OverheadActual]=[OverheadStd],[OverheadStandard]=[OverheadStd]
  WHERE 1=1;");
```
  确保新列 AddColumn 在该 Sql 之前、DropColumn(LaborStd/OverheadStd) 在该 Sql 之后（必要时手工调整迁移语句顺序）。

- [ ] **Step 4: 跑全量回归** → 全绿（注意 `CostSheet` 的 `TotalActual` 等 NotMapped 被 `CostSettleService` 引用，签名不变）。

- [ ] **Step 5: 提交** → `git commit -m "feat(fin): CostSheet labor/overhead actual+standard fields + line trace fields + history backfill (A2 §3.6/§3.7)"`

## Task D-2: CostCollectService 工费做真（双模式）（spec §4.4）★★

**Files:** Modify `CP6.Core/Services/Fin/CostCollectService.cs`；Test `CP6.Tests/CostCollectLaborOverheadTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/CostCollectLaborOverheadTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Fin;

namespace CP6.Tests;

public class CostCollectLaborOverheadTests
{
    private static CostCollectService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new CostCollectService(db, new FakeSeq(), new ProcessCostRateService(db));
    }

    private sealed class FakeSeq : IFinSequenceService
    {
        public Task<string> NextAsync(string key, DateTime date) => Task.FromResult($"{key}-1");
    }

    private static async Task Seed(CP6.Core.EFDbContext.CP6Context db)
    {
        db.WorkCenters.Add(new WorkCenter { WgCd = "PRINT", Enable = true });
        db.ProcessCostRates.Add(new ProcessCostRate { WgCd = "PRINT", LaborRate = 80, OverheadRate = 120, ValidFrom = new(2026, 1, 1) });
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO1", ProductCd = "FG", CompletedQty = 1000, PlanStartDate = new(2026, 7, 1) });
        db.Set<ProductProcess>().Add(new ProductProcess { ProductCd = "FG", TaskCd = "T1", ProcessCd = "P1", WgCd = "PRINT", SetupHour = 0.5m, CycleTime = 0.002m, StandardCrewSize = 2 });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO1", ProcessCd = "P1", TaskCd = "T1", WgCd = "PRINT", ActualMachineHour = 2, ActualLaborHour = 4 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LaborOverhead_FromHoursTimesRate_WithStandard()
    {
        var svc = Create(out var db);
        await Seed(db);

        var r = await svc.CollectAsync("WO1", 0, 0, "admin");
        Assert.True(r.Ok);

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        // 标准机时 = 0.5 + 1000×0.002 = 2.5；标准人工工时 = 2.5×2 = 5
        // LaborActual = 4×80 = 320；LaborStandard = 5×80 = 400
        // OverheadActual = 2×120 = 240；OverheadStandard = 2.5×120 = 300
        Assert.Equal(320m, sheet.LaborActual);
        Assert.Equal(400m, sheet.LaborStandard);
        Assert.Equal(240m, sheet.OverheadActual);
        Assert.Equal(300m, sheet.OverheadStandard);
        Assert.Contains(sheet.Lines, l => l.Element == CostElement.Labor && l.WgCd == "PRINT" && l.RateValidFrom == new DateTime(2026, 1, 1));
    }

    [Fact]
    public async Task MissingActualHour_UsesStandardFallback_Warning()
    {
        var svc = Create(out var db);
        await Seed(db);
        var wop = await db.Set<WorkOrderProcess>().FirstAsync();
        wop.ActualMachineHour = null; wop.ActualLaborHour = null;   // 无实绩工时
        await db.SaveChangesAsync();

        await svc.CollectAsync("WO1", 0, 0, "admin");

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(400m, sheet.LaborActual);     // 回退用标准工时 5×80
        Assert.Contains(sheet.Lines, l => l.WarningCode == "W-A2-COST-001");
    }

    [Fact]
    public async Task MissingRate_StrictMode_Fails()
    {
        var svc = Create(out var db);
        await Seed(db);
        var rate = await db.ProcessCostRates.FirstAsync();
        rate.IsDeleted = true;   // 删费率
        await db.SaveChangesAsync();
        svc.StrictCostRate = true;

        var r = await svc.CollectAsync("WO1", 0, 0, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A2-RATE-002", r.Code);
    }

    [Fact]
    public async Task MissingRate_MigrationMode_LegacyFallback()
    {
        var svc = Create(out var db);
        await Seed(db);
        var rate = await db.ProcessCostRates.FirstAsync();
        rate.IsDeleted = true;
        await db.SaveChangesAsync();
        svc.StrictCostRate = false;

        await svc.CollectAsync("WO1", 111, 222, "admin");

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(111m, sheet.LaborActual);
        Assert.Equal(111m, sheet.LaborStandard);
        Assert.Equal(222m, sheet.OverheadActual);
        Assert.Contains(sheet.Lines, l => l.WarningCode == "W-A2-COST-002");
    }
}
```

> 注：`FinResult` 须可读 `.Code`（现有 `Fail(code)` 已存 code；若无公开属性，本步顺带在 `FinResult` 暴露 `public string? Code`）。`CostCollectService` 构造改为注入 `IProcessCostRateService`（见 Step 3 DI）。

- [ ] **Step 2: 跑红** → `--filter "FullyQualifiedName~CostCollectLaborOverhead"`，预期编译失败（构造签名/StrictCostRate 缺）。

- [ ] **Step 3: 改造 `CostCollectService`**

构造注入费率服务 + 模式开关；料逻辑不变，替换"工/费标准估算行"为工时×费率块：
```csharp
private readonly IProcessCostRateService _rateSvc;
public bool StrictCostRate { get; set; } = false;   // 默认迁移模式；TDD/生产收尾切 true

public CostCollectService(CP6Context db, IFinSequenceService seq, IProcessCostRateService rateSvc)
{ _db = db; _seq = seq; _rateSvc = rateSvc; }
```
在 `CollectAsync` 内，删除原 `if (laborStd!=0) lines.Add(... Labor ...)` 与 overhead 两行，替换为：
```csharp
// ── 工/费做真：逐工序 工时×费率（spec §4.4）──
decimal laborAct = 0, laborStd2 = 0, ohAct = 0, ohStd = 0;
var baseDate = wo.ActualEndTime ?? wo.PlanStartDate ?? wo.CreateDate;
var calcQty = wo.CompletedQty;

var wops = await _db.Set<WorkOrderProcess>()
    .Where(p => p.WorkOrderNo == workOrderNo && !p.IsDeleted).ToListAsync();
var ppList = await _db.Set<ProductProcess>()
    .Where(p => p.ProductCd == wo.ProductCd && !p.IsDeleted).ToListAsync();
ProductProcess? PP(WorkOrderProcess w) =>
    ppList.FirstOrDefault(p => p.ProcessCd == w.ProcessCd && p.TaskCd == w.TaskCd)
    ?? ppList.FirstOrDefault(p => p.ProcessCd == w.ProcessCd);

bool legacyFallback = false;
if (wops.Count == 0)
{
    if (StrictCostRate) return FinResult.Fail("E-A2-COST-001");
    legacyFallback = true;
}
else
{
    foreach (var w in wops)
    {
        var pp = PP(w);
        var stdMachine = (pp?.SetupHour ?? 0m) + calcQty * (pp?.CycleTime ?? 0m);
        var stdLabor = stdMachine * (pp?.StandardCrewSize ?? 1m);
        var rate = await _rateSvc.ResolveAsync(w.WgCd ?? "", baseDate);
        if (rate == null)
        {
            if (StrictCostRate) return FinResult.Fail("E-A2-RATE-002");
            legacyFallback = true; break;   // 全单回退，不混算
        }

        var actMachine = w.ActualMachineHour ?? stdMachine;
        var actLabor = w.ActualLaborHour ?? stdLabor;
        var fallbackHour = w.ActualMachineHour == null || w.ActualLaborHour == null;

        var laborActAmt = Math.Round(actLabor * rate.LaborRate, 2, MidpointRounding.AwayFromZero);
        var laborStdAmt = Math.Round(stdLabor * rate.LaborRate, 2, MidpointRounding.AwayFromZero);
        var ohActAmt = Math.Round(actMachine * rate.OverheadRate, 2, MidpointRounding.AwayFromZero);
        var ohStdAmt = Math.Round(stdMachine * rate.OverheadRate, 2, MidpointRounding.AwayFromZero);
        laborAct += laborActAmt; laborStd2 += laborStdAmt; ohAct += ohActAmt; ohStd += ohStdAmt;

        lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Labor, ProcessCd = w.ProcessCd, TaskCd = w.TaskCd, WgCd = w.WgCd,
            Hours = actLabor, StandardHours = stdLabor, UnitPrice = rate.LaborRate, ActualAmount = laborActAmt, StandardAmount = laborStdAmt,
            RateValidFrom = rate.ValidFrom, HourSource = fallbackHour ? "StandardFallback" : "Derived",
            WarningCode = fallbackHour ? "W-A2-COST-001" : null, CalcNote = fallbackHour ? "实绩工时缺失，按标准工时计" : null });
        lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Overhead, ProcessCd = w.ProcessCd, TaskCd = w.TaskCd, WgCd = w.WgCd,
            Hours = actMachine, StandardHours = stdMachine, UnitPrice = rate.OverheadRate, ActualAmount = ohActAmt, StandardAmount = ohStdAmt,
            RateValidFrom = rate.ValidFrom, HourSource = fallbackHour ? "StandardFallback" : "Derived",
            WarningCode = fallbackHour ? "W-A2-COST-001" : null });
    }
}

if (legacyFallback)
{
    laborAct = laborStd2 = laborStd; ohAct = ohStd = overheadStd;
    lines.RemoveAll(l => l.Element is CostElement.Labor or CostElement.Overhead);   // 清部分行，整单估算
    lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Labor, ActualAmount = laborStd, StandardAmount = laborStd,
        HourSource = "LegacyFallback", WarningCode = "W-A2-COST-002", CalcNote = "缺费率/工序，整单回退传入估算" });
    lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Overhead, ActualAmount = overheadStd, StandardAmount = overheadStd,
        HourSource = "LegacyFallback", WarningCode = "W-A2-COST-002" });
}
```
写表（替换原 `sheet.LaborStd/OverheadStd` 赋值）：
```csharp
sheet.LaborActual = laborAct; sheet.LaborStandard = laborStd2;
sheet.OverheadActual = ohAct; sheet.OverheadStandard = ohStd;
```
DI（`Program.cs`）：`CostCollectService` 已注册，确认其构造现多注入 `IProcessCostRateService`（已在 A-2 DI 注册，DI 自动解析）。`FinResult` 加 `public string? Code { get; init; }`，`Fail(code,...)` 填充。

- [ ] **Step 4: 跑绿** → `--filter "FullyQualifiedName~CostCollectLaborOverhead"`，预期 4 passed。

- [ ] **Step 5: 跑现有成本测试回归** → `--filter "FullyQualifiedName~Cost"`，修正旧 `CostCollect` 测试（若断言 `LaborStd`，改为 `LaborActual/LaborStandard`）。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): CostCollect labor/overhead from hours×rate (machine/labor split, standard+migration fallback) (A2 §4.4)"`

## Task D-3: CostSettleService 贷实际额（spec §4.5）

**Files:** Modify `CP6.Core/Services/Fin/CostSettleService.cs`；Test `CP6.Tests/CostSettleActualTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/CostSettleActualTests.cs`（建已归集 sheet → settle → 断言贷 DIRECT_LABOR=LaborActual、MFG_OVERHEAD=OverheadActual、无差异分录）

```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests;

public class CostSettleActualTests
{
    [Fact]
    public async Task Settle_CreditsActualLaborOverhead()
    {
        var db = TestHelper.CreateInMemoryContext();
        // 科目角色
        foreach (var role in new[] { "WIP", "FG", "INVENTORY", "DIRECT_LABOR", "MFG_OVERHEAD" })
            db.GlAccounts.Add(new GlAccount { Code = role, Name = role, Role = role, IsActive = true });
        db.CostSheets.Add(new CostSheet { Id = Guid.NewGuid(), No = "CS-1", WorkOrderNo = "WO1", CompletedQty = 100,
            MaterialActual = 1000, MaterialStandard = 1000, LaborActual = 320, LaborStandard = 400,
            OverheadActual = 240, OverheadStandard = 300, Status = CostSheetStatus.Collected });
        await db.SaveChangesAsync();

        var svc = new CostSettleService(db, new JournalEntryService(db /* 按现有构造补齐依赖 */));
        var r = await svc.SettleAsync("WO1", "admin");
        Assert.True(r.Ok);

        var lines = await db.JournalLines.AsNoTracking().ToListAsync();
        Assert.Contains(lines, l => l.Credit == 320m);   // DIRECT_LABOR 实际额
        Assert.Contains(lines, l => l.Credit == 240m);   // MFG_OVERHEAD 实际额
    }
}
```
> `JournalEntryService` 构造按现有签名补齐（参现有 settle 测试或 `CostSettleServiceTests` 既有写法；若已有同类测试，复用其工厂）。

- [ ] **Step 2: 跑红** → `--filter "FullyQualifiedName~CostSettleActual"`，预期失败（贷额仍取 LaborStd/编译错）。

- [ ] **Step 3: 改 `CostSettleService.SettleAsync`**——把
```csharp
if (sheet.LaborStd > 0m) { ... Credit = sheet.LaborStd ... }
if (sheet.OverheadStd > 0m) { ... Credit = sheet.OverheadStd ... }
```
改为
```csharp
if (sheet.LaborActual > 0m) { var lab = await RoleIdAsync("DIRECT_LABOR"); if (lab == null) return FinResult.Fail("E-FIN-141", "DIRECT_LABOR"); collect.Lines.Add(new JournalLine { AccountId = lab.Value, Credit = sheet.LaborActual }); }
if (sheet.OverheadActual > 0m) { var oh = await RoleIdAsync("MFG_OVERHEAD"); if (oh == null) return FinResult.Fail("E-FIN-141", "MFG_OVERHEAD"); collect.Lines.Add(new JournalLine { AccountId = oh.Value, Credit = sheet.OverheadActual }); }
```
（`total = sheet.TotalActual` 已自动含真实工/费，WIP/FG 行不变。）

- [ ] **Step 4: 跑绿** → `--filter "FullyQualifiedName~CostSettleActual"`，预期 passed。

- [ ] **Step 5: 提交** → `git commit -m "feat(fin): CostSettle credits actual labor/overhead (real cost to FG) (A2 §4.5)"`

---

# Phase E — 前端 + 收口 + QA

## Task E-1: 前端主数据（工作中心 + 工序费率）+ api/类型/路由/菜单/i18n

**Files:**
- Create: `cp6.web/src/types/mes/processCost.ts`、`src/api/mes/processCost.ts`、`src/views/mes/WorkCenterView.vue`、`src/views/mes/ProcessCostRateView.vue`
- Modify: `cp6.web/src/router/index.ts`、`CP6.WebApi/Program.cs`（菜单 + i18n 接入）、Create `CP6.WebApi/Seed/I18nA2ScreenSeed.cs`

- [ ] **Step 1: 类型 + api**（照 `src/api/plan/plan.ts` 模式）`src/types/mes/processCost.ts`
```typescript
export interface ApiResp<T> { code: number; message: string; data: T }
export interface WorkCenter { id?: string; wgCd: string; wgName?: string | null; dailyCapacityHours?: number | null; enable: boolean }
export interface ProcessCostRate { id?: string; wgCd: string; laborRate: number; overheadRate: number; validFrom: string; validTo?: string | null }
```
`src/api/mes/processCost.ts`
```typescript
import http from '../http'
import type { ApiResp, WorkCenter, ProcessCostRate } from '@/types/mes/processCost'

export const workCenterApi = {
  list(keyword?: string) { return http.get<any, ApiResp<WorkCenter[]>>('/mes/work-center', { params: { keyword } }) },
  save(d: WorkCenter) { return http.post<any, ApiResp<unknown>>('/mes/work-center/upsert', d) },
  remove(wgCd: string) { return http.delete<any, ApiResp<unknown>>(`/mes/work-center/${wgCd}`) },
}
export const processCostRateApi = {
  list(wgCd?: string) { return http.get<any, ApiResp<ProcessCostRate[]>>('/mes/process-cost-rate', { params: { wgCd } }) },
  save(d: ProcessCostRate) { return http.post<any, ApiResp<unknown>>('/mes/process-cost-rate/upsert', d) },
  remove(id: string) { return http.delete<any, ApiResp<unknown>>(`/mes/process-cost-rate/${id}`) },
}
```

- [ ] **Step 2: 视图**——`WorkCenterView.vue`（CRUD，照 `views/plan/ItemPolicyView.vue` 结构：toolbar+table+dialog；字段 wgCd/wgName/dailyCapacityHours/enable）、`ProcessCostRateView.vue`（按 wgCd 过滤 + 生效日列表 + dialog 含 laborRate/overheadRate/validFrom/validTo）。t() 用自然语言中文 key。

- [ ] **Step 3: 路由** `src/router/index.ts`（viewModules 内 Mes 区域）
```typescript
'/mes/work-center': () => import('@/views/mes/WorkCenterView.vue'),
'/mes/process-cost-rate': () => import('@/views/mes/ProcessCostRateView.vue'),
```

- [ ] **Step 4: 菜单** `Program.cs`（MES 组下，参 A1 Plan 菜单 730 块）——取 MES 顶层组 MenuId（探查现有 MES 组 MenuId），加两子项 `工作中心`(/mes/work-center)、`工序费率`(/mes/process-cost-rate)，授权 RoleId=1，幂等 `if(!Any(MenuId==X))`。

- [ ] **Step 5: i18n seed** `CP6.WebApi/Seed/I18nA2ScreenSeed.cs`（照 `I18nPlanScreenSeed`，五语）：菜单名、视图标题、字段（工作中心/日可用产能/人工费率/制造费率/生效日/失效日/段取工时/单件工时/标准人数/实际机时/实际人工工时/标准机时/标准人工工时）、按钮、W-A2-*/E-A2-* 文案。接入 `Program.cs` i18n 合并链 `.Concat(I18nA2ScreenSeed.Items)`。

- [ ] **Step 6: 全量构建 + i18n 校验**——`dotnet build CP6.WebApi`；起后端→`npm run i18n:pull`→`npm run i18n:check`（绿）→`npm run type-check`（绿）。补缺 key。

- [ ] **Step 7: 提交** → `git commit -m "feat(mes): work-center + process-cost-rate UI + api/routes/menu/i18n (A2)"`

## Task E-2: 成本单展示 + ProductProcess 工程页字段 + gstack QA 收口

**Files:** Modify `cp6.web/src/views/fin/CostSheetView.vue`、`src/views/erp/ProductMaster*`（工程编辑加 setupHour/cycleTime/standardCrewSize）；前端类型同步。

- [ ] **Step 1: 成本单视图**——`CostSheetView.vue` 加 工/费 实际/标准/差异列；工序明细展开 CostSheetLine（Element/ProcessCd/WgCd/Hours/StandardHours/UnitPrice/ActualAmount/StandardAmount/HourSource/WarningCode）。t() 中文 key。

- [ ] **Step 2: 製品工程页**——ProductProcess 编辑表加 `段取工时/单件工时/标准人数` 三列（前端 ProductProcess 类型 + 表单）。

- [ ] **Step 3: type-check + i18n:check** 绿。

- [ ] **Step 4: 全量构建全测** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿。

- [ ] **Step 5: gstack 真浏览器 QA**（起后端 5177 + 前端 5173，admin/123456）照 spec §八 路径：
  1. 建 WorkCenter `PRINT`，DailyCapacityHours=16；
  2. 建 ProcessCostRate `PRINT` ValidFrom=2026-01-01 LaborRate=80 OverheadRate=120；
  3. ProductProcess(某製品) 填 SetupHour=0.5/CycleTime=0.002/StandardCrewSize=2；
  4. 工单完工 1000 + 报工（同机 M1 两作业者 08:00-10:00）→ 派生 ActualMachine=2/ActualLabor=4；
  5. 成本归集 → CostSheet 显示 LaborActual=320/LaborStandard=400/OverheadActual=240/OverheadStandard=300 + 工时行；
  6. 结转 → DIRECT_LABOR=320、MFG_OVERHEAD=240。
  截图留证；修任何 UI/联调 bug。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): cost-sheet labor/overhead actual/std display + ProductProcess routing fields UI + gstack QA (A2)"`

---

## Self-Review（对照 spec 覆盖）

- **§3.1 ProductProcess**：B-1 ✅（SetupHour/CycleTime/StandardCrewSize）
- **§3.2 WorkCenter**：A-1 ✅（含 DailyCapacityHours 地基）
- **§3.3 ProcessCostRate**：A-2 ✅（双率+生效区间+重叠校验+Resolve）
- **§3.4/§3.5 工时字段**：C-1 ✅（WorkOrderProcess+6 / ProductionResult+2）
- **§3.6/§3.7 CostSheet/Line**：D-1 ✅（四字段+9 追溯字段+历史回填）
- **§4.1/§4.2 主数据服务**：A-1/A-2 ✅
- **§4.3 工时派生**：C-2 ✅（机器合并/作业者累加/中断扣减/显式覆盖/覆盖态跳过）
- **§4.4 CostCollect**：D-2 ✅（标准机时/人工工时、工时×费率、缺工时标准回退 W-A2-COST-001、缺费率严格 E-A2-RATE-002 / 迁移 W-A2-COST-002 不混算）
- **§4.5 CostSettle**：D-3 ✅（贷实际额）
- **§五 API/前端**：E-1/E-2 ✅
- **§六 错误/Warning 码**：分散落各 Task（E-A2-WC/RATE/COST、W-A2-COST）✅
- **§七 迁移/多租户/权限**：各 Task 迁移 + BaseBizEntity ✅；权限随菜单（E-1）
- **§八 测试**：A-1(3)/A-2(5)/C-2(4)/D-2(4)/D-3(1) + gstack QA ✅

**Type 一致性**：`ProcessCostRate.ResolveAsync(wgCd,onDate)`（A-2）被 `CostCollectService`（D-2）调用；`WorkOrderProcess.ActualMachineHour/ActualLaborHour`（C-1）由 `ProductionResultService.RecalculateProcessHoursAsync`（C-2）写、`CostCollect`（D-2）读；`CostSheet.{LaborActual,LaborStandard,OverheadActual,OverheadStandard}`（D-1）被 `CostCollect`（D-2）写、`CostSettle`（D-3）贷实际额；`ProductProcess.SetupHour/CycleTime/StandardCrewSize`（B-1）被标准工时公式（D-2）消费、将来 CRP（MRP P4）复用。

**已知推迟（spec §十）**：CRP 负荷引擎（MRP P4）；标准成本法差异入账；月末制造费用分摊；费率审批流；机台级费率；外协工序成本要素重构。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-17-a2-process-routing.md`。源 spec：`docs/superpowers/specs/2026-06-17-a2-process-routing-design.md`（v2 定稿）。执行序：A 主数据 → B 路线字段 → C 实绩工时 → D 成本做真 → E 前端 + QA。

**两种执行方式**：
1. **Subagent-Driven（推荐）**——每 Task 派新 subagent，任务间评审。
2. **Inline Execution**——本会话分批 + 检查点（同 A1 MRP 落地方式）。
