# 租户配置基建六件套 + 行业包骨架 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 07-09 spec 的配置基建六件套（开关/采番/术语/单据流裁剪/SFS绑定/导出）与行业包机制骨架（三钩子+纸器包空壳），为 Sales v2 提供出生即消费的地基。

**Architecture:** 六个独立窄机制（各自表+服务+真实消费者），实体统一 `Cfg_` 前缀继承 `BaseTenantEntity`（租户过滤/盖章/唯一索引租户化全部白得）；主干状态机为编译期代码（DocFlowRegistry 声明），配置只在白名单与预声明备选边内选择；行业包=编译进产品的独立项目，按 FeatureGate 路由钩子。

**Tech Stack:** .NET 8 / EF Core 8（SQL Server + InMemory/Sqlite 测试）/ xUnit+Moq / Vue3 + element-plus。

**范围外（各自另立 plan）**：API-first 双模认证（spec §4.1，独立子系统）；Item 通用化+PaperPack 扩展表（spec §6 步骤3）；Sales v2 域模型（步骤4）；纸器迁移（步骤5）。

## Global Constraints

- 全部拍板已闭合：07-09 spec §7（六项）+ 2026-07-10 两份盘点文档（13 项全采）。实现与拍板冲突时以拍板为准。
- 新实体继承 `BaseTenantEntity`（`CP6.Entity\BaseTenantEntity.cs`）；唯一索引只写业务列，框架自动升级为 `(TenantId, …)` 复合（CP6Context.cs:2105 反射机制）。
- 错误码一律 `throw new BizException("E-CONF-NNN")` / `"E-WF-NNN"`（`CP6.Core\Localization\BizException.cs`，namespace 是 `CP6.WebApi.Localization`）；配置类错误**保存时校验拒绝**，运行时 fail-closed。
- Controller 惯例：`[ApiController] [Route("api/cfg/…")] [Authorize]`，继承 `LocalizedControllerBase`，成功信封 `Ok(new { code = 0, message = "OK", data })`；操作权限 `[RequirePermission(menu, action)]`。
- DI 注册全部写在 `CP6.WebApi\Program.cs`（`builder.Services.AddScoped<…>` + 中文行注释），不建扩展方法。
- 迁移：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`；数据回填用 `migrationBuilder.Sql` 内联 T-SQL（幂等 NOT EXISTS + THROW 校验，样板 `20260708093013_SysRoleTenantize.cs`）。
- 测试：`CP6.Tests`（xUnit），`TestHelper.CreateInMemoryContext(user, tenant)`；跨租户隔离测试用共享库名双上下文法（样板 `CP6.Tests\Sys\RoleTenantIsolationTests.cs`）。全量验证命令 `dotnet test`（基线 1577 绿，只增不减）。
- **每个 commit 立即 `git push`**（用户硬性纪律）。工作分支：从 main 新开 `feat/tenant-config-platform`。
- 跳号口径（拍板）：编号回滚不回收，写运维文档不写补偿代码。
- 口径3=per-action（拍板 #1）：不做配置快照/版本 pin，任何"按创建时配置"的实现都是错的。
- 缓存 key 前缀照 `CacheService` 惯例加 const：`feat:`（开关）。

## 计划级设计决策（与 spec 字面的两处偏离，均已在会话中确认方向）

1. **② 采番不建新表**：`Pub_DocSequence`(BaseTenantEntity)+`SeqService` 已是租户级富采番。演进方案=同表加 `Pattern`/`RowVersion` 列，新 façade `INumberingService`（原子取号+默认规则回退+Pattern 保存校验）；`ISeqService` 及既有调用方不动（记后续归并票）。
2. **⑤ 答案表新建 `Cfg_EntityFormData`**：`Wf_FormData` 与 OA 流程实例绑定，不混用。表单定义复用 `Wf_FormDef`，一律按 `FormKey`（稳定业务键）引用而非 GUID——⑥ 的 SFS 引用重映射因此退化为 FormKey 存在性校验。

## 文件地图（新建/修改总览）

```
CP6.Entity/DomainModels/Cfg/
  Cfg_TenantFeature.cs          ① 开关
  Cfg_TermOverride.cs           ③ 术语覆盖
  Cfg_DocFlowConfig.cs          ④ 单据流裁剪
  Cfg_EntityFormBinding.cs      ⑤ SFS 绑定
  Cfg_EntityFormData.cs         ⑤ 扩展字段答案
CP6.Entity/DomainModels/Pub/Pub_DocSequence.cs        ② 加 Pattern/RowVersion（修改）
CP6.Core/Services/Cfg/
  IFeatureGate.cs / FeatureGate.cs                    ①
  INumberingService.cs / NumberingService.cs / NumberingPattern.cs   ②
  ITerminologyResolver.cs / TerminologyResolver.cs    ③
  DocFlow/DocFlowDefinition.cs / DocFlowRegistry.cs / DocFlowDefinitions.Sales.cs  ④ 代码侧状态机
  DocFlow/IDocFlowGuard.cs / DocFlowConfigService.cs / IDocFlowEngine.cs / DocFlowEngine.cs  ④
  IEntityFormBindingService.cs / EntityFormBindingService.cs  ⑤
  Bundle/ConfigBundle.cs / IConfigBundleService.cs / ConfigBundleService.cs  ⑥
  Packs/IIndustryPack.cs / IPackHookRegistry.cs / PackRegistry.cs / PackEnableService.cs  §2
  Packs/Hooks/IPricingHook.cs / IDocExtensionProvider.cs / IItemValidationHook.cs  §2
CP6.Packs.PaperPack/（新项目）PaperPack.cs + Seeds/terminology.json + Seeds/features.json
CP6.Core/Services/Wf/ApprovalService.cs               直通绑定/ConditionJson/Withdraw（修改）
CP6.Core/Services/Pur/Contracts/ApprovalServiceAdapter.cs   fail-open 收敛（修改）
CP6.Core/Services/Wf/（FlowDef 保存路径，Task 9 先定位）     保存闸（修改）
CP6.WebApi/Controllers/Cfg/
  FeatureController.cs / NumberingController.cs / TerminologyController.cs
  DocFlowConfigController.cs / FormBindingController.cs / ConfigBundleController.cs / PackController.cs
CP6.WebApi/Services/LangPublishService.cs             ③ 租户快照（修改）
CP6.WebApi/Seed/I18nCfgErrorSeed.cs                   E-CONF/E-WF 码五语词条
CP6.WebApi/Program.cs                                 DI 注册（修改）
CP6.Core/EFDbContext/CP6Context.cs                    DbSet+索引（修改）
cp6.web/src/api/cfg.ts + src/views/cfg/ConfigCenterView.vue + router/index.ts   顾问配置中心
CP6.Tests/Cfg/…（每任务一个测试文件，见各任务）
```

依赖序：Task 1→2 / 3 / 4 / 5 互相独立可并行（Wave A）；Task 6→7→8 串行，9、10 独立（Wave B）；Task 11 依赖 1，12 依赖 1-8，13、14 收尾（Wave C）。

---

### Task 1: ① Cfg_TenantFeature 实体 + IFeatureGate 服务（两层缓存）

**Files:**
- Create: `CP6.Entity/DomainModels/Cfg/Cfg_TenantFeature.cs`
- Create: `CP6.Core/Services/Cfg/IFeatureGate.cs`、`CP6.Core/Services/Cfg/FeatureGate.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 唯一索引）
- Modify: `CP6.Core/Utilities/CacheService.cs`（加 `FeatureKeyPrefix = "feat:"` 常量）
- Modify: `CP6.WebApi/Program.cs`（DI）
- Test: `CP6.Tests/Cfg/FeatureGateTests.cs`
- 迁移: `CfgTenantFeature`

**Interfaces:**
- Consumes: `BaseTenantEntity`、`CacheService`（IDistributedCache 封装）、`IMemoryCache`、`ITenantContext`
- Produces: `IFeatureGate.IsEnabledAsync(string featureKey): Task<bool>`；`SetAsync(string featureKey, bool enabled, string? configJson = null): Task`；`GetConfigJsonAsync(string featureKey): Task<string?>`——Task 2/11/13 消费

- [ ] **Step 1: 写实体**

```csharp
// CP6.Entity/DomainModels/Cfg/Cfg_TenantFeature.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Cfg;

/// <summary>
/// 租户功能开关（配置基建①，spec 2026-07-09 §1①）。
/// FeatureKey 分层命名：pack.paperpack / module.sales-v2。
/// 只管包/模块级开关；单据环节裁剪唯一归 Cfg_DocFlowConfig，禁止在此放 flow.* 键。
/// </summary>
[Table("Cfg_TenantFeature")]
public class Cfg_TenantFeature : BaseTenantEntity
{
    /// <summary>功能键（分层命名 pack.* / module.*，租户内唯一）</summary>
    [Required, MaxLength(100)]
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }

    /// <summary>小 JSON 参数（可选；不做百分比灰度）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? ConfigJson { get; set; }
}
```

- [ ] **Step 2: 注册进 CP6Context**——DbSet 区加 `public DbSet<Cfg_TenantFeature> Cfg_TenantFeatures { get; set; }`；OnModelCreating 加：

```csharp
modelBuilder.Entity<Cfg_TenantFeature>(e =>
{
    e.HasIndex(x => x.FeatureKey).IsUnique().HasDatabaseName("UX_Cfg_TenantFeature_Key"); // 框架自动升级为 (TenantId, FeatureKey)
});
```

- [ ] **Step 3: 写失败测试**

```csharp
// CP6.Tests/Cfg/FeatureGateTests.cs
using CP6.Core.Services.Cfg;
using CP6.Entity.DomainModels.Cfg;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CP6.Tests.Cfg;

public class FeatureGateTests
{
    private static (FeatureGate gate, CP6.Core.EFDbContext.CP6Context db) Build()
    {
        var db = TestHelper.CreateInMemoryContext();
        var gate = new FeatureGate(db, TestHelper.CreateCacheService(),
            new MemoryCache(new MemoryCacheOptions()));
        return (gate, db);
    }

    [Fact]
    public async Task Unknown_key_defaults_to_disabled()
    {
        var (gate, _) = Build();
        Assert.False(await gate.IsEnabledAsync("pack.paperpack"));
    }

    [Fact]
    public async Task Set_then_read_roundtrip()
    {
        var (gate, db) = Build();
        await gate.SetAsync("module.sales-v2", true, "{\"mode\":\"parallel\"}");
        Assert.True(await gate.IsEnabledAsync("module.sales-v2"));
        Assert.Equal("{\"mode\":\"parallel\"}", await gate.GetConfigJsonAsync("module.sales-v2"));
        Assert.Single(db.Cfg_TenantFeatures);           // upsert 不重复插行
        await gate.SetAsync("module.sales-v2", false);
        Assert.False(await gate.IsEnabledAsync("module.sales-v2"));  // Set 已删缓存，立刻可见
    }

    [Fact]
    public async Task Flow_star_keys_rejected()   // 职责边界：环节裁剪唯一归④
    {
        var (gate, _) = Build();
        var ex = await Assert.ThrowsAsync<CP6.WebApi.Localization.BizException>(
            () => gate.SetAsync("flow.quotation", true));
        Assert.Equal("E-CONF-001", ex.Code);
    }
}
```

- [ ] **Step 4: 跑测试确认失败**——`dotnet test --filter FeatureGateTests`，预期编译错误（FeatureGate 不存在）。

- [ ] **Step 5: 实现服务**

```csharp
// CP6.Core/Services/Cfg/IFeatureGate.cs
namespace CP6.Core.Services.Cfg;

public interface IFeatureGate
{
    /// <summary>开关判定。两层缓存：进程内 60s TTL → IDistributedCache → DB。未配置=false。</summary>
    Task<bool> IsEnabledAsync(string featureKey);
    Task<string?> GetConfigJsonAsync(string featureKey);
    /// <summary>upsert 开关并失效两层缓存（分布式键删除，各实例最迟 60s 收敛——运维须知已记 ≤60s 最终一致）。flow.* 键拒绝（E-CONF-001）。</summary>
    Task SetAsync(string featureKey, bool enabled, string? configJson = null);
    /// <summary>当前租户全量开关（管理页用）</summary>
    Task<List<Cfg_TenantFeature>> ListAsync();
}
```

```csharp
// CP6.Core/Services/Cfg/FeatureGate.cs
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Cfg;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CP6.Core.Services.Cfg;

public class FeatureGate : IFeatureGate
{
    private readonly CP6Context _db;
    private readonly CacheService _cache;          // 分布式层（Redis/内存回退）
    private readonly IMemoryCache _local;          // 进程内 60s 前置层
    private static readonly TimeSpan LocalTtl = TimeSpan.FromSeconds(60);

    public FeatureGate(CP6Context db, CacheService cache, IMemoryCache local)
    { _db = db; _cache = cache; _local = local; }

    private string Key(string featureKey) => $"{CacheService.FeatureKeyPrefix}{_db.CurrentTenantId}:{featureKey}";

    public async Task<bool> IsEnabledAsync(string featureKey)
        => (await LoadAsync(featureKey))?.Enabled ?? false;

    public async Task<string?> GetConfigJsonAsync(string featureKey)
        => (await LoadAsync(featureKey))?.ConfigJson;

    private async Task<FeatureSnapshot?> LoadAsync(string featureKey)
    {
        var key = Key(featureKey);
        if (_local.TryGetValue(key, out FeatureSnapshot? hit)) return hit;
        var snap = await _cache.GetOrSetAsync(key, async () =>
        {
            var row = await _db.Cfg_TenantFeatures.AsNoTracking()
                .FirstOrDefaultAsync(x => x.FeatureKey == featureKey);
            return new FeatureSnapshot(row?.Enabled ?? false, row?.ConfigJson);
        });
        _local.Set(key, snap, LocalTtl);
        return snap;
    }

    public async Task SetAsync(string featureKey, bool enabled, string? configJson = null)
    {
        if (featureKey.StartsWith("flow.", StringComparison.OrdinalIgnoreCase))
            throw new BizException("E-CONF-001");   // 环节裁剪唯一归④，禁止双源
        var row = await _db.Cfg_TenantFeatures.FirstOrDefaultAsync(x => x.FeatureKey == featureKey);
        if (row == null) { row = new Cfg_TenantFeature { FeatureKey = featureKey }; _db.Cfg_TenantFeatures.Add(row); }
        row.Enabled = enabled;
        if (configJson != null) row.ConfigJson = configJson;
        await _db.SaveChangesAsync();
        var key = Key(featureKey);
        await _cache.RemoveAsync(key);
        _local.Remove(key);
    }

    public Task<List<Cfg_TenantFeature>> ListAsync()
        => _db.Cfg_TenantFeatures.AsNoTracking().OrderBy(x => x.FeatureKey).ToListAsync();

    public sealed record FeatureSnapshot(bool Enabled, string? ConfigJson);
}
```

`CacheService.cs` 加常量：`public const string FeatureKeyPrefix = "feat:";`

- [ ] **Step 6: 生成迁移**——`dotnet ef migrations add CfgTenantFeature --project CP6.Core --startup-project CP6.WebApi`
- [ ] **Step 7: DI 注册**——Program.cs：`builder.Services.AddScoped<CP6.Core.Services.Cfg.IFeatureGate, CP6.Core.Services.Cfg.FeatureGate>();   // 配置基建① 功能开关`（确认 `AddMemoryCache()` 已注册，没有则加）
- [ ] **Step 8: 跑测试至绿**——`dotnet test --filter FeatureGateTests` 预期 3 PASS；再全量 `dotnet test` 不减绿。
- [ ] **Step 9: Commit + push**——`git add -A && git commit -m "feat(cfg): 配置基建① Cfg_TenantFeature+IFeatureGate 两层缓存" && git push -u origin feat/tenant-config-platform`

---

### Task 2: ① 管理 API + 菜单过滤接线

**Files:**
- Create: `CP6.WebApi/Controllers/Cfg/FeatureController.cs`
- Modify: 菜单下发处（定位：`grep -rn "viewModules\|GetMenus" CP6.WebApi/Controllers` 找菜单列表 API，通常 MenuController/LoginController）——按 FeatureKey `module.*` 过滤被关模块的菜单
- Test: `CP6.Tests/Cfg/FeatureMenuFilterTests.cs`

**Interfaces:**
- Consumes: `IFeatureGate`（Task 1 全签名）
- Produces: `GET api/cfg/feature/list`、`POST api/cfg/feature/set {featureKey, enabled, configJson?}`；菜单 DTO 增加约定：`Sys_Menu.FeatureKey`（新 nullable 列，标记菜单挂哪个开关）

- [ ] **Step 1: 失败测试**——菜单过滤：给 Sys_Menu 一行 `FeatureKey="module.sales-v2"`，开关关→菜单列表不含它；开→含。

```csharp
[Fact]
public async Task Menu_rows_hidden_when_feature_disabled()
{
    var db = TestHelper.CreateInMemoryContext();
    db.Sys_Menus.Add(new Sys_Menu { MenuId = 900, MenuName = "SalesV2", FeatureKey = "module.sales-v2", Enable = 1 });
    await db.SaveChangesAsync();
    var gate = /* Task 1 Build() 同款 */;
    var visible = await MenuFeatureFilter.FilterAsync(db.Sys_Menus.ToList(), gate);
    Assert.DoesNotContain(visible, m => m.MenuId == 900);
    await gate.SetAsync("module.sales-v2", true);
    visible = await MenuFeatureFilter.FilterAsync(db.Sys_Menus.ToList(), gate);
    Assert.Contains(visible, m => m.MenuId == 900);
}
```

- [ ] **Step 2: 跑失败**（`Sys_Menu.FeatureKey` 不存在，编译失败）
- [ ] **Step 3: 实现**——`Sys_Menu` 加 `[MaxLength(100)] public string? FeatureKey { get; set; }`（nullable=存量零迁移）+ 迁移 `SysMenuFeatureKey`；静态 `MenuFeatureFilter.FilterAsync(List<Sys_Menu>, IFeatureGate)`：FeatureKey 为空恒可见，非空按开关；在菜单下发 API 的现有查询后插一行过滤调用。Controller：

```csharp
[ApiController]
[Route("api/cfg/feature")]
[Authorize]
public class FeatureController : LocalizedControllerBase
{
    private readonly IFeatureGate _gate;
    public FeatureController(IFeatureGate gate) => _gate = gate;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    [HttpGet("list")]
    [RequirePermission("cfg-center", "Search")]
    public async Task<IActionResult> List() => Ok2(await _gate.ListAsync());

    public record SetReq(string FeatureKey, bool Enabled, string? ConfigJson);
    [HttpPost("set")]
    [RequirePermission("cfg-center", "Update")]
    public async Task<IActionResult> Set([FromBody] SetReq req)
    { await _gate.SetAsync(req.FeatureKey, req.Enabled, req.ConfigJson); return Ok2(); }
}
```

- [ ] **Step 4: 跑测试至绿 + 全量不减绿**
- [ ] **Step 5: Commit + push**——`git commit -m "feat(cfg): ① 开关管理API+菜单FeatureKey过滤" && git push`

---

### Task 3: ② INumberingService（Pub_DocSequence 演进：Pattern+原子取号+默认回退）

**Files:**
- Modify: `CP6.Entity/DomainModels/Pub/Pub_DocSequence.cs`（加 `Pattern`、`RowVersion`）
- Create: `CP6.Core/Services/Cfg/NumberingPattern.cs`（纯函数模板引擎）
- Create: `CP6.Core/Services/Cfg/INumberingService.cs`、`NumberingService.cs`
- Create: `CP6.WebApi/Controllers/Cfg/NumberingController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）
- Test: `CP6.Tests/Cfg/NumberingPatternTests.cs`、`CP6.Tests/Cfg/NumberingServiceTests.cs`
- 迁移: `PubDocSequencePattern`

**Interfaces:**
- Consumes: `Pub_DocSequence`（现字段 BizKey/Prefix/DateFormat/SeqLength/ResetCycle/CurrentPeriod/CurrentValue）
- Produces: `INumberingService.NextAsync(string docType): Task<string>`（Sales v2 采番唯一入口）；`NumberingPattern.Parse(string): NumberingPattern`、`.Format(DateTime now, long seq): string`、`.PeriodKey(DateTime now, int resetCycle): string`；`NumberingDefaults.For(string docType): (string Pattern, int ResetCycle)`
- `ISeqService`/既有调用方**一行不改**。

- [ ] **Step 1: 模板引擎失败测试**

```csharp
public class NumberingPatternTests
{
    [Theory]
    [InlineData("SO-{yyyy}{MM}-{seq:5}", "2026-07-10", 42, "SO-202607-00042")]
    [InlineData("{seq:4}", "2026-07-10", 7, "0007")]
    [InlineData("Q{yy}{MM}{dd}-{seq:3}", "2026-07-10", 999, "Q260710-999")]
    public void Format_composes_segments(string pattern, string date, long seq, string expected)
        => Assert.Equal(expected, NumberingPattern.Parse(pattern).Format(DateTime.Parse(date), seq));

    [Theory]
    [InlineData("SO-{seq:5}", 0, "*")]        // 不重置 → 常量周期键
    [InlineData("SO-{seq:5}", 2, "2026-07")]  // 月重置
    [InlineData("SO-{seq:5}", 3, "2026")]     // 年重置
    [InlineData("SO-{seq:5}", 1, "2026-07-10")] // 日重置
    public void PeriodKey_by_resetCycle(string pattern, int cycle, string expected)
        => Assert.Equal(expected, NumberingPattern.Parse(pattern).PeriodKey(DateTime.Parse("2026-07-10"), cycle));

    [Theory]
    [InlineData("SO-{bad}")]      // 未知占位符
    [InlineData("SO-{seq}")]      // seq 缺位数
    [InlineData("SO-12345")]      // 无 seq 段
    public void Invalid_pattern_throws_ECONF010(string pattern)
    {
        var ex = Assert.Throws<BizException>(() => NumberingPattern.Parse(pattern));
        Assert.Equal("E-CONF-010", ex.Code);
    }
}
```

- [ ] **Step 2: 跑失败 → 实现模板引擎**——占位符白名单 `{yyyy} {yy} {MM} {dd} {seq:N}`（N∈1..10，恰一个 seq 段），其余字面量。`Parse` 用正则 `\{([^}]+)\}` 逐段校验，非法抛 `E-CONF-010`。约 60 行纯静态类，无 IO。跑至绿。

- [ ] **Step 3: 实体加列 + 迁移**

```csharp
// Pub_DocSequence.cs 追加
/// <summary>段模板（如 SO-{yyyy}{MM}-{seq:5}）。非空时优先于 Prefix/DateFormat/SeqLength 三段式（legacy 兼容）</summary>
[MaxLength(100)] public string? Pattern { get; set; }
/// <summary>乐观并发戳——仅管理页编辑用，取号路径不碰（spec §1② 明文）</summary>
[Timestamp] public byte[]? RowVersion { get; set; }
```

`dotnet ef migrations add PubDocSequencePattern --project CP6.Core --startup-project CP6.WebApi`

- [ ] **Step 4: NumberingService 失败测试**

```csharp
public class NumberingServiceTests
{
    [Fact]
    public async Task No_rule_falls_back_to_default_and_creates_row()   // 存量租户零迁移
    {
        var db = TestHelper.CreateInMemoryContext();
        var svc = new NumberingService(db);
        var no = await svc.NextAsync("SALES_ORDER");
        Assert.Matches(@"^SO-\d{6}-\d{5}$", no);                        // NumberingDefaults
        Assert.NotNull(await db.Pub_DocSequences.FirstOrDefaultAsync(x => x.BizKey == "SALES_ORDER"));
    }

    [Fact]
    public async Task Sequence_increments_and_resets_across_period()
    {
        var db = TestHelper.CreateInMemoryContext();
        var svc = new NumberingService(db) { UtcNowOverride = DateTime.Parse("2026-07-31") };
        var a = await svc.NextAsync("SALES_ORDER");
        var b = await svc.NextAsync("SALES_ORDER");
        Assert.EndsWith("00001", a); Assert.EndsWith("00002", b);
        svc.UtcNowOverride = DateTime.Parse("2026-08-01");              // 跨月
        Assert.EndsWith("00001", await svc.NextAsync("SALES_ORDER"));   // 月重置
    }

    [Fact]
    public async Task Concurrent_next_never_duplicates()                 // Sqlite 真事务
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        // …建 options 用 conn、EnsureCreated；32 个并行 Task 各取一号
        var all = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => /* 各自新 context 取号 */)));
        Assert.Equal(32, all.Distinct().Count());
    }
}
```

- [ ] **Step 5: 跑失败 → 实现 NumberingService**

```csharp
// CP6.Core/Services/Cfg/INumberingService.cs
public interface INumberingService
{
    /// <summary>取下一单号。独立短事务原子取号（不进业务事务——业务回滚产生跳号，跳号是正确行为不回收）。</summary>
    Task<string> NextAsync(string docType);
    /// <summary>保存/更新规则（Pattern 解析校验，坏模板拒存 E-CONF-010；乐观并发经 RowVersion）</summary>
    Task SaveRuleAsync(string docType, string pattern, int resetCycle, byte[]? rowVersion);
    Task<List<Pub_DocSequence>> ListRulesAsync();
}
```

实现要点（`NumberingService.cs`）：
1. `NextAsync`：行不存在→按 `NumberingDefaults.For(docType)` 插入（幂等 catch 唯一冲突后重读）。
2. 取号=独立事务：`IsSqlServer()` 走单语句原子 SQL（UPDLOCK 行锁，周期判定与 +1 同语句）：

```sql
UPDATE Pub_DocSequence WITH (UPDLOCK)
SET CurrentValue = CASE WHEN CurrentPeriod = {period} THEN CurrentValue + 1 ELSE 1 END,
    CurrentPeriod = {period}
OUTPUT inserted.CurrentValue
WHERE TenantId = {tid} AND BizKey = {docType}
```

   非 SQL Server（InMemory/Sqlite 测试）走 `BeginTransaction`+读改写（Sqlite 写事务天然串行，满足并发测试；InMemory 单线程测试够用）。
3. `Format` 用 `Pattern ?? 三段式合成`（legacy 行 `Prefix+DateFormat+SeqLength` 转译成等价 Pattern）。
4. `NumberingDefaults`：静态字典 `{ "SALES_ORDER": ("SO-{yyyy}{MM}-{seq:5}", 2), "QUOTATION": ("QT-{yyyy}{MM}-{seq:5}", 2) }`，未知 docType 用 `("{docType}-{yyyy}{MM}{dd}-{seq:4}", 1)` 兜底（docType 前缀字面量拼进 Pattern）。
5. `UtcNowOverride`（internal 属性）供测试注入时钟。

- [ ] **Step 6: Controller + DI**——`NumberingController`：`GET api/cfg/numbering/list`、`POST api/cfg/numbering/save`（权限 `cfg-center:Update`；`DbUpdateConcurrencyException` 由既有 409 管道处理）。DI：`AddScoped<INumberingService, NumberingService>()`。
- [ ] **Step 7: 全绿 + Commit + push**——`git commit -m "feat(cfg): ② INumberingService 原子取号+Pattern模板+默认回退(Pub_DocSequence演进)" && git push`

---

### Task 4: ③ 术语覆盖 + 租户 i18n 快照（stale/显式发布）

**Files:**
- Create: `CP6.Entity/DomainModels/Cfg/Cfg_TermOverride.cs`
- Create: `CP6.Core/Services/Cfg/ITerminologyResolver.cs`、`TerminologyResolver.cs`
- Modify: `CP6.WebApi/Services/LangPublishService.cs`（租户快照）
- Create: `CP6.WebApi/Controllers/Cfg/TerminologyController.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`、`CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/TerminologyResolverTests.cs`、`CP6.Tests/Cfg/TenantLangPublishTests.cs`
- 迁移: `CfgTermOverride`

**Interfaces:**
- Consumes: `Sys_Lang` 体系（`LangColumn.Codes`/`ToDict`）、`LangPublishService.PublishAsync` 现有产物结构 `wwwroot/i18n/{version}/{lang}.json + manifest.json`
- Produces: `ITerminologyResolver.ResolveAsync(string lang, IReadOnlyCollection<string> keys): Task<Dictionary<string,string>>`（解析顺序 Tenant→Pack→产品默认）；`LangPublishService.PublishTenantAsync(Guid tenantId, string publishedBy): Task<string>`（返回 version）；`GetTenantStateAsync(Guid tenantId): Task<TenantLangState>`（`record TenantLangState(string? Version, DateTime? PublishedAt, bool Stale)`）
- 快照路径约定（Task 13 前端消费）：`wwwroot/i18n/tenants/{tenantId}/{version}/{lang}.json` + `wwwroot/i18n/tenants/{tenantId}/manifest.json`；无覆盖租户无目录，前端 404 回退全局快照。

- [ ] **Step 1: 实体**

```csharp
// Cfg_TermOverride.cs
/// <summary>
/// 术语词典覆盖层（配置基建③）。唯一键 (TenantId,LangKey,Lang,Source)——
/// 租户手工行(Tenant)与包种子行(Pack)并存，解析 Tenant 优先；停用包只清 Source=Pack 行，手工覆盖不株连。
/// </summary>
[Table("Cfg_TermOverride")]
public class Cfg_TermOverride : BaseTenantEntity
{
    [Required, MaxLength(200)] public string LangKey { get; set; } = string.Empty;
    /// <summary>语言码（zh-CN/ja/en/…，值域=LangColumn.Codes）</summary>
    [Required, MaxLength(10)] public string Lang { get; set; } = string.Empty;
    /// <summary>Tenant=顾问手工 / Pack=行业包种子</summary>
    [Required, MaxLength(10)] public string Source { get; set; } = "Tenant";
    [Required, MaxLength(500)] public string OverrideText { get; set; } = string.Empty;
}
// CP6Context: e.HasIndex(x => new { x.LangKey, x.Lang, x.Source }).IsUnique()  → 自动 (TenantId,…) 前缀
```

- [ ] **Step 2: 解析器失败测试**——三层顺序：种 Sys_Lang 产品默认 `item.name="製品"`；加 Pack 行 `"品目"` → 解析得 Pack 值；再加 Tenant 行 `"物料"` → 解析得 Tenant 值；删 Tenant 行回落 Pack。缺键回落产品默认；产品默认也缺→原样返回 key。
- [ ] **Step 3: 实现 TerminologyResolver**——一条查询取两 Source 行 + `Sys_Langs` 默认值，内存合并优先级。约 40 行。跑至绿。
- [ ] **Step 4: 租户快照失败测试**

```csharp
[Fact]
public async Task Tenant_publish_writes_snapshot_and_clears_stale()
{
    // Arrange: 临时目录当 ContentRoot；一行 Tenant override
    var state0 = await svc.GetTenantStateAsync(tid);
    Assert.True(state0.Stale);                       // 有覆盖、从未发布 → stale
    var ver = await svc.PublishTenantAsync(tid, "tester");
    var json = File.ReadAllText(Path.Combine(root, "i18n/tenants", tid.ToString(), ver, "ja.json"));
    Assert.Contains("\"item.name\":\"物料\"", json.Replace(" ", ""));
    Assert.False((await svc.GetTenantStateAsync(tid)).Stale);   // 发布后不 stale
    // 再改一行覆盖 → stale 复燃
}
```

- [ ] **Step 5: 实现 PublishTenantAsync/GetTenantStateAsync**——生成逻辑复用现 `PublishAsync` 骨架：全局词条为底、`ITerminologyResolver` 覆盖后逐语言写 `{version}/{lang}.json`+`manifest.json`（version=`"v"+yyyyMMddHHmmss`，含 `publishedAt/publishedBy/count`）。**stale 判定不建新表**：`max(Cfg_TermOverride.ModifyDate ?? CreateDate) > manifest.publishedAt`。**保存词条绝不触发发布**（拍板 7.4 显式发布制）。
- [ ] **Step 6: Controller**——`api/cfg/terminology`：`GET list`（分页+按 LangKey 过滤）、`POST save`（upsert Tenant 行）、`DELETE {id}`、`GET state`（含 stale，前端提示「有未发布的术语变更」）、`POST publish`（权限 `cfg-center:Update`）。发布对静态文件是新版本目录+改 manifest 指针，天然原子，同步执行即可（后台任务 YAGNI）。
- [ ] **Step 7: 全绿 + Commit + push**——`git commit -m "feat(cfg): ③ 术语覆盖三层解析+租户i18n快照显式发布制" && git push`

---

### Task 5: ⑤ EntityFormBinding + 扩展字段答案

**Files:**
- Create: `CP6.Entity/DomainModels/Cfg/Cfg_EntityFormBinding.cs`、`Cfg_EntityFormData.cs`
- Create: `CP6.Core/Services/Cfg/IEntityFormBindingService.cs`、`EntityFormBindingService.cs`
- Create: `CP6.WebApi/Controllers/Cfg/FormBindingController.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`、`CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/EntityFormBindingTests.cs`
- 迁移: `CfgEntityFormBinding`

**Interfaces:**
- Consumes: `Wf_FormDef`（FormKey/SchemaJson/Version/Enable）
- Produces: `IEntityFormBindingService`：`ResolveAsync(string entityType): Task<FormBindingView?>`（`record FormBindingView(string FormKey, string SchemaJson, int FormVersion, string Placement)`）；`SaveBindingAsync(string entityType, string formKey, string placement)`；`GetDataAsync(string entityType, Guid entityId): Task<string?>`（DataJson）；`SaveDataAsync(string entityType, Guid entityId, string dataJson)`；`GetDataReadonlyAsync` 同 GetData（桌面端只读 JSON 接口，spec §4.3）

- [ ] **Step 1: 实体**

```csharp
[Table("Cfg_EntityFormBinding")]
public class Cfg_EntityFormBinding : BaseTenantEntity
{
    /// <summary>核心实体类型键（"Item"/"SalesOrder"…，值域=代码侧注册的可绑实体清单）</summary>
    [Required, MaxLength(100)] public string EntityType { get; set; } = string.Empty;
    /// <summary>SFS 表单稳定键（引用 Wf_FormDef.FormKey，不用 GUID——Bundle 平移免重映射）</summary>
    [Required, MaxLength(100)] public string FormKey { get; set; } = string.Empty;
    /// <summary>渲染位置（v1 只有 detail-footer）</summary>
    [Required, MaxLength(20)] public string Placement { get; set; } = "detail-footer";
    public bool Enable { get; set; } = true;
}
// 唯一索引 EntityType（v1 每实体最多一张表单）

[Table("Cfg_EntityFormData")]
public class Cfg_EntityFormData : BaseTenantEntity
{
    [Required, MaxLength(100)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    [Required, MaxLength(100)] public string FormKey { get; set; } = string.Empty;
    /// <summary>答案落库时的表单版本（留痕；旧版本答案不迁移）</summary>
    public int FormVersion { get; set; }
    [Column(TypeName = "nvarchar(max)")] public string DataJson { get; set; } = "{}";
}
// 唯一索引 (EntityType, EntityId)
```

- [ ] **Step 2: 失败测试**——①绑定 FormKey 不存在/Enable=false → `E-CONF-015` 拒存；②Resolve 返回 SchemaJson；③SaveData 对 SchemaJson 里 `required:true` 字段缺值 → `E-CONF-016`；④答案 roundtrip；⑤**硬边界**：断言 `Cfg_EntityFormData` 无任何核心表回写（纯本表 upsert）。
- [ ] **Step 3: 实现**——required 校验：解析 SchemaJson 的 `fields[].{key,required}`（与 Wf_FormDef 注释「后端 required/类型复核」同款口径；若 OA 已有 schema 校验 helper——`grep -rn "required" CP6.Core/Services/Wf | grep -i schema` 先找——直接复用，没有再写 20 行 System.Text.Json 遍历）。可绑实体清单：`public static class ExtendableEntities { public static readonly string[] Keys = { "Item", "SalesOrder", "Quotation", "BusinessPartner" }; }`，绑定保存校验 EntityType ∈ 清单（越界 `E-CONF-017`）。
- [ ] **Step 4: Controller**——`api/cfg/form-binding`：`GET resolve?entityType=`（登录即可，详情页用）、`GET data?entityType=&entityId=`、`POST data`、`GET list`/`POST save`（`cfg-center:Update`）。
- [ ] **Step 5: 全绿 + Commit + push**——`git commit -m "feat(cfg): ⑤ SFS实体绑定+扩展字段答案表(不回写核心表)" && git push`

---

### Task 6: ④ 状态机声明框架 + Sales 主干注册

**Files:**
- Create: `CP6.Core/Services/Cfg/DocFlow/DocFlowDefinition.cs`、`DocFlowRegistry.cs`、`DocFlowDefinitions.Sales.cs`
- Test: `CP6.Tests/Cfg/DocFlowRegistryTests.cs`

**Interfaces:**
- Produces（Task 7/8/12 与 Sales v2 消费）:

```csharp
public sealed record DocFlowEdge(string From, string To, bool IsAlternate = false, string? WhenDisabled = null);
public sealed class DocFlowDefinition
{
    public required string DocType { get; init; }                 // "SALES_ORDER"
    public required IReadOnlyList<string> States { get; init; }
    public required IReadOnlyList<string> OptionalSteps { get; init; }  // 可裁白名单（编译期锁死）
    public required IReadOnlyList<DocFlowEdge> Edges { get; init; }     // 主边+预声明备选边
    public bool CanDisableWhole { get; init; }                    // 整环节可关（Quotation=true）
    public void Validate();   // 自检：备选边 WhenDisabled∈OptionalSteps；每个 Optional 步被关后图仍连通
}
public static class DocFlowRegistry
{
    public static void Register(DocFlowDefinition def);           // 重复 DocType 抛 InvalidOperationException
    public static DocFlowDefinition Get(string docType);          // 未注册抛 BizException("E-CONF-020")
    public static IReadOnlyCollection<DocFlowDefinition> All { get; }
    public static void ResetForTests();
}
```

- [ ] **Step 1: 失败测试**——①注册后 Get 取回；②`Validate()` 拒绝「备选边 WhenDisabled 指向非 Optional 步」；③拒绝「Optional 步被关后无备选边可达其后继」（口径2 结构性消解悬空——用一个故意缺备选边的定义断言抛）；④SALES_ORDER/QUOTATION 两个真实定义注册成功且 `Validate()` 通过。
- [ ] **Step 2: 跑失败 → 实现**——Registry 用 `ConcurrentDictionary`（静态，启动注册）。Sales 定义（拍板终局）：

```csharp
// DocFlowDefinitions.Sales.cs —— Sales v2 主干（07-10 边界盘点拍板终局），Program.cs 启动时调用 RegisterAll()
public static class DocFlowDefinitions
{
    public static void RegisterAll()
    {
        DocFlowRegistry.Register(new DocFlowDefinition
        {
            DocType = "SALES_ORDER",
            States = new[] { "Draft", "Confirmed", "InFulfillment", "Shipped", "Invoiced", "Closed", "Cancelled" },
            OptionalSteps = new[] { "Confirmed", "Invoiced" },      // Shipped/Closed/Cancelled 永不可裁
            Edges = new DocFlowEdge[]
            {
                new("Draft", "Confirmed"), new("Confirmed", "InFulfillment"),
                new("InFulfillment", "Shipped"), new("Shipped", "Invoiced"), new("Invoiced", "Closed"),
                new("Draft", "Cancelled"), new("Confirmed", "Cancelled"), new("InFulfillment", "Cancelled"),
                new("Draft", "InFulfillment", IsAlternate: true, WhenDisabled: "Confirmed"),   // 预声明备选边
                new("Shipped", "Closed",     IsAlternate: true, WhenDisabled: "Invoiced"),
            },
        });
        DocFlowRegistry.Register(new DocFlowDefinition
        {
            DocType = "QUOTATION",
            States = new[] { "Draft", "Submitted", "Confirmed", "Converted", "Expired", "Cancelled" },
            OptionalSteps = new[] { "Submitted" },
            CanDisableWhole = true,                                  // 整环节可关（仅藏新建，在途走完可转单）
            Edges = new DocFlowEdge[]
            {
                new("Draft", "Submitted"), new("Submitted", "Confirmed"), new("Confirmed", "Converted"),
                new("Draft", "Cancelled"), new("Submitted", "Cancelled"), new("Confirmed", "Expired"),
                new("Draft", "Confirmed", IsAlternate: true, WhenDisabled: "Submitted"),
            },
        });
    }
}
```

- [ ] **Step 3: 全绿 + Commit + push**——`git commit -m "feat(cfg): ④ DocFlow状态机声明框架+Sales主干注册(编译期锁死)" && git push`

---

### Task 7: ④ Cfg_DocFlowConfig 表 + 保存校验 + dry-run

**Files:**
- Create: `CP6.Entity/DomainModels/Cfg/Cfg_DocFlowConfig.cs`
- Create: `CP6.Core/Services/Cfg/DocFlow/IDocFlowGuard.cs`、`DocFlowConfigService.cs`（含接口）
- Create: `CP6.WebApi/Controllers/Cfg/DocFlowConfigController.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`、`CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/DocFlowConfigServiceTests.cs`
- 迁移: `CfgDocFlowConfig`

**Interfaces:**
- Consumes: `DocFlowRegistry`（Task 6）、`Wf_ApprovalBinding`、`IFeatureGate`
- Produces:

```csharp
[Table("Cfg_DocFlowConfig")]
public class Cfg_DocFlowConfig : BaseTenantEntity   // 唯一索引 DocType
{
    [Required, MaxLength(50)] public string DocType { get; set; } = string.Empty;
    /// <summary>整环节关闭（仅 CanDisableWhole=true 的 DocType 可设；效果=藏新建，在途走完）</summary>
    public bool DocTypeDisabled { get; set; }
    [Column(TypeName = "nvarchar(max)")] public string DisabledStepsJson { get; set; } = "[]";   // string[]
    [Column(TypeName = "nvarchar(max)")] public string ApprovalPointsJson { get; set; } = "[]";  // ApprovalPoint[]
    [Column(TypeName = "nvarchar(max)")] public string GuardConfigsJson { get; set; } = "[]";    // GuardConfig[]
    [Column(TypeName = "nvarchar(max)")] public string? SubStateLabelsJson { get; set; }         // 仅展示标签
}
public sealed record ApprovalPoint(string EnterState, string BizType);   // 迁移点→BizType，仅此而已（流程选择归 Wf_ApprovalBinding）
public sealed record GuardConfig(string EnterState, string GuardKey, string? ParamsJson);
public interface IDocFlowGuard   // 校验器：代码注册（含行业包注册的），DI 多实现
{
    string Key { get; }          // "sales.credit-limit"
    Task ValidateAsync(DocFlowGuardContext ctx);   // 不通过抛 BizException
}
public sealed record DocFlowGuardContext(string DocType, string FromState, string ToState, object? Payload, string? ParamsJson);
public interface IDocFlowConfigService
{
    Task<Cfg_DocFlowConfig?> GetAsync(string docType);
    Task SaveAsync(Cfg_DocFlowConfig cfg);                       // 全部校验在此，坏配置拒存
    Task<DocFlowDryRunReport> DryRunAsync(string docType, Cfg_DocFlowConfig proposed);
}
public sealed record DocFlowDryRunReport(List<string> Warnings);  // 人读清单
```

- [ ] **Step 1: 失败测试（保存校验矩阵——每条一个 Fact）**
  - 越界裁剪：`DisabledSteps=["Shipped"]` → `E-CONF-021`（不在 OptionalSteps 白名单）
  - 合法组合可存：`["Confirmed"]` ✔、`["Confirmed","Invoiced"]` ✔（备选边覆盖完整性由 Task 6 Validate 编译期保证）
  - Guard 键不存在：`GuardConfigs=[{Confirmed,"no.such.guard"}]` → `E-CONF-022`（DI 注册的 IDocFlowGuard.Key 集合里查）
  - ApprovalPoint 的 EnterState 不在 States → `E-CONF-023`；BizType 空 → `E-CONF-023`
  - `DocTypeDisabled=true` 而 `CanDisableWhole=false`（SALES_ORDER）→ `E-CONF-024`
  - 未注册 DocType → `E-CONF-020`
  - **F1 联动（拍板 #4）**：`DisabledSteps` 含 `Invoiced` 且 `IFeatureGate.IsEnabledAsync("module.f1") == true` → dry-run Warning「F1 开着但 Invoiced 关闭」；保存仍允许但 Warning 必须出现在响应里
- [ ] **Step 2: 跑失败 → 实现 SaveAsync 校验链 + DryRunAsync**——dry-run 三类 Warning：①ApprovalPoint 的 BizType 无启用 Wf_ApprovalBinding →「迁移点将被 fail-closed 卡死（E-CONF-025），请先配绑定或删审批点」；②关 Invoiced 的 F1 联动警告；③GuardKey 来自包（Key 前缀 `pack.`）而对应包开关已关 →「悬空键将拦迁移」。
- [ ] **Step 3: Controller**——`api/cfg/docflow`：`GET list`（All 定义+各自当前配置+OptionalSteps 白名单供前端渲染勾选）、`POST save`（响应带 dry-run Warnings）、`POST dry-run`。权限 `cfg-center:Update`。
- [ ] **Step 4: 全绿 + Commit + push**——`git commit -m "feat(cfg): ④ Cfg_DocFlowConfig+保存校验矩阵+dry-run(E-CONF-02x)" && git push`

---

### Task 8: ④ DocFlowEngine 运行时判定（per-action / fail-closed / 审批接缝）

**Files:**
- Create: `CP6.Core/Services/Cfg/DocFlow/IDocFlowEngine.cs`、`DocFlowEngine.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/DocFlowEngineTests.cs`

**Interfaces:**
- Consumes: `DocFlowRegistry`、`IDocFlowConfigService`、`IDocFlowGuard` 集合、`IApprovalService`（现有 `CP6.Core/Services/Wf/IApprovalService.cs`——实现前先读它拿准确签名）、`Wf_ApprovalBinding`
- Produces（Sales v2 唯一迁移入口）:

```csharp
public interface IDocFlowEngine
{
    /// <summary>迁移判定+执行副作用（守卫、审批提交）。per-action：每次调用按「当时的」配置评估，绝无快照。</summary>
    Task<TransitionDecision> TransitionAsync(TransitionRequest req);
}
public sealed record TransitionRequest(string DocType, Guid EntityId, string FromState, string ToState, object? Payload);
public abstract record TransitionDecision
{
    public sealed record Allowed : TransitionDecision;                        // 调用方落状态
    public sealed record PendingApproval(string BizType, Guid InstanceId) : TransitionDecision;  // 已 SubmitAsync，调用方置等待态
    // 拒绝一律抛 BizException（E-CONF-026 非法迁移 / E-CONF-025 审批点无绑定 / E-CONF-027 悬空Guard键 / 守卫自带码）
}
```

- [ ] **Step 1: 失败测试（语义矩阵）**
  - 主边直行：无配置，`Draft→Confirmed` → Allowed
  - 非图上边：`Draft→Shipped` → `E-CONF-026`
  - **关 Confirmed 后**：`Draft→InFulfillment`（备选边）→ Allowed；`Draft→Confirmed`（进被关步）→ `E-CONF-026`
  - **离开被关状态不拦**（口径3）：配置关 Confirmed，但单已停在 Confirmed → `Confirmed→InFulfillment` Allowed
  - 备选边未启用时不可走：无配置时 `Draft→InFulfillment` → `E-CONF-026`
  - Guard 执行：注册假守卫（Key="test.always-fail" 抛 `E-TEST-001`），配置挂 Confirmed 迁入 → `Draft→Confirmed` 抛 `E-TEST-001`；**悬空键**（配置里的 Key 已不在 DI）→ `E-CONF-027` fail-closed
  - 审批点：配置 `ApprovalPoint(Confirmed,"SALES_ORDER_CONFIRM")`+启用 Binding（Moq IApprovalService 返回实例 Id）→ 返回 PendingApproval 且 SubmitAsync 恰被调一次；**Point 在 Binding 停用** → `E-CONF-025`（拍板 #3 fail-closed）
  - per-action 验证：同一单第一次调用时配置 A、改配置后第二次调用按 B 语义（两次 Transition 断言不同结果）
- [ ] **Step 2: 跑失败 → 实现**——判定序：①`DocFlowRegistry.Get`；②整环节关闭且动作=新建入口由 Controller 层拦（引擎不管创建）；③有效边集合=主边(To∉Disabled) ∪ 备选边(WhenDisabled∈Disabled)——From 是否被关不参与（离开不拦）；④Guards（fail-closed）；⑤ApprovalPoints：查启用 Binding，有→SubmitAsync 返回 PendingApproval，无→E-CONF-025。审批通过后的最终落状由业务回调（IApprovalCallback 实现方）持有——引擎只负责判定+提交，落库归调用方（回调需幂等+状态守卫，写进接口 XML 注释供 Sales v2 遵守）。
- [ ] **Step 3: 全绿 + Commit + push**——`git commit -m "feat(cfg): ④ DocFlowEngine per-action判定+fail-closed守卫+审批点接缝" && git push`

---

### Task 9: WFS 保存闸 + Pur fail-open 收敛（拍板 #2/#3 落地）

**Files:**
- Modify: FlowDef 保存路径——先 `grep -rn "SchemaJson" CP6.Core/Services/Wf --include="*Service*"` 定位（FlowAdmin 用的保存服务），在更新分支加闸
- Modify: `CP6.Core/Services/Wf/ApprovalService.cs`（PASSTHROUGH 直通语义）
- Modify: `CP6.Core/Services/Pur/Contracts/ApprovalServiceAdapter.cs`（fail-open 删除）
- 迁移: `SeedPassthroughBindings`（数据迁移）
- Test: `CP6.Tests/Cfg/FlowDefSaveGateTests.cs`、`CP6.Tests/Cfg/ApprovalPassthroughTests.cs`

**Interfaces:**
- Produces: 常量 `ApprovalFlowKeys.Passthrough = "sys-passthrough"`（放 `CP6.Core/Services/Wf/ApprovalFlowKeys.cs`）——Binding.FlowKey 为此值=显式直通（AutoApproved+同步回调）。**「无绑定=自动放行」语义全系统删除。**

- [ ] **Step 1: 保存闸失败测试**——建 FlowDef+一个 Running `Wf_FlowInstance`（FlowKey 同）→ 改 SchemaJson 保存 → `E-WF-101`；无在途实例改 → 允许；只改 FlowName 不改 SchemaJson → 允许；删除 FlowDef 有在途 → `E-WF-102`。
- [ ] **Step 2: 实现闸**——定位到的保存方法里：`SchemaJson` 有变更（字符串比对）且 `_db.Wf_FlowInstances.Any(i => i.FlowKey == key && i.Status == Running态值)`（先读实体确认 Running 的实际表示）→ 抛。运维正道写进异常 i18n 文案：「建新 FlowKey+换绑」。
- [ ] **Step 3: PASSTHROUGH 失败测试**——①Binding.FlowKey=Passthrough → `SubmitAsync` 不起流程实例、回调 OnApproved 同步触发（Moq IApprovalCallback 验证）、返回标记 AutoApproved 的结果；②**无绑定 → 抛（原语义保持）**；③`ApprovalServiceAdapter` 无绑定 → 不再自动放行，抛与 ApprovalService 同码（先读 Adapter 现返回类型，保持接口不变只改行为）。
- [ ] **Step 4: 实现 + 种子迁移**——`SeedPassthroughBindings` 数据迁移（幂等 NOT EXISTS，样板 SysRoleTenantize）：对每个现有租户（`SELECT Id FROM Sys_Tenant`）的 `PUR_PR`/`PUR_PO`，若无任何 Wf_ApprovalBinding 行则插 `FlowKey='sys-passthrough', Enable=1, Remark='fail-open收敛迁移种子——原自动放行语义显式化'`。已配真实流程的租户不受影响。
- [ ] **Step 5: 全绿（重点全量跑，Pur 既有测试可能依赖 fail-open——逐个改为先种 PASSTHROUGH 绑定）+ Commit + push**——`git commit -m "fix(wf): WFS在途保存闸E-WF-101/102+Pur fail-open收敛为显式直通绑定" && git push`

---

### Task 10: ConditionJson 条件选流程 + WithdrawAsync 撤回

**Files:**
- Modify: `CP6.Core/Services/Wf/ApprovalService.cs`（两能力都在此）
- Create: `CP6.Core/Services/Wf/ApprovalConditionEvaluator.cs`
- Modify: `CP6.Core/Services/Wf/IApprovalService.cs`、`IApprovalCallback.cs`（加默认实现方法）
- Modify: `CP6.Core/Services/Pur/PurchaseRequestService.cs`、`PurchaseOrderService.cs`（撤回后回 Draft 的回退处理，随 OnWithdrawn 回调）
- Test: `CP6.Tests/Cfg/ApprovalConditionTests.cs`、`CP6.Tests/Cfg/ApprovalWithdrawTests.cs`

**Interfaces:**
- Produces:

```csharp
// ConditionJson 契约（Wf_ApprovalBinding.ConditionJson，原「预留」字段激活）：
// [{ "when": { "field": "Amount", "op": ">", "value": 100000 }, "flowKey": "sales-confirm-high" }]
// 顺序求值，首个命中生效；无命中/无条件 → binding.FlowKey。op ∈ {">", ">=", "<", "<=", "==", "!="}，value 为 number 或 string。
public static class ApprovalConditionEvaluator
{
    /// <summary>facts=业务事实字典（调用方传，如 {"Amount": 250000}）。坏 JSON 抛 E-WF-110（绑定保存时也校验）。</summary>
    public static string SelectFlowKey(string bindingFlowKey, string? conditionJson, IReadOnlyDictionary<string, object?> facts);
}
// IApprovalService 增：
Task WithdrawAsync(string bizType, string bizId, Guid operatorUserId);   // Running→Withdrawn 终态，触发 OnWithdrawnAsync
// IApprovalCallback 增（C#8 默认实现，存量实现零改动）：
Task OnWithdrawnAsync(string bizType, string bizId) => Task.CompletedTask;
```

- [ ] **Step 1: Evaluator 失败测试**——命中高额/未命中回默认/多规则顺序/字符串等值/坏 JSON E-WF-110/facts 缺 field 视为未命中。纯函数表驱动 Theory。
- [ ] **Step 2: 实现 Evaluator + SubmitAsync 接线**——先读 `ApprovalService.SubmitAsync` 现签名；facts 参数新增为可选 `IReadOnlyDictionary<string, object?>? facts = null`（存量调用零改动）。绑定保存路径（找到 Binding 的管理保存点）加 ConditionJson 解析校验。
- [ ] **Step 3: Withdraw 失败测试**——①Running 实例 Withdraw → 实例状态 Withdrawn、OnWithdrawnAsync 恰一次；②无 Running 实例 → `E-WF-111`；③PUR_PR 撤回后 PR 状态回 Draft（PurApprovalCallback 实现 OnWithdrawnAsync；先读 PrStatus 枚举确认 Draft 表示）；④撤回后可再次 SubmitAsync（防重键已释放）。
- [ ] **Step 4: 实现**——ApprovalService.WithdrawAsync：找 Running 实例→引擎终止为 Withdrawn（先看 FlowEngine 有无终止 API，无则直接置实例状态+写流转历史行，与引擎同事务）→回调。PR/PO 的 OnWithdrawnAsync 把单据置回可编辑态（即拍板 #6 的「回退边」——PendingApproval/Submitted→Draft）。
- [ ] **Step 5: 全绿 + Commit + push**——`git commit -m "feat(wf): ConditionJson条件选流程+管理员撤回WithdrawAsync(回退边)" && git push`

---

### Task 11: 行业包骨架 + 纸器包空壳

**Files:**
- Create: `CP6.Core/Services/Cfg/Packs/Hooks/IPricingHook.cs`、`IDocExtensionProvider.cs`、`IItemValidationHook.cs`
- Create: `CP6.Core/Services/Cfg/Packs/IIndustryPack.cs`、`PackRegistry.cs`、`PackEnableService.cs`（含 IPackEnableService）
- Create: 新项目 `CP6.Packs.PaperPack/CP6.Packs.PaperPack.csproj`（net8.0，引用 CP6.Core）+ `PaperPack.cs` + `Seeds/terminology.json` + `Seeds/features.json`
- Create: `CP6.WebApi/Controllers/Cfg/PackController.cs`
- Modify: `CP6.sln`（加项目）、`CP6.WebApi/CP6.WebApi.csproj`（引用 PaperPack）、`CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/PackRegistryTests.cs`、`CP6.Tests/Cfg/PackEnableServiceTests.cs`

**Interfaces:**
- Consumes: `IFeatureGate`（Task 1）、`Cfg_TermOverride`（Task 4，Source="Pack" 行）
- Produces（v1 恰三个钩子，均以纸器包为真实消费者；Item 通用化/Sales v2 后续接线）:

```csharp
// 钩子契约（CP6.Core/Services/Cfg/Packs/Hooks/）
public sealed record PricingContext(string ItemCd, decimal Qty, string CurrencyCd, decimal? BasePrice, IReadOnlyDictionary<string, object?> Facts);
public sealed record PriceResult(decimal UnitPrice, string SourceKey);   // SourceKey="pack.paperpack.sheet-price" 留痕
public interface IPricingHook { Task<PriceResult?> ApplyAsync(PricingContext ctx); }   // null=本包不处理

public sealed record DocExtensionBlock(string EntityType, string ComponentKey, int SortOrder);  // 前端按 ComponentKey 挂载包组件
public interface IDocExtensionProvider { IReadOnlyList<DocExtensionBlock> GetBlocks(string entityType); }

public sealed record ItemValidationContext(string ItemCd, string CategoryCd, IReadOnlyDictionary<string, object?> Fields);
public interface IItemValidationHook { Task ValidateAsync(ItemValidationContext ctx); }  // 不通过抛 BizException

// 包契约
public interface IIndustryPack
{
    string PackKey { get; }        // "paperpack" → FeatureKey = $"pack.{PackKey}"
    string DisplayName { get; }
    PackSeedManifest GetSeeds();   // record PackSeedManifest(List<TermSeed> Terminology, List<FeatureSeed> Features)
                                   // record TermSeed(string LangKey, string Lang, string Text); record FeatureSeed(string FeatureKey, bool Enabled)
    void RegisterHooks(IPackHookRegistry registry);   // registry.AddPricing(hook) / AddDocExtension / AddItemValidation
}

// 路由：核心代码唯一入口（未启用租户零开销直路）
public interface IPackRouter
{
    Task<IReadOnlyList<IPricingHook>> PricingHooksAsync();          // 只返回当前租户已启用包的钩子
    Task<IReadOnlyList<IDocExtensionProvider>> DocExtensionsAsync();
    Task<IReadOnlyList<IItemValidationHook>> ItemValidationsAsync();
}

public interface IPackEnableService
{
    Task<PackDryRunReport> DryRunAsync(string packKey);   // 种子清单（将写入的术语/开关行）+ 停用时受影响 GuardConfigs 清单
    Task EnableAsync(string packKey);                     // 开关 pack.{key}=true + 种子落库（术语 Source=Pack upsert）
    Task DisableAsync(string packKey);                    // 只关开关+清 Source=Pack 术语行；业务数据/手工覆盖不动
    Task<List<PackInfo>> ListAsync();                     // record PackInfo(string PackKey, string DisplayName, bool Enabled)
}
```

- [ ] **Step 1: 失败测试**——①PackRegistry 枚举 DI 里的 IIndustryPack（注册 PaperPack 假体断言可见）；②PackRouter：开关关 → 三钩子列表全空；开 → 含包钩子（真实 PaperPack 空壳钩子）；③EnableAsync：种子术语落库为 Source=Pack 行、`pack.paperpack` 开；④DisableAsync：Pack 行清除、同键 Tenant 手工行**存活**（拍板不株连语义）；⑤DryRun 返回种子清单。
- [ ] **Step 2: 实现骨架**——PackRegistry=DI `IEnumerable<IIndustryPack>` 的索引封装（Singleton）；PackRouter（Scoped）逐包查 `IFeatureGate.IsEnabledAsync($"pack.{key}")` 过滤（开关本身有两层缓存，无需再缓存）。PackEnableService.Enable/Disable 单事务。
- [ ] **Step 3: 纸器包空壳**——新项目：

```csharp
// CP6.Packs.PaperPack/PaperPack.cs
public class PaperPack : IIndustryPack
{
    public string PackKey => "paperpack";
    public string DisplayName => "纸器行业包";
    public PackSeedManifest GetSeeds() => PackSeedLoader.FromEmbedded(typeof(PaperPack).Assembly, "Seeds");  // 嵌入资源 JSON
    public void RegisterHooks(IPackHookRegistry r)
    {
        r.AddPricing(new PaperPricingHook());          // v1 空壳：ApplyAsync 恒返回 null（平米单价换算迁入是 Item 通用化 plan 的活）
        r.AddDocExtension(new PaperDocExtensionProvider());  // 空列表
        r.AddItemValidation(new PaperItemValidationHook());  // 恒通过
    }
}
```

`Seeds/terminology.json`（首个门面用例，拍板 §1③）：`[{"langKey":"item.name","lang":"ja","text":"品目"},{"langKey":"item.name","lang":"zh-CN","text":"品目"}]`（终值以 Item 通用化时补齐，此处两行即可验证机制）；`Seeds/features.json`：`[]`。csproj 里 `<EmbeddedResource Include="Seeds/*.json" />`。
- [ ] **Step 4: Controller + DI**——`api/cfg/pack`：`GET list`、`POST dry-run {packKey}`、`POST enable`、`POST disable`（`cfg-center:Update`）。Program.cs：`AddSingleton<IIndustryPack, PaperPack>()`、Registry/Router/EnableService 注册。
- [ ] **Step 5: 全绿 + Commit + push**——`git commit -m "feat(pack): 行业包骨架三钩子+PackRouter租户路由+纸器包空壳(种子/启停/不株连)" && git push`

---

### Task 12: ⑥ ConfigBundle 导出/导入 + 三分类 dry-run

**Files:**
- Create: `CP6.Core/Services/Cfg/Bundle/ConfigBundle.cs`（DTO）、`IConfigBundleService.cs`、`ConfigBundleService.cs`
- Create: `CP6.WebApi/Controllers/Cfg/ConfigBundleController.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Cfg/ConfigBundleTests.cs`

**Interfaces:**
- Consumes: 六件套全部表 + `Wf_FormDef`（FormKey）+ `Wf_FlowDef`/`Wf_ApprovalBinding`（WFS 定义）
- Produces:

```csharp
public sealed class ConfigBundle
{
    public int SchemaVersion { get; set; } = ConfigBundleSchema.Version;   // const int Version = 1
    public string ExportedBy { get; set; } = "";
    public DateTime ExportedAt { get; set; }
    public List<FeatureDto> Features { get; set; } = new();
    public List<NumberingRuleDto> NumberingRules { get; set; } = new();
    public List<TermOverrideDto> TermOverrides { get; set; } = new();      // 含 Source
    public List<DocFlowConfigDto> DocFlowConfigs { get; set; } = new();
    public List<FormDefDto> FormDefs { get; set; } = new();                // FormKey/FormName/SchemaJson/Version
    public List<FormBindingDto> FormBindings { get; set; } = new();        // 按 FormKey 引用
    public List<FlowDefDto> FlowDefs { get; set; } = new();                // FlowKey/SchemaJson
    public List<ApprovalBindingDto> ApprovalBindings { get; set; } = new();
}
public interface IConfigBundleService
{
    Task<ConfigBundle> ExportAsync();
    Task<BundleDryRunReport> DryRunImportAsync(ConfigBundle bundle);
    Task ImportAsync(ConfigBundle bundle);   // 内部先 DryRun，有 Blocker 抛 E-CONF-031
}
// 三分类报告（spec §1⑥ 拍板结构）
public sealed record BundleDryRunReport(
    List<string> Portable,        // 可平移：词典/编号/开关/裁剪——逐节计数描述
    List<string> Remapped,        // 需重映射：FormKey 冲突处理决定（同键同 Schema=跳过；同键异 Schema=报 Blocker）
    List<string> ManualRework,    // 不可平移：FlowDef SchemaJson 内的租户内主数据引用，逐条点名
    List<string> Blockers);       // SchemaVersion 不符(E-CONF-030)/FormKey 冲突等
```

- [ ] **Step 1: 失败测试**——①导出→空租户导入→六件套行数一致（roundtrip）；②SchemaVersion=99 导入 → `E-CONF-030` 拒绝；③FlowDef SchemaJson 含 `"assigneeType":"Specified"`/`"userId":"…"` 模式 → dry-run ManualRework 逐条点名且导入后该 FlowDef `Enable=false`（待配置态）；④目标租户已有同 FormKey 但 SchemaJson 不同 → Blocker；⑤导入不覆盖目标租户已有 Tenant 术语行（防顾问手工成果被冲）。
- [ ] **Step 2: 实现**——Export=逐表 AsNoTracking 摘 DTO。DryRun：ManualRework 扫描=对每个 FlowDef.SchemaJson 做 JSON 遍历找 `Specified/userId/deptId/roleId` 值非空的节点（先读一个真实 FlowDef SchemaJson 确认节点字段名再定扫描键）。Import 单事务：upsert 顺序 Features→Numbering→Terms→FormDefs→Bindings→FlowDefs(不可平移置 Enable=false)→ApprovalBindings→DocFlowConfigs（最后——其校验依赖前面的 Guard/Binding 已就位）；复用各件套 Service 的 SaveAsync 走同一套校验，不绕过。
- [ ] **Step 3: Controller**——`api/cfg/bundle`：`GET export`（文件下载 `cp6-config-{tenantCode}-{yyyyMMdd}.json`）、`POST dry-run`（multipart 上传）、`POST import`。权限 `cfg-center:Update`。
- [ ] **Step 4: 全绿 + Commit + push**——`git commit -m "feat(cfg): ⑥ ConfigBundle导出导入+三分类dry-run+schema版本闸" && git push`

---

### Task 13: 顾问配置中心前端（糙版）

**Files:**
- Create: `cp6.web/src/api/cfg.ts`（六组 API 封装，axios 惯例照 `src/api/` 现有文件）
- Create: `cp6.web/src/views/cfg/ConfigCenterView.vue`
- Modify: `cp6.web/src/router/index.ts`（viewModules 加 `'/cfg/center': () => import('@/views/cfg/ConfigCenterView.vue')`）
- 菜单/权限种子: 照现有 Seed 惯例（`grep -rn "Sys_Menu" CP6.WebApi/Seed` 找最近一个菜单种子文件抄结构）加菜单行 `cfg-center`（名称「配置中心」，路由 `/cfg/center`，Action: Search/Update）
- Test: 手动 QA 清单（见 Step 3）；前端跑 `npm run test`（369 绿基线不减）

**Interfaces:**
- Consumes: Task 2/3/4/5/7/11/12 的全部 API 端点
- Produces: 顾问可用的六页签管理界面（spec 拍板「界面可糙、必须可导出复制」）

- [ ] **Step 1: api/cfg.ts**——按端点一比一封装：`featureList/featureSet`、`numberingList/numberingSave`、`termList/termSave/termDelete/termState/termPublish`、`docflowList/docflowSave/docflowDryRun`、`bindingList/bindingSave`、`packList/packDryRun/packEnable/packDisable`、`bundleExport/bundleDryRun/bundleImport`。
- [ ] **Step 2: ConfigCenterView.vue**——`el-tabs` 六页签：开关（el-table+el-switch 行内切换）；采番（表格+编辑对话框：DocType/Pattern/ResetCycle，保存失败按 message 显示 E-CONF-010 译文）；术语（表格+新增对话框+顶部 stale 横幅「有未发布的术语变更」+发布按钮）；单据流（每 DocType 一卡片：OptionalSteps 勾选=关，审批点/Guard 用 JSON 文本域糙版编辑，保存后 Warnings 用 el-alert 列出）；SFS 绑定（EntityType 下拉=可绑清单 + FormKey 下拉=Wf_FormDef 列表）；包与导出（包列表启停+dry-run 结果对话框；Bundle 导出下载/上传 dry-run 三分类分节展示/导入按钮——ManualRework 非空时导入按钮旁红字提示）。全部文案走 i18n key（`cfg.*` 命名空间），五语词条进 Task 14 的种子。
- [ ] **Step 3: 手动 QA 清单（dev server 起后逐条过）**——①开关翻转→菜单立刻隐现；②建采番规则 SO-{yyyy}{MM}-{seq:5} 保存→坏模板被拒并显示译文；③加术语覆盖→stale 横幅出现→发布→横幅消失；④关 Quotation 的 Submitted 步保存成功、试关 SALES_ORDER 的 Shipped 被拒；⑤绑一张 SFS 表单到 Item；⑥启用纸器包 dry-run→启用→术语页出现 Pack 行；⑦导出 Bundle→新租户导入 dry-run 三分类可读。
- [ ] **Step 4: Commit + push**——`git commit -m "feat(cfg): 顾问配置中心六页签(糙版)+菜单权限种子" && git push`

---

### Task 14: E2E 金线 + 横切 DoD 收口

**Files:**
- Create: `CP6.Tests/Cfg/ConfigPlatformE2ETests.cs`
- Create: `CP6.WebApi/Seed/I18nCfgErrorSeed.cs`（E-CONF-001..031/E-WF-101..111 全部码的五语词条，照 `I18nTenantComplianceSeed` 结构）
- Create: `docs/erp/配置基建运维须知.md`
- Modify: 各 Cfg_ 实体加 `IAuditable`（字段审计 opt-in）
- Test: 全量 `dotnet test`

- [ ] **Step 1: E2E 金线失败测试**（spec §5 测试层3，InMemory 内联全链）：

```csharp
[Fact]
public async Task New_tenant_golden_path()
{
    // 开新租户（Sys_Tenant 插行 + 租户上下文切换）
    // → PackEnableService.EnableAsync("paperpack")：开关亮、术语 Pack 行落库
    // → ITerminologyResolver 解析 item.name 得包值「品目」
    // → INumberingService.NextAsync("QUOTATION") 得 QT-202607-00001
    // → DocFlowConfig：关 QUOTATION.Submitted 保存成功
    // → DocFlowEngine：Draft→Confirmed（备选边）Allowed；Draft→Submitted 拒 E-CONF-026
    // → 另一租户全程零感知（并行断言其解析/开关/采番不受影响——共享库名双上下文法）
}
[Fact]
public async Task Trim_variant_skip_quotation()   // 裁剪变体：跳见积租户
{
    // DocTypeDisabled=true(QUOTATION) → 新建入口判定 API 拒；SALES_ORDER 直接 Draft→Confirmed 走通
}
```

- [ ] **Step 2: 错误码词条种子**——本计划全部错误码逐个登记五语（zh-CN/ja/en/其余两语照 LangColumn.Codes），文案含运维正道（如 E-WF-101：「流程有在途实例，禁止原地修改；请建新 FlowKey 并换绑」）。同时在 `docs/00-横切接线规范.md` 的错误码总纲登记 E-CONF/E-WF 新段（先读该文件找登记节）。
- [ ] **Step 3: 运维须知文档**——四条拍板口径成文：跳号不回收；开关 ≤60s 最终一致；per-action（在途单行为随配置变，含「反向开回环节」在途单需过新环节）；改流程=建新 FlowKey+换绑。
- [ ] **Step 4: IAuditable 挂接**——五个 Cfg_ 实体声明 `IAuditable`（配置变更留痕），跑一条字段审计断言测试。
- [ ] **Step 5: 全量 dotnet test 绿（1577+新增全过）+ 前端 369 绿 + Commit + push**——`git commit -m "test(cfg): E2E金线+错误码五语登记+运维须知+审计挂接(配置基建收口)" && git push`

---

## Self-Review 记录（写毕自查）

1. **Spec 覆盖**：§1①→T1/T2；§1②→T3；§1③→T4；§1④→T6/T7/T8（+拍板2/3→T9、ConditionJson/撤回→T10）；§1⑤→T5；§1⑥→T12；§2→T11；§3 接缝→各 Produces 块锁定签名待 Sales v2 消费；§5→各任务测试+T14；§4.1 双模认证显式范围外（独立 plan）。盘点拍板 #1~#13 全部有落点（#1→T8 per-action 测试；#2→T9 保存闸；#3→T9 PASSTHROUGH；#4→T7 F1 联动 Warning；#5→T7 DocTypeDisabled 语义注释+T14 变体测试；#6→T10 Withdraw；#7 变更单跨配置 Apply 属 Sales v2 plan（SalesOrderChange 实体不在本计划）——已在 spec 7.6 记录，Sales v2 plan 必须实现。
2. **占位符扫描**：无 TBD/TODO；三处「先定位/先读」是明确的发现步骤（FlowDef 保存点、SubmitAsync 签名、FlowDef SchemaJson 节点名），各自附了定位命令。
3. **类型一致性**：`IFeatureGate`/`INumberingService`/`ITerminologyResolver`/`DocFlowRegistry`/`IDocFlowEngine`/`IPackRouter`/`IConfigBundleService` 签名在 Produces 与后续 Consumes 引用处逐一核对一致；错误码不重号（E-CONF-001,010,015~017,020~027,030~031；E-WF-101~102,110~111）。
