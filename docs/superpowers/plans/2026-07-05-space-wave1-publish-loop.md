# Space 波1：发布闭环真做（T_WmsBin + 真消费端 + 停用同步 RPC + 三隐患修复）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 Space 3D 的库位发布闭环从 NoOp 假闭环做成真闭环——发布落地到 WMS 侧新表 `T_WmsBin`（幂等 upsert），停用改为同步 RPC 决策模型，并修复发布非原子 / 重试重复落事件两个隐患 + 2026-07-05 设计评审确认的 5 个缺陷（H1 场景保存状态机后门 / H5 zoneId 假参数 / H6 停用乱序孤儿 Bin / H7 库存校验无仓维度 / H8 并发冲突 500）。评审遗留的 H2/H3/H4（编辑动作↔发布联动）另立波 1.5 计划，本波不做。

**Architecture:** 方案 A（用户 2026-07-05 拍板）：新建 `T_WmsBin` 独立消费表（契约 docs/space/04-publish-contract.md v1.1 §5.3），与 `T_Stock` 靠 `(WarehouseCd, LocationCode)` 逻辑关联、无物理 FK。Space→WMS 依赖方向恒为抽象接口（`IWmsLocationConsumer` / `IWmsBinDeactivator` 定义在 Integration 侧，WMS 实现），唯一 DI 换绑点在 `Program.cs`。发布保持异步事件 + 重试 Worker；停用改同步 RPC 即时确认 + 异步事件兜底（契约 §6 v1.1）。

**Tech Stack:** .NET 8 + EF Core（SQL Server 生产 / InMemory 单测）+ xUnit。既有集成基建 `T_IntegrationEvent` + `BridgeHookBase` + `IntegrationEventDispatcher` + `IntegrationEventRetryWorker` 全部复用，不新造机制。

## Global Constraints

- 契约文档是硬约束：`docs/space/04-publish-contract.md`（v1.1）§3.4 / §5.1–5.3 / §6；字段名、幂等算法、逐项结果取值 `Success|Skipped|Rejected`（代码里对应 `UPSERTED/DEACTIVATED/SKIPPED/REJECTED`）照抄，不得自创。
- EF 迁移**必须**用 `dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations` 生成，**禁止手写迁移文件**（否则 `CP6ContextModelSnapshot.cs` 不同步）。生成后确认新迁移带 `.Designer.cs`。
- 事务用项目既有守卫惯例（见 `CP6.Core\Services\Space\SceneService.cs:28`）：`IDbContextTransaction? tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;`——InMemory 单测下自动降级无事务。
- 错误消息沿用现有模式：`throw new InvalidOperationException("E-SPACE-xxx: 中文描述")`（错误码整段换 BizException 属波4，本波不做）。
- 单测用 InMemory（`new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options`），照抄 `CP6.Tests\LocationPublishServiceTests.cs:18` 的 `Db()` 帮手。
- 测试命令：`dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~<类名>"`；全量回归 `dotnet test CP6.Tests/CP6.Tests.csproj`。
- 提交信息格式：`feat(space): ...` / `fix(space): ...`，末尾带 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`。
- 每个 Task 一个 commit，commit 前该 Task 的测试必须绿。
- 多租户铁律（`LocationPublishService.cs` 头注释）：查询不写 `.Where(TenantId)`（全局过滤自动隔离）；创建实体不写 TenantId（SaveChanges 盖章）；唯一例外是 DTO 字段 `LocationPublishBatch.TenantId` 须显式赋值。

---

### Task 1: T_WmsBin 实体 + Space_Site.WarehouseCd 映射列 + EF 迁移

**Files:**
- Create: `CP6.Entity/DomainModels/Wms/WmsBin.cs`
- Modify: `CP6.Entity/DomainModels/Space/Space_Site.cs`（末尾加 1 个属性）
- Modify: `CP6.Core/EFDbContext/CP6Context.cs:429` 后（DbSet）、`:2043` 后（索引配置，`Space_Marker` 索引块之后）
- Create（生成）: `CP6.Core/Migrations/*_SpaceWave1WmsBin.cs`（dotnet ef 生成，勿手写）

**Interfaces:**
- Consumes: `BaseBizEntity`（Id Guid 主键 / TenantId / IsDeleted / RowVersion / Creator / CreateDate）
- Produces: 实体 `CP6.Entity.DomainModels.Wms.WmsBin`（属性：`string LocationCode`、`string WarehouseCd`、`long Version`、`string PathJson`、`string AttrsJson`、`bool IsActive`、`DateTime? LastPublishedAt`、`string? LastPublishedBy`）；`CP6Context.WmsBins` DbSet；`Space_Site.WarehouseCd`（`string?`，空=默认映射 WarehouseCd=SiteCode）。后续 Task 2/3/4 全部依赖。

- [ ] **Step 1: 创建 WmsBin 实体**

新建 `CP6.Entity/DomainModels/Wms/WmsBin.cs`：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// WMS 侧库位消费表（Space ch04 §5.3 v1.1）。接收 Space LocationPublished 发布的库位目录，
/// 是发布的物理落点与幂等判据 lastVersion 的存放处。
/// Id = Space LocationId（稳定主键，跨系统同一身份，由发布方给定、非自动生成）。
/// 与 T_Stock 靠 (WarehouseCd, LocationCode) 逻辑关联，不加物理 FK（库位目录与库存事务解耦演进）。
/// </summary>
[Table("T_WmsBin")]
public class WmsBin : BaseBizEntity
{
    /// <summary>冻结的 join key（发布后不变，ch04 §3.1）</summary>
    [Required, MaxLength(100)]
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>仓库维度（SiteCode↔WarehouseCd 映射得来，ch04 §3.4，多仓防串仓）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>已消费的最新发布版本（= lastVersion，幂等判据，ch04 §3.3）</summary>
    public long Version { get; set; }

    /// <summary>变长层级路径 JSON（区/巷/架…，不含坐标几何，ch04 §3.2）</summary>
    public string PathJson { get; set; } = "{}";

    /// <summary>业务属性 JSON（格口尺寸等，ch04 §3.1）</summary>
    public string AttrsJson { get; set; } = "{}";

    /// <summary>是否启用（DEACTIVATE 置 false，ch04 §6）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>最近一次成功消费时间</summary>
    public DateTime? LastPublishedAt { get; set; }

    /// <summary>最近一次发布人（payload publishedBy，溯源，ch04 §2.1）</summary>
    [MaxLength(100)]
    public string? LastPublishedBy { get; set; }
}
```

- [ ] **Step 2: Space_Site 加 WarehouseCd 映射列**

`CP6.Entity/DomainModels/Space/Space_Site.cs`，在 `public bool Enable { get; set; } = true;` 之后加：

```csharp
    /// <summary>WMS 仓库编码映射（ch04 §3.4 SiteCode↔WarehouseCd；空=默认规则 WarehouseCd=SiteCode）</summary>
    [MaxLength(10)]
    public string? WarehouseCd { get; set; }
```

- [ ] **Step 3: CP6Context 注册 DbSet + 索引**

`CP6.Core/EFDbContext/CP6Context.cs`，在 `public DbSet<Space_ConnectorStop> Space_ConnectorStops { get; set; }`（:429）之后加：

```csharp
    // ───── Space ch04 v1.1 §5.3 发布落点（WMS 侧消费表，方案A 2026-07-05 拍板）─────
    /// <summary>WMS 库位消费表（Space 发布落点，幂等判据 lastVersion 存放处）</summary>
    public DbSet<CP6.Entity.DomainModels.Wms.WmsBin> WmsBins { get; set; }
```

在 `modelBuilder.Entity<Space_Marker>()...`（:2042-2043）索引块之后加：

```csharp
        modelBuilder.Entity<CP6.Entity.DomainModels.Wms.WmsBin>(e =>
        {
            // Id = Space LocationId，由发布方给定，禁止自动生成（ch04 §5.3）
            e.Property(x => x.Id).ValueGeneratedNever();
            // join 锚：同租户同仓内 code 唯一（ch04 §3.4）
            e.HasIndex(x => new { x.TenantId, x.WarehouseCd, x.LocationCode }).IsUnique();
            e.Property(x => x.PathJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.AttrsJson).HasColumnType("nvarchar(max)");
        });
```

- [ ] **Step 4: 生成 EF 迁移（禁止手写）**

Run: `dotnet ef migrations add SpaceWave1WmsBin --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations`
Expected: 生成 `CP6.Core/Migrations/<timestamp>_SpaceWave1WmsBin.cs` + 同名 `.Designer.cs`，内容含 `CreateTable("T_WmsBin", ...)` 与 `AddColumn<string>("WarehouseCd", "Space_Site", ...)`。若无 `.Designer.cs` → 删掉重跑（勿手补）。

- [ ] **Step 5: 编译验证**

Run: `dotnet build CP6.sln`
Expected: Build succeeded, 0 errors。

- [ ] **Step 6: Commit**

```bash
git add CP6.Entity/DomainModels/Wms/WmsBin.cs CP6.Entity/DomainModels/Space/Space_Site.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/
git commit -m "feat(space): T_WmsBin 消费表 + Space_Site.WarehouseCd 映射列（ch04 v1.1 §5.3/§3.4，方案A）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: 发布载荷补 WarehouseCd（DTO + BuildItemAsync 映射）

**Files:**
- Modify: `CP6.Entity/DTOs/Space/LocationPublishBatch.cs`（`LocationPublishItem` 加 1 字段）
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs:158-209`（`BuildItemAsync` + 新增私有帮手）
- Test: `CP6.Tests/LocationPublishServiceTests.cs`（新增 2 个测试）

**Interfaces:**
- Consumes: Task 1 的 `Space_Site.WarehouseCd`
- Produces: `LocationPublishItem.WarehouseCd`（`string?`）；`LocationPublishService` 私有方法 `Task<string?> ResolveWarehouseCdAsync(Space_Location l)`（Task 4 复用）。Task 3 的消费端依赖 `item.WarehouseCd`。

- [ ] **Step 1: 写失败测试**

在 `CP6.Tests/LocationPublishServiceTests.cs` 的 `// ── D-4: 停用 ──` 注释之前加（复用文件既有的 seed 写法）：

```csharp
    // ── v1.1 §3.4: SiteCode↔WarehouseCd 映射 ─────────────────────────────

    private static (Guid floorId, Guid rackId) SeedHierarchy(CP6Context db, string? siteWarehouseCd)
    {
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1", WarehouseCd = siteWarehouseCd };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        return (floorId, rackId);
    }

    [Fact]
    public async Task Publish_SiteWithoutMapping_ItemWarehouseCd_DefaultsToSiteCode()
    {
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: null);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        // 默认规则：WarehouseCd = SiteCode（ch04 §3.4）
        Assert.Equal("WH1", payload.GetProperty("Items")[0].GetProperty("WarehouseCd").GetString());
    }

    [Fact]
    public async Task Publish_SiteWithMapping_ItemWarehouseCd_UsesMappedValue()
    {
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: "W9");
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        Assert.Equal("W9", payload.GetProperty("Items")[0].GetProperty("WarehouseCd").GetString());
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests.Publish_SiteWith"`
Expected: FAIL——编译错（`WarehouseCd` 不是 `Space_Site`… 已在 Task 1 加过，则败在 `GetProperty("WarehouseCd")` KeyNotFound）。

- [ ] **Step 3: DTO 加字段**

`CP6.Entity/DTOs/Space/LocationPublishBatch.cs`，`LocationPublishItem` 类中 `public long Version { get; set; }` 之后加：

```csharp
    /// <summary>仓库编码（v1.1 §3.4：发布 hook 投递前按 SiteCode↔WarehouseCd 映射填好；(WarehouseCd, LocationCode) 是跨系统 join 锚）</summary>
    public string? WarehouseCd { get; set; }
```

- [ ] **Step 4: BuildItemAsync 填映射 + 抽 ResolveWarehouseCdAsync 帮手**

`CP6.Core/Services/Space/LocationPublishService.cs`：

① `BuildItemAsync` 末尾的 `return new LocationPublishItem { ... }` 增加一行 `WarehouseCd`：

```csharp
        return new LocationPublishItem
        {
            Op = op,
            LocationId = l.Id,
            LocationCode = l.LocationCode ?? "",
            CodeOrigin = l.CodeOrigin,
            Version = l.Version,
            WarehouseCd = await ResolveWarehouseCdAsync(l),
            Path = path,
            Attrs = attrs
        };
```

② 类末尾（`BuildItemAsync` 方法之后）新增私有帮手：

```csharp
    /// <summary>
    /// SiteCode↔WarehouseCd 映射（ch04 §3.4）：Site.WarehouseCd 显式配置优先，空则默认 = SiteCode。
    /// 走 FloorId → Site 链（比 Rack 链短，且停用未落位库位也可能有 FloorId）；无楼层归属返回 null。
    /// </summary>
    private async Task<string?> ResolveWarehouseCdAsync(Space_Location l)
    {
        if (l.FloorId == null) return null;
        var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == l.FloorId);
        if (floor == null) return null;
        var site = await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floor.SiteId);
        if (site == null) return null;
        return string.IsNullOrEmpty(site.WarehouseCd) ? site.SiteCode : site.WarehouseCd;
    }
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests"`
Expected: 全 PASS（新 2 个 + 既有 8 个）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Entity/DTOs/Space/LocationPublishBatch.cs CP6.Core/Services/Space/LocationPublishService.cs CP6.Tests/LocationPublishServiceTests.cs
git commit -m "feat(space): 发布载荷补 WarehouseCd（ch04 v1.1 §3.4 SiteCode↔WarehouseCd 映射，默认=SiteCode）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: WmsBinConsumer 真消费端（幂等 upsert）+ DI 换绑

**Files:**
- Create: `CP6.Core/Services/Wms/WmsBinConsumer.cs`
- Modify: `CP6.WebApi/Program.cs:400`（DI 换绑）
- Test: Create `CP6.Tests/WmsBinConsumerTests.cs`

**Interfaces:**
- Consumes: `IWmsLocationConsumer.ConsumeAsync(LocationPublishBatch)`（`CP6.Core/Services/Integration/IWmsLocationConsumer.cs:12`，签名不变）；Task 1 `WmsBin`/`CP6Context.WmsBins`；Task 2 `item.WarehouseCd`；`CP6Context.Stocks`（`Stock.WarehouseCd/LocationCd/PhysicalQty`）
- Produces: `CP6.Core.Services.Wms.WmsBinConsumer : IWmsLocationConsumer`。逐项 `WmsItemResult.Status` 取值：`UPSERTED / DEACTIVATED / SKIPPED / REJECTED`；任一 `REJECTED` → `WmsConsumeResult.Success=false`（→ 事件 Failed，Worker 重试）。`NoOpWmsLocationConsumer` **保留不删**（测试与回滚开关用）。

- [ ] **Step 1: 写失败测试**

新建 `CP6.Tests/WmsBinConsumerTests.cs`：

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// WmsBinConsumer 测试（ch04 §5.1/§5.2/§5.3 幂等 upsert + 逐项结果）。[InMemory 仅测逻辑]
/// </summary>
public class WmsBinConsumerTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LocationPublishBatch Batch(params LocationPublishItem[] items) => new()
    {
        BatchNo = "LPUB-20260705-0001",
        PublishedBy = "u",
        Items = items.ToList()
    };

    private static LocationPublishItem Upsert(Guid id, string code, long version, string? warehouseCd = "WH1") => new()
    {
        Op = "UPSERT", LocationId = id, LocationCode = code, CodeOrigin = 1,
        Version = version, WarehouseCd = warehouseCd,
        Path = new LocationPath { SiteCode = "WH1", FloorLevel = 1, ZoneCode = "A", RackCode = "R1", Col = 1, Level = 1, Depth = 1 }
    };

    [Fact]
    public async Task Upsert_NewLocation_CreatesBin()
    {
        using var db = Db();
        var id = Guid.NewGuid();

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 1)));

        Assert.True(r.Success);
        Assert.Equal("UPSERTED", r.Items.Single().Status);
        var bin = await db.WmsBins.SingleAsync();
        Assert.Equal(id, bin.Id);
        Assert.Equal("A-01-01-01", bin.LocationCode);
        Assert.Equal("WH1", bin.WarehouseCd);
        Assert.Equal(1, bin.Version);
        Assert.True(bin.IsActive);
        Assert.Equal("u", bin.LastPublishedBy);
        Assert.Contains("\"ZoneCode\":\"A\"", bin.PathJson);
    }

    [Fact]
    public async Task Upsert_StaleVersion_Skipped_NoWrite()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        var c = new WmsBinConsumer(db);
        await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 5)));

        // 重复投递同版本（至少一次投递语义）→ 幂等跳过
        var r = await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 5)));

        Assert.True(r.Success);                       // 纯 Skipped 也算成功收敛（§5.2）
        Assert.Equal("SKIPPED", r.Items.Single().Status);
        Assert.True(r.AllSkipped);
        Assert.Equal(5, (await db.WmsBins.SingleAsync()).Version);
    }

    [Fact]
    public async Task Upsert_NewerVersion_UpdatesBin()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        var c = new WmsBinConsumer(db);
        await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 1)));

        var item = Upsert(id, "A-01-01-01", 2);
        item.Attrs["sizeW"] = 1200;
        var r = await c.ConsumeAsync(Batch(item));

        Assert.Equal("UPSERTED", r.Items.Single().Status);
        var bin = await db.WmsBins.SingleAsync();
        Assert.Equal(2, bin.Version);
        Assert.Contains("1200", bin.AttrsJson);
    }

    [Fact]
    public async Task Upsert_MissingWarehouseCd_Rejected_EventShouldFail()
    {
        using var db = Db();
        var r = await new WmsBinConsumer(db).ConsumeAsync(
            Batch(Upsert(Guid.NewGuid(), "A-01-01-01", 1, warehouseCd: null)));

        Assert.False(r.Success);                      // 任一 Rejected → 整事件 Failed（§5.2）
        Assert.Equal("REJECTED", r.Items.Single().Status);
        Assert.Equal(0, await db.WmsBins.CountAsync());
    }

    [Fact]
    public async Task Deactivate_NoBin_CreatesTombstone()
    {
        // H6 乱序防护（对契约 §5.1 的修正）：bin 不存在的 DEACTIVATE 落墓碑而非跳过，
        // 否则迟到重试的旧版 UPSERT 会复活已停用库位。
        using var db = Db();
        var id = Guid.NewGuid();
        var item = Upsert(id, "A-01-01-01", 2);
        item.Op = "DEACTIVATE";

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(item));

        Assert.True(r.Success);
        Assert.Equal("DEACTIVATED", r.Items.Single().Status);
        var tomb = await db.WmsBins.SingleAsync();
        Assert.False(tomb.IsActive);
        Assert.Equal(2, tomb.Version);
    }

    [Fact]
    public async Task Deactivate_NoBin_NoWarehouseCd_Skipped()
    {
        // 无仓维度建不了 (WarehouseCd, LocationCode) join 锚 → 退回幂等跳过（如采纳态无楼层归属）
        using var db = Db();
        var item = Upsert(Guid.NewGuid(), "A-01-01-01", 2, warehouseCd: null);
        item.Op = "DEACTIVATE";

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(item));

        Assert.True(r.Success);
        Assert.Equal("SKIPPED", r.Items.Single().Status);
        Assert.Equal(0, await db.WmsBins.CountAsync());
    }

    [Fact]
    public async Task Deactivate_Tombstone_ThenLateUpsert_Skipped()
    {
        // H6 全链路：墓碑(v2) 落库后，重试队列里的旧版 UPSERT(v1) 到达 → 版本单调掐死，不复活
        using var db = Db();
        var id = Guid.NewGuid();
        var c = new WmsBinConsumer(db);
        var deact = Upsert(id, "A-01-01-01", 2);
        deact.Op = "DEACTIVATE";
        await c.ConsumeAsync(Batch(deact));

        var r = await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 1)));   // 迟到的 v1 UPSERT

        Assert.Equal("SKIPPED", r.Items.Single().Status);
        var bin = await db.WmsBins.SingleAsync();
        Assert.False(bin.IsActive);                          // 仍是停用态
        Assert.Equal(2, bin.Version);
    }

    [Fact]
    public async Task Deactivate_WithStock_Rejected()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        var c = new WmsBinConsumer(db);
        await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 1)));
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(), WarehouseCd = "WH1", LocationCd = "A-01-01-01",
            ProductCd = "P1", LotNo = "", PhysicalQty = 5m
        });
        await db.SaveChangesAsync();

        var item = Upsert(id, "A-01-01-01", 2);
        item.Op = "DEACTIVATE";
        var r = await c.ConsumeAsync(Batch(item));

        Assert.False(r.Success);
        Assert.Equal("REJECTED", r.Items.Single().Status);
        Assert.True((await db.WmsBins.SingleAsync()).IsActive);   // 未停用
    }

    [Fact]
    public async Task Deactivate_NoStock_SetsInactive_AndVersion()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        var c = new WmsBinConsumer(db);
        await c.ConsumeAsync(Batch(Upsert(id, "A-01-01-01", 1)));

        var item = Upsert(id, "A-01-01-01", 2);
        item.Op = "DEACTIVATE";
        var r = await c.ConsumeAsync(Batch(item));

        Assert.True(r.Success);
        Assert.Equal("DEACTIVATED", r.Items.Single().Status);
        var bin = await db.WmsBins.SingleAsync();
        Assert.False(bin.IsActive);
        Assert.Equal(2, bin.Version);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~WmsBinConsumerTests"`
Expected: FAIL——编译错 `WmsBinConsumer` 不存在。

- [ ] **Step 3: 实现 WmsBinConsumer**

新建 `CP6.Core/Services/Wms/WmsBinConsumer.cs`：

```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS 库位消费端真实现（ch04 §5.1/§5.3 v1.1）：LocationPublished → T_WmsBin 幂等 upsert。
/// 幂等判据：按 Id(=Space LocationId) 取行，incoming.Version &lt;= stored.Version → SKIPPED。
/// 整批语义（§5.2）：任一 REJECTED → Success=false（整事件 Failed，Worker 重试/人工介入）；
/// 其余项照常落库（部分失败返回逐项结果）。TenantId 由 SaveChanges 自动盖章。
/// </summary>
public class WmsBinConsumer : IWmsLocationConsumer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly CP6Context _db;

    public WmsBinConsumer(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<WmsConsumeResult> ConsumeAsync(LocationPublishBatch batch)
    {
        var result = new WmsConsumeResult { Success = true };
        foreach (var item in batch.Items)
        {
            var bin = await _db.WmsBins.FirstOrDefaultAsync(b => b.Id == item.LocationId);

            // 陈旧/重复事件 → 幂等跳过（§5.1 关键行，至少一次投递安全）
            if (bin != null && item.Version <= bin.Version)
            {
                result.Items.Add(Item(item, "SKIPPED", "version<=lastVersion（幂等重复）"));
                continue;
            }

            if (item.Op == "DEACTIVATE")
            {
                if (bin == null)
                {
                    // H6 乱序防护（对契约 §5.1 的修正）：对应 UPSERT 事件可能仍在重试队列，
                    // 直接跳过会让迟到的旧版 UPSERT 复活已停用库位 → 落墓碑行（IsActive=false + Version 占位），
                    // 版本单调判据自动掐死后到的旧版。无仓维度（建不了 join 锚）才退回幂等跳过。
                    if (string.IsNullOrEmpty(item.WarehouseCd))
                    {
                        result.Items.Add(Item(item, "SKIPPED", "bin 不存在且缺 WarehouseCd，幂等无操作"));
                        continue;
                    }
                    var tomb = new WmsBin
                    {
                        Id = item.LocationId,
                        LocationCode = item.LocationCode,
                        WarehouseCd = item.WarehouseCd,
                        PathJson = JsonSerializer.Serialize(item.Path, Json),
                        AttrsJson = JsonSerializer.Serialize(item.Attrs, Json),
                        IsActive = false
                    };
                    Stamp(tomb, item.Version, batch.PublishedBy);
                    _db.WmsBins.Add(tomb);
                    result.Items.Add(Item(item, "DEACTIVATED", "墓碑落库（bin 未曾消费，防乱序复活）"));
                    continue;
                }
                // TOCTOU 权威校验：库存真相在 WMS（§6），按 (WarehouseCd, LocationCode) 锚查
                var qty = await _db.Stocks
                    .Where(s => s.WarehouseCd == bin.WarehouseCd && s.LocationCd == bin.LocationCode)
                    .SumAsync(s => s.PhysicalQty);
                if (qty > 0)
                {
                    result.Items.Add(Item(item, "REJECTED", "W-SPACE-404 库存非0"));
                    result.Success = false;
                    continue;
                }
                bin.IsActive = false;
                Stamp(bin, item.Version, batch.PublishedBy);
                result.Items.Add(Item(item, "DEACTIVATED", null));
                continue;
            }

            // UPSERT
            if (string.IsNullOrEmpty(item.WarehouseCd))
            {
                // 无仓维度无法建 (WarehouseCd, LocationCode) join 锚（§3.4）→ 拒绝该条
                result.Items.Add(Item(item, "REJECTED", "缺 WarehouseCd（SiteCode↔WarehouseCd 映射未命中）"));
                result.Success = false;
                continue;
            }
            if (bin == null)
            {
                bin = new WmsBin { Id = item.LocationId };
                _db.WmsBins.Add(bin);
            }
            bin.LocationCode = item.LocationCode;   // 理论不变（发布后码冻结）
            bin.WarehouseCd = item.WarehouseCd;
            bin.PathJson = JsonSerializer.Serialize(item.Path, Json);
            bin.AttrsJson = JsonSerializer.Serialize(item.Attrs, Json);
            bin.IsActive = true;
            Stamp(bin, item.Version, batch.PublishedBy);
            result.Items.Add(Item(item, "UPSERTED", null));
        }

        await _db.SaveChangesAsync();
        result.AllSkipped = result.Items.Count > 0 && result.Items.All(i => i.Status == "SKIPPED");
        return result;
    }

    private static void Stamp(WmsBin bin, long version, string? publishedBy)
    {
        bin.Version = version;
        bin.LastPublishedAt = DateTime.Now;
        bin.LastPublishedBy = publishedBy;
    }

    private static WmsItemResult Item(LocationPublishItem i, string status, string? reason) =>
        new() { LocationId = i.LocationId, Status = status, Reason = reason };
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~WmsBinConsumerTests"`
Expected: 9 个全 PASS。

- [ ] **Step 5: DI 换绑（发布闭环接通点）**

`CP6.WebApi/Program.cs:400`，把：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsLocationConsumer, CP6.Core.Services.Integration.NoOpWmsLocationConsumer>();
```

改为：

```csharp
// ch04 v1.1 §5.3：真消费端（T_WmsBin 幂等 upsert，方案A 2026-07-05）。回滚开关：换回 NoOpWmsLocationConsumer 即断开。
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsLocationConsumer, CP6.Core.Services.Wms.WmsBinConsumer>();
```

- [ ] **Step 6: 全量回归 + Commit**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS（既有 LocationPublishServiceTests 用的是测试内自建 NoOp/hook，不受 DI 换绑影响）。

```bash
git add CP6.Core/Services/Wms/WmsBinConsumer.cs CP6.WebApi/Program.cs CP6.Tests/WmsBinConsumerTests.cs
git commit -m "feat(space): WmsBinConsumer 真消费端替换 NoOp——发布闭环接通（ch04 §5 幂等 upsert）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: IWmsBinDeactivator 停用同步 RPC + DeactivateAsync 决策模型重构

**Files:**
- Create: `CP6.Core/Services/Integration/IWmsBinDeactivator.cs`
- Create: `CP6.Core/Services/Wms/WmsBinDeactivator.cs`
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`（构造函数 + `DeactivateAsync` 重写）
- Modify: `CP6.WebApi/Program.cs`（DI 注册，加在 :400 换绑行之后）
- Test: `CP6.Tests/LocationPublishServiceTests.cs`（`MakePublishSvc` 改造 + 新增 2 个测试）

**Interfaces:**
- Consumes: Task 1 `WmsBin`；Task 2 `ResolveWarehouseCdAsync`
- Produces:
  ```csharp
  public interface IWmsBinDeactivator
  {
      Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default);
  }
  public sealed class WmsDeactivateRequest { Guid LocationId; string LocationCode; string? WarehouseCd; long Version; string? User; }
  public sealed class WmsDeactivateResult { bool Success; string? Reason; }
  ```
  `LocationPublishService` 构造函数变为 6 参：`(CP6Context, ITenantContext, ICodeEngineService, ISpaceBridgeHook, IWmsStockQuery, IWmsBinDeactivator)`——**所有调用方（DI 与测试）须同步**。停用语义变化：不再"乐观翻转+失败回滚"，改为"同步确认成功才翻转"（契约 §6.3：不存在回滚路径）。

- [ ] **Step 1: 定义契约接口**

新建 `CP6.Core/Services/Integration/IWmsBinDeactivator.cs`：

```csharp
namespace CP6.Core.Services.Integration;

/// <summary>
/// 停用同步 RPC 契约（ch04 §6 v1.1，Space 侧定义、WMS 实现，与 IWmsStockQuery 同构的单向抽象）。
/// Space 停用前同步调用，WMS 按实时库存权威判定（TOCTOU 防护）；
/// Space 据同步返回决定本地 Status——成功才 1→2，不再乐观翻转+回滚（§6.3 无孤儿库位）。
/// </summary>
public interface IWmsBinDeactivator
{
    Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default);
}

/// <summary>停用同步请求（对应契约 POST /api/wms/bins/deactivate 的进程内形态）。</summary>
public sealed class WmsDeactivateRequest
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    /// <summary>仓库维度（§3.4 映射；bin 已落库时以 bin 记录为准）</summary>
    public string? WarehouseCd { get; set; }
    /// <summary>停用后的新版本号（= Space 侧 Version+1），成功时写入 T_WmsBin.Version</summary>
    public long Version { get; set; }
    public string? User { get; set; }
}

/// <summary>停用同步返回。</summary>
public sealed class WmsDeactivateResult
{
    public bool Success { get; set; }
    /// <summary>拒绝原因（如 W-SPACE-404 库存非0）</summary>
    public string? Reason { get; set; }
}
```

- [ ] **Step 2: 实现 WMS 侧真停用器**

新建 `CP6.Core/Services/Wms/WmsBinDeactivator.cs`：

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 停用同步 RPC 真实现（ch04 §6.1② v1.1）：再查一次实时库存做 TOCTOU 权威校验，
/// 无库存 → T_WmsBin.IsActive=false + Version 落定；有库存 → 拒绝 W-SPACE-404。
/// bin 尚未消费落库（如 UPSERT 事件还在重试）→ 无库存即放行，异步兜底事件会幂等收敛（§6.1④）。
/// </summary>
public class WmsBinDeactivator : IWmsBinDeactivator
{
    private readonly CP6Context _db;

    public WmsBinDeactivator(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default)
    {
        var bin = await _db.WmsBins.FirstOrDefaultAsync(b => b.Id == req.LocationId, ct);

        // 权威库存判定：优先按 bin 落库的 (WarehouseCd, LocationCode) 锚；bin 未落库退回请求携带的键
        var warehouseCd = bin?.WarehouseCd ?? req.WarehouseCd;
        var code = bin?.LocationCode ?? req.LocationCode;
        var stocks = _db.Stocks.Where(s => s.LocationCd == code);
        if (!string.IsNullOrEmpty(warehouseCd))
            stocks = stocks.Where(s => s.WarehouseCd == warehouseCd);
        var qty = await stocks.SumAsync(s => s.PhysicalQty, ct);
        if (qty > 0)
            return new WmsDeactivateResult { Success = false, Reason = "W-SPACE-404 库存非0" };

        if (bin != null)
        {
            bin.IsActive = false;
            bin.Version = req.Version;
            bin.LastPublishedAt = DateTime.Now;
            bin.LastPublishedBy = req.User;
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrEmpty(req.WarehouseCd))
        {
            // H6 乱序防护墓碑：同步 RPC 是权威停用时点。bin 未曾消费（UPSERT 事件可能仍在重试）
            // 也要占住 (Id, Version)，防止迟到的旧版 UPSERT 事后复活该库位。
            _db.WmsBins.Add(new WmsBin
            {
                Id = req.LocationId,
                LocationCode = req.LocationCode,
                WarehouseCd = req.WarehouseCd,
                IsActive = false,
                Version = req.Version,
                LastPublishedAt = DateTime.Now,
                LastPublishedBy = req.User
            });
            await _db.SaveChangesAsync(ct);
        }
        // bin 不存在且无仓维度（如采纳态无楼层归属）→ 无库存即放行，异步兜底事件幂等收敛
        return new WmsDeactivateResult { Success = true };
    }
}
```

- [ ] **Step 3: 更新测试帮手 + 写失败测试**

`CP6.Tests/LocationPublishServiceTests.cs`：

① `MakePublishSvc` 改为（新增 `deact` 参数 + 6 参构造）：

```csharp
    private static LocationPublishService MakePublishSvc(
        CP6Context db,
        IWmsStockQuery? stock = null,
        ISpaceBridgeHook? hook = null,
        IWmsBinDeactivator? deact = null)
    {
        var t = new TenantContext();
        var code = new CodeEngineService(db);
        hook ??= new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        stock ??= new StubWmsStockQuery();
        deact ??= new CP6.Core.Services.Wms.WmsBinDeactivator(db);
        return new LocationPublishService(db, t, code, hook, stock, deact);
    }
```

② 文件末尾 `FixedStockQuery` 类之后加拒绝桩 + 2 个新测试（放在 `// ── D-5` 注释之前）：

```csharp
    private sealed class RejectingDeactivator : IWmsBinDeactivator
    {
        public Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default)
            => Task.FromResult(new WmsDeactivateResult { Success = false, Reason = "W-SPACE-404 库存非0" });
    }

    [Fact]
    public async Task Deactivate_WmsRejects_StatusStays1_NoEvent()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakePublishSvc(db, deact: new RejectingDeactivator()).DeactivateAsync(locId, "u"));

        Assert.StartsWith("W-SPACE-404", ex.Message);
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);            // §6.3：不前进、无翻转回滚
        Assert.Equal(1, loc.Version);           // 版本不动
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 决策失败不发兜底事件
    }

    [Fact]
    public async Task Deactivate_Success_WmsBinInactive_VersionSynced()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        // 预置已消费的 bin（模拟此前 UPSERT 已落库）
        db.WmsBins.Add(new CP6.Entity.DomainModels.Wms.WmsBin
        {
            Id = locId, LocationCode = "A-01-01-01", WarehouseCd = "WH1", Version = 1, IsActive = true
        });
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).DeactivateAsync(locId, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Status);
        Assert.Equal(2, loc.Version);
        var bin = await db.WmsBins.SingleAsync();
        Assert.False(bin.IsActive);
        Assert.Equal(2, bin.Version);           // 同步 RPC 落定的新版本
        Assert.Equal(1, await db.IntegrationEvents.CountAsync());   // ④ 兜底事件已补发
    }

    [Fact]
    public async Task Deactivate_NoBinYet_SyncRpc_WritesTombstone()
    {
        // H6：UPSERT 事件尚未消费（bin 不存在）时停用 → 同步 RPC 落墓碑占住 (Id, Version)
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: null);   // WarehouseCd 默认=SiteCode "WH1"
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).DeactivateAsync(locId, "u");

        var tomb = await db.WmsBins.SingleAsync();
        Assert.False(tomb.IsActive);
        Assert.Equal(2, tomb.Version);
        Assert.Equal("WH1", tomb.WarehouseCd);
        Assert.Equal(2, (await db.Space_Locations.SingleAsync()).Status);
    }
```

- [ ] **Step 4: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests"`
Expected: FAIL——编译错（`LocationPublishService` 还是 5 参构造）。

- [ ] **Step 5: 重写 DeactivateAsync（同步决策模型）**

`CP6.Core/Services/Space/LocationPublishService.cs`：

① 构造函数注入 deactivator（字段 + 参数 + 赋值）：

```csharp
    private readonly CP6Context _db;
    private readonly ITenantContext _t;
    private readonly ICodeEngineService _code;
    private readonly ISpaceBridgeHook _hook;
    private readonly IWmsStockQuery _stock;
    private readonly IWmsBinDeactivator _deactivator;

    public LocationPublishService(
        CP6Context db,
        ITenantContext t,
        ICodeEngineService code,
        ISpaceBridgeHook hook,
        IWmsStockQuery stock,
        IWmsBinDeactivator deactivator)
    {
        _db = db;
        _t = t;
        _code = code;
        _hook = hook;
        _stock = stock;
        _deactivator = deactivator;
    }
```

② `DeactivateAsync` 整体替换为（ch04 §6.1 四步）：

```csharp
    /// <inheritdoc/>
    public async Task DeactivateAsync(Guid locationId, string? user)
    {
        var l = await _db.Space_Locations.FirstOrDefaultAsync(x => x.Id == locationId)
                ?? throw new InvalidOperationException("E-SPACE-004: 库位不存在");
        if (l.Status != 1)
            throw new InvalidOperationException("E-SPACE-004: 库位未处于已发布状态");

        // ① 前置校验（用户体验，连 RPC 都不发；ch04 §6.1①）
        var qty = await _stock.GetStockQtyAsync(l.LocationCode ?? "");
        if (qty > 0)
            throw new InvalidOperationException("E-SPACE-401: 库位仍有库存，无法停用");

        // ② 同步 RPC：WMS 按实时库存权威判定（TOCTOU 防护；ch04 §6.1② v1.1）
        var newVersion = l.Version + 1;
        var resp = await _deactivator.DeactivateAsync(new WmsDeactivateRequest
        {
            LocationId = l.Id,
            LocationCode = l.LocationCode ?? "",
            WarehouseCd = await ResolveWarehouseCdAsync(l),
            Version = newVersion,
            User = user
        });

        // ③ 据同步返回决定本地 Status——被拒不前进，无翻转回滚（ch04 §6.3）
        if (!resp.Success)
            throw new InvalidOperationException("W-SPACE-404: WMS 侧仍有库存，停用未生效");

        l.Status = 2;
        l.Version = newVersion;
        l.Modifier = user;
        l.ModifyDate = DateTime.Now;

        var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
        var batch = new LocationPublishBatch
        {
            BatchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}",
            TenantId = _t.CurrentTenantId,
            PublishedBy = user
        };
        batch.Items.Add(await BuildItemAsync(l, "DEACTIVATE"));
        await _db.SaveChangesAsync();

        // ④ 异步事件兜底（对账/审计/漂移纠正，不参与本地 Status 决策；ch04 §6.1④）
        await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
    }
```

（注意：`BuildItemAsync(l, "DEACTIVATE")` 在 `l.Version = newVersion` 赋值**之后**调用，兜底事件携带的即新版本——消费端 `Version <= stored` 幂等跳过已由同步 RPC 落定的同版本，正好收敛。）

③ `Program.cs` 在 Task 3 换绑行之后加注册：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsBinDeactivator, CP6.Core.Services.Wms.WmsBinDeactivator>();
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests"`
Expected: 全 PASS。注意既有 `Deactivate_StockZero_Success_EmitsDeactivateEvent`（无 bin、无库存 → 真 deactivator 放行）与 `Deactivate_StockPositive_Throws_E401`（①步拦截）语义不变，必须仍绿。

- [ ] **Step 7: 全量回归 + Commit**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS。

```bash
git add CP6.Core/Services/Integration/IWmsBinDeactivator.cs CP6.Core/Services/Wms/WmsBinDeactivator.cs CP6.Core/Services/Space/LocationPublishService.cs CP6.WebApi/Program.cs CP6.Tests/LocationPublishServiceTests.cs
git commit -m "feat(space): 停用改同步 RPC 决策模型（ch04 §6 v1.1）——消灭孤儿库位与乐观回滚

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 发布原子性事务 + 重试不重复落事件

**Files:**
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`（`PublishFloorAsync` 包事务）
- Modify: `CP6.Core/Services/Integration/SpaceBridgeHook.cs`（`persistEvent` 参数）
- Modify: `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs:68-73`（SPACE 路由传 `persistEvent: false`）
- Test: `CP6.Tests/SpaceBridgeHookTests.cs`（新增 1 个测试；若 MakeHook 类帮手签名不匹配则同步）

**Interfaces:**
- Consumes: Task 3 真消费端（事务包裹后 flip + T_WmsBin 写入 + 事件落库同库同事务）
- Produces: `ISpaceBridgeHook.OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId, bool persistEvent = true)`——带默认值，既有调用点（`LocationPublishService` 两处）无需改；仅 Dispatcher 重试路由显式传 `false`（重试成功/失败由 Worker 更新**原事件行**，不再新插行）。

- [ ] **Step 1: 写失败测试（重试不重复落事件）**

打开 `CP6.Tests/SpaceBridgeHookTests.cs`（已存在，含 `AllSkippedConsumer` 桩），照其现有构造方式新增：

```csharp
    [Fact]
    public async Task Hook_PersistEventFalse_DoesNotInsertEventRow()
    {
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hook = new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        var batch = new LocationPublishBatch { BatchNo = "LPUB-20260705-0001" };

        var r = await hook.OnLocationPublishedAsync(batch, Guid.NewGuid(), persistEvent: false);

        Assert.True(r.Success);
        // 重试路径（Dispatcher）走此分支：Worker 更新原事件行，hook 不得再新插一行
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }
```

（文件顶部 using 若缺 `CP6.Entity.DTOs.Space` / `Microsoft.Extensions.Logging.Abstractions` / `Microsoft.EntityFrameworkCore` 则补齐——先看现有 using，多数已在。）

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceBridgeHookTests"`
Expected: FAIL——编译错（`OnLocationPublishedAsync` 无 `persistEvent` 参数）。

- [ ] **Step 3: hook 加 persistEvent 参数**

`CP6.Core/Services/Integration/SpaceBridgeHook.cs`：

① 接口（同文件 :9-12）：

```csharp
public interface ISpaceBridgeHook
{
    /// <param name="persistEvent">true=末尾落 IntegrationEvent（首发路径）；false=不落（Worker 重试路径，
    /// 由 Worker 更新原事件行，避免每次重试新插一行导致事件表翻倍增长）。</param>
    Task<SpaceBridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId, bool persistEvent = true);
}
```

② 实现方法签名同步加参，`PersistEventAsync` 调用包进 if：

```csharp
    public async Task<SpaceBridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId, bool persistEvent = true)
    {
        // …… try/catch 调 _wms.ConsumeAsync 的现有逻辑保持原样 ……

        if (persistEvent)
        {
            await PersistEventAsync(
                sourceModule: "SPACE",
                targetModule: "WMS",
                hookName: nameof(OnLocationPublishedAsync),
                sourceNo: batch.BatchNo,
                targetNo: null,
                status: status,
                error: error,
                correlationId: correlationId,
                payload: batch);
        }

        return new SpaceBridgeResult
        {
            Success = ok && status != IntegrationEventStatus.Failed,
            Message = error
        };
    }
```

③ `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs:68-73` 的 SPACE 路由改为：

```csharp
        [RouteKey("SPACE", "WMS", "OnLocationPublishedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<LocationPublishBatch>();
            // 重试路径不重复落事件：Worker 负责更新原 IntegrationEvent 行的 Status/Attempts
            var r = await ctx.Space.OnLocationPublishedAsync(p, Guid.NewGuid(), persistEvent: false);
            return r.Success;
        },
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceBridgeHookTests|FullyQualifiedName~IntegrationEventDispatcherTests|FullyQualifiedName~IntegrationEventRetryWorkerTests"`
Expected: 全 PASS。

- [ ] **Step 5: PublishFloorAsync 包事务（发布原子性）**

`CP6.Core/Services/Space/LocationPublishService.cs`，文件顶部补 `using Microsoft.EntityFrameworkCore.Storage;`，`PublishFloorAsync` 整体替换为：

```csharp
    /// <inheritdoc/>
    public async Task<int> PublishFloorAsync(Guid floorId, Guid? zoneId, string? user)
    {
        // InMemory 安全事务守卫（惯例见 SceneService）：真库开事务，InMemory 降级无事务。
        // 事务范围＝闸门→翻状态→WMS 消费(T_WmsBin 写入)→事件落库，全部同库原子提交，
        // 修复"翻了状态但事件静默丢失"的窗口（同一 CP6Context 实例，hook 内 SaveChanges 同事务）。
        IDbContextTransaction? tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync()
            : null;
        try
        {
            // 1. 闸门（ch03 §9.2）
            var pre = await _code.PrecheckAsync(floorId);
            if (pre.EmptyCodeCount > 0 || pre.DuplicateGroups.Count > 0 || pre.PrecheckErrors.Count > 0)
                throw new InvalidOperationException("E-SPACE-307: 楼层存在空码、重码或其他预检错误，无法发布");

            // 2. 取 Status=0 且编码就绪的库位
            var locs = await _db.Space_Locations
                .Where(l => l.FloorId == floorId && l.Status == 0 && l.LocationCode != null)
                .ToListAsync();

            if (locs.Count == 0) return 0;

            // 3. 批号（D-E）
            var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
            var batchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}";

            // 4. 翻状态 + 升版 + 组载荷
            var batch = new LocationPublishBatch
            {
                BatchNo = batchNo,
                TenantId = _t.CurrentTenantId,  // DTO 字段，不被 EF 盖章，必须显式赋值
                PublishedBy = user
            };
            foreach (var l in locs)
            {
                l.Status = 1;
                l.Version += 1;
                l.Modifier = user;
                l.ModifyDate = DateTime.Now;
                batch.Items.Add(await BuildItemAsync(l, "UPSERT"));
            }
            await _db.SaveChangesAsync();

            // 5. 发事件（hook 内部吞消费异常→Failed 事件落库，由 Worker 重试；不影响本事务提交）
            await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());

            if (tx != null) await tx.CommitAsync();
            return locs.Count;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();   // 未 Commit 即 Dispose = 回滚
        }
    }
```

- [ ] **Step 6: 全量回归确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS（InMemory 下 `IsRelational()==false`，事务守卫自动降级，既有测试不受影响）。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Space/LocationPublishService.cs CP6.Core/Services/Integration/SpaceBridgeHook.cs CP6.Core/Services/Integration/IntegrationEventDispatcher.cs CP6.Tests/SpaceBridgeHookTests.cs
git commit -m "fix(space): 发布包事务原子提交 + 重试路径不重复落 IntegrationEvent

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: 按库区发布真实现（H5：zoneId 假参数修复 + 闸门同步收窄）

**Files:**
- Modify: `CP6.Core/Services/Space/ICodeEngineService.cs`（`PrecheckAsync` 签名加可选 zoneId）
- Modify: `CP6.Core/Services/Space/CodeEngineService.cs:282-325`（`PrecheckAsync` 库区过滤）
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`（`PublishFloorAsync` 库区过滤，在 Task 5 的事务版本之上改）
- Modify: `CP6.WebApi/Controllers/Space/CodeRuleController.cs`（`GET floor/{id}/code-precheck` 端点加 `?zoneId=` 透传，保持现有包壳写法）
- Test: `CP6.Tests/LocationPublishServiceTests.cs`（新增 1 个测试）

**Interfaces:**
- Consumes: Task 5 版 `PublishFloorAsync`（事务包裹）
- Produces: `ICodeEngineService.PrecheckAsync(Guid floorId, Guid? zoneId = null)`——默认参数，既有调用点（CodeRuleController、LocationPublishService）编译不破坏。库区归属经 `Rack.ZoneId` 推导（`Space_Location` 无 ZoneId 冗余列）。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/LocationPublishServiceTests.cs`，加在 `// ── D-4: 停用 ──` 注释之前：

```csharp
    [Fact]
    public async Task Publish_WithZoneId_OnlyPublishesThatZone_GateZoneScoped()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zoneA = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZA", ZoneName = "A" };
        var zoneB = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZB", ZoneName = "B" };
        var rackA = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zoneA.Id, FloorId = floorId, RackCode = "RA", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        var rackB = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zoneB.Id, FloorId = floorId, RackCode = "RB", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule { Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true, Segments = ValidSegmentsJson() });
        db.AddRange(site, floor, zoneA, zoneB, rackA, rackB);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rackA.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "ZA-01", Col = 1, Level = 1, Depth = 1 });
        // Zone B 留一个空码草稿——整层闸门会拦（E-307），库区闸门必须放行 Zone A
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rackB.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = null, Col = 1, Level = 1, Depth = 1 });
        await db.SaveChangesAsync();

        var n = await MakePublishSvc(db).PublishFloorAsync(floorId, zoneA.Id, "u");

        Assert.Equal(1, n);
        var a = await db.Space_Locations.SingleAsync(l => l.RackId == rackA.Id);
        var b = await db.Space_Locations.SingleAsync(l => l.RackId == rackB.Id);
        Assert.Equal(1, a.Status);
        Assert.Equal(0, b.Status);   // Zone B 未被波及
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Publish_WithZoneId"`
Expected: FAIL——当前 zoneId 被忽略：整层闸门被 Zone B 空码拦下抛 E-SPACE-307（或若先改了过滤未改闸门，则 b.Status==1 断言失败）。

- [ ] **Step 3: PrecheckAsync 加库区维度**

`ICodeEngineService.cs` 中 `PrecheckAsync` 签名改为：

```csharp
    /// <summary>发布前编码预检（ch03 §9.2）。zoneId 给定时按库区收窄（H5：库位经 Rack.ZoneId 归属）。</summary>
    Task<CodePrecheckResp> PrecheckAsync(Guid floorId, Guid? zoneId = null);
```

`CodeEngineService.cs` `PrecheckAsync`（:282）改为：

```csharp
    /// <inheritdoc/>
    public async Task<CodePrecheckResp> PrecheckAsync(Guid floorId, Guid? zoneId = null)
    {
        var resp = new CodePrecheckResp();

        // 拉 floor（或指定库区）内全部草稿库位——库区归属经 Rack.ZoneId 推导
        var locQuery = _db.Space_Locations.Where(l => l.FloorId == floorId && l.Status == 0);
        if (zoneId != null)
        {
            var rackIds = await _db.Space_Racks.Where(r => r.ZoneId == zoneId).Select(r => r.Id).ToListAsync();
            locQuery = locQuery.Where(l => l.RackId != null && rackIds.Contains(l.RackId.Value));
        }
        var locs = await locQuery.ToListAsync();

        resp.EmptyCodeCount = locs.Count(l => l.LocationCode == null);

        resp.DuplicateGroups = locs
            .Where(l => l.LocationCode != null)
            .GroupBy(l => l.LocationCode!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(x => x.Id).ToList())
            .ToList();

        resp.UnplacedDraftCount = locs.Count(l => l.LocationCode != null && !l.Placed);

        // 规则完备性：对 floor（或指定库区）跑静态预检，汇总错误码（去重）
        var zoneQuery = _db.Space_Zones.Where(z => z.FloorId == floorId);
        if (zoneId != null) zoneQuery = zoneQuery.Where(z => z.Id == zoneId);
        var zones = await zoneQuery.ToListAsync();
        var rules = await _db.Space_CodeRules.ToListAsync();
        var precheckErrs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var z in zones)
        {
            try
            {
                var rule = PickRule(rules, z.Id, floorId);
                var segs = DeserializeSegs(rule.Segments);
                foreach (var err in CodePrecheck.Validate(segs))
                    precheckErrs.Add(err);
            }
            catch (InvalidOperationException ex)
            {
                // E-SPACE-301 (无规则) / E-SPACE-302 (多规则无默认)
                precheckErrs.Add(ex.Message);
            }
        }

        resp.PrecheckErrors = precheckErrs.ToList();
        return resp;
    }
```

- [ ] **Step 4: PublishFloorAsync 按库区过滤**

`LocationPublishService.PublishFloorAsync`（Task 5 事务版本内），步骤 1-2 改为：

```csharp
            // 1. 闸门（ch03 §9.2；zoneId 给定时按库区收窄，H5）
            var pre = await _code.PrecheckAsync(floorId, zoneId);
            if (pre.EmptyCodeCount > 0 || pre.DuplicateGroups.Count > 0 || pre.PrecheckErrors.Count > 0)
                throw new InvalidOperationException("E-SPACE-307: 楼层存在空码、重码或其他预检错误，无法发布");

            // 2. 取 Status=0 且编码就绪的库位（zoneId 给定时经 Rack.ZoneId 收窄）
            var locQuery = _db.Space_Locations
                .Where(l => l.FloorId == floorId && l.Status == 0 && l.LocationCode != null);
            if (zoneId != null)
            {
                var rackIds = await _db.Space_Racks.Where(r => r.ZoneId == zoneId).Select(r => r.Id).ToListAsync();
                locQuery = locQuery.Where(l => l.RackId != null && rackIds.Contains(l.RackId.Value));
            }
            var locs = await locQuery.ToListAsync();
```

- [ ] **Step 5: code-precheck 端点透传 zoneId**

`CP6.WebApi/Controllers/Space/CodeRuleController.cs` 的 `GET floor/{id}/code-precheck` 动作：方法签名加 `[FromQuery] Guid? zoneId = null`，调用改为 `PrecheckAsync(id, zoneId)`，返回包壳与现有写法保持一致。

- [ ] **Step 6: 跑测试确认通过 + Commit**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests"`
Expected: 全 PASS。

```bash
git add CP6.Core/Services/Space/ICodeEngineService.cs CP6.Core/Services/Space/CodeEngineService.cs CP6.Core/Services/Space/LocationPublishService.cs CP6.WebApi/Controllers/Space/CodeRuleController.cs CP6.Tests/LocationPublishServiceTests.cs
git commit -m "fix(space): 按库区发布真实现——zoneId 过滤 + 闸门同步收窄（评审 H5）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 库存校验带仓维度（H7）+ 并发冲突 409（H8）

**Files:**
- Modify: `CP6.Core/Services/Integration/IWmsStockQuery.cs:18`（`GetStockQtyAsync` 签名 + `StubWmsStockQuery` 同步）
- Modify: `CP6.Core/Services/Wms/WmsStockQuery.cs:14-15`（实现加仓过滤）
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`（`DeactivateAsync` 提前解析 warehouseCd 并传入）
- Modify: `CP6.WebApi/Controllers/Space/LocationPublishController.cs`（publish/deactivate/adopt 三动作补 `DbUpdateConcurrencyException` → 409）
- Test: `CP6.Tests/WmsStockQueryTests.cs`（新增 1 个测试）+ `CP6.Tests/LocationPublishServiceTests.cs` 的 `FixedStockQuery` 签名同步

**Interfaces:**
- Consumes: Task 2 `ResolveWarehouseCdAsync`、Task 4 版 `DeactivateAsync`
- Produces: `Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default)`——默认参数向后兼容，`SpaceStockController` 等既有调用点不受影响。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/WmsStockQueryTests.cs` 末尾追加（自带上下文，不依赖该文件既有帮手）：

```csharp
    [Fact]
    public async Task GetStockQty_WarehouseScoped_ExcludesOtherWarehouses()
    {
        // H7：多仓同码时不带仓维度会跨仓求和 → 误拦他仓同码库位的停用
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Stocks.Add(new Stock { Id = Guid.NewGuid(), WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P", LotNo = "", PhysicalQty = 3m });
        db.Stocks.Add(new Stock { Id = Guid.NewGuid(), WarehouseCd = "W2", LocationCd = "A-01", ProductCd = "P", LotNo = "", PhysicalQty = 7m });
        await db.SaveChangesAsync();

        var q = new WmsStockQuery(db);
        Assert.Equal(10m, await q.GetStockQtyAsync("A-01"));        // 兼容：不带仓维度=跨仓求和
        Assert.Equal(3m, await q.GetStockQtyAsync("A-01", "W1"));   // 带仓维度只算本仓
    }
```

（文件顶部 using 如缺 `CP6.Entity.DomainModels.Wms` 则补。）

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~GetStockQty_WarehouseScoped"`
Expected: FAIL——编译错（`GetStockQtyAsync` 无双参重载）。

- [ ] **Step 3: 改签名 + 实现**

① `IWmsStockQuery.cs:18` 改为：

```csharp
    /// <summary>单库位库存量（04 停用前置校验用）。warehouseCd 给定时按 (仓,码) 锚查（§3.4 多仓防串仓）。</summary>
    Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default);
```

② 同文件 `StubWmsStockQuery` 对应方法：

```csharp
    public Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default)
        => Task.FromResult(0m);
```

③ `WmsStockQuery.cs:14-15` 改为：

```csharp
    public async Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default)
    {
        var q = _db.Stocks.Where(s => s.LocationCd == locationCode);
        if (!string.IsNullOrEmpty(warehouseCd)) q = q.Where(s => s.WarehouseCd == warehouseCd);
        return await q.SumAsync(s => s.PhysicalQty, ct);
    }
```

④ `CP6.Tests/LocationPublishServiceTests.cs` 内 `FixedStockQuery` 的对应方法签名同步：

```csharp
        public Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default) => Task.FromResult(_qty);
```

⑤ `LocationPublishService.DeactivateAsync`：把 `ResolveWarehouseCdAsync` 提到 ① 前置校验之前、一次解析两处复用：

```csharp
        // ① 前置校验（用户体验，连 RPC 都不发；ch04 §6.1①；H7 带仓维度防多仓同码误拦）
        var warehouseCd = await ResolveWarehouseCdAsync(l);
        var qty = await _stock.GetStockQtyAsync(l.LocationCode ?? "", warehouseCd);
        if (qty > 0)
            throw new InvalidOperationException("E-SPACE-401: 库位仍有库存，无法停用");
```

同方法内 `WmsDeactivateRequest` 的 `WarehouseCd = await ResolveWarehouseCdAsync(l)` 改为 `WarehouseCd = warehouseCd`。

- [ ] **Step 4: 控制器并发冲突 409（H8）**

`LocationPublishController.cs` 的 `PublishFloor`、`Deactivate`、`Adopt` 三个动作，在既有 `catch (InvalidOperationException e)` 之后各加：

```csharp
        catch (DbUpdateConcurrencyException)
        {
            // ch04 §11 E-SPACE-009：RowVersion 乐观并发冲突 → 409（此前落到 500）
            return Conflict(new { code = 409, message = "E-SPACE-009: 数据已被他人修改，请刷新重试" });
        }
```

（`using Microsoft.EntityFrameworkCore;` 该文件已有，:6。InMemory 无 rowversion，无法单测并发路径——以编译 + Task 9 真库冒烟为验证。）

- [ ] **Step 5: 跑测试 + Commit**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS（含新仓维度测试）。

```bash
git add CP6.Core/Services/Integration/IWmsStockQuery.cs CP6.Core/Services/Wms/WmsStockQuery.cs CP6.Core/Services/Space/LocationPublishService.cs CP6.WebApi/Controllers/Space/LocationPublishController.cs CP6.Tests/WmsStockQueryTests.cs CP6.Tests/LocationPublishServiceTests.cs
git commit -m "fix(space): 停用库存校验带仓维度（评审 H7）+ 并发冲突映射 409 E-SPACE-009（评审 H8）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: 场景保存状态机护栏（H1：堵住 Status/CodeOrigin 后门）

**Files:**
- Modify: `CP6.Core/Services/Space/SceneService.cs:207-235`（Locations 差量块）
- Test: `CP6.Tests/SceneServiceTests.cs`（新增 2 个测试；该文件帮手为 `Make()`，返回 `(db, svc)`）

**Interfaces:**
- Consumes: 无新依赖
- Produces: 行为变化——场景保存对已存在库位**不再接受** `Status`/`CodeOrigin` 覆盖；新建库位强制 `Status=0, CodeOrigin=1`。状态流转唯一通道：publish / deactivate / adopt / bind-codes。**注意**：若 `SceneServiceTests`/`SceneIoServiceTests` 中存在断言"场景保存能写入 Status=1 或 CodeOrigin=2"的既有测试，那是 H1 漏洞的固化，应更新断言并在 commit message 说明；若 `SceneIoService` 导入路径依赖经 SaveSceneAsync 保留 Status，则导入处应改为直接操作实体（绕开场景保存护栏），执行时核实并报告。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/SceneServiceTests.cs` 末尾追加：

```csharp
    [Fact]
    public async Task SaveScene_CannotFlipPublishedStatus_OrCodeOrigin()
    {
        // H1：场景保存曾可任意覆盖 Status/CodeOrigin——绕过 publish/deactivate 状态机的后门
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = null,
            Placed = false, Status = 1, CodeOrigin = 2, LocationCode = "EXT-001", Version = 3
        });
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = locId, RackId = null, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 0, CodeOrigin = 1 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);       // 发布状态不被场景保存改写
        Assert.Equal(2, loc.CodeOrigin);   // 来源标签（对账依据）同理
    }

    [Fact]
    public async Task SaveScene_NewLocation_ForcedDraft()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = Guid.NewGuid(), RackId = null, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 1, CodeOrigin = 2 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(0, loc.Status);       // 编辑器新建恒草稿；发布走 publish、采纳走 adopt
        Assert.Equal(1, loc.CodeOrigin);
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SaveScene_CannotFlip|FullyQualifiedName~SaveScene_NewLocation_ForcedDraft"`
Expected: 2 个 FAIL（当前 DTO 值直通落库）。

- [ ] **Step 3: 加护栏**

`SceneService.cs` Locations 差量块（:207-235）改为：

```csharp
                if (existing != null)
                {
                    existing.RackId     = ld.RackId;
                    existing.Col        = ld.Col;
                    existing.Level      = ld.Level;
                    existing.Depth      = ld.Depth;
                    existing.Placed     = ld.Placed;
                    // H1 状态机护栏：Status/CodeOrigin 不接受场景保存覆盖——
                    // 状态只经 publish/deactivate 通道流转（ch04 §4），来源标签只在生码/采纳时落定
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Locations.Add(new Space_Location
                    {
                        Id         = ld.Id ?? Guid.NewGuid(),
                        RackId     = ld.RackId,
                        FloorId    = floorId,
                        Col        = ld.Col,
                        Level      = ld.Level,
                        Depth      = ld.Depth,
                        Placed     = ld.Placed,
                        Status     = 0,   // H1：编辑器新建恒草稿（发布走 publish、采纳走 adopt/bind-codes）
                        CodeOrigin = 1,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
```

- [ ] **Step 4: 跑 Scene 全套测试确认无回归**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SceneServiceTests|FullyQualifiedName~SceneIoServiceTests|FullyQualifiedName~BindCodesTests"`
Expected: 全 PASS。若有既有测试断言场景保存写入 Status/CodeOrigin → 按本 Task 头部说明处理并报告。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Space/SceneService.cs CP6.Tests/SceneServiceTests.cs
git commit -m "fix(space): 场景保存状态机护栏——Status/CodeOrigin 拒绝 DTO 覆盖，新建强制草稿（评审 H1）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: 端到端联调验证（真库迁移 + live QA 冒烟）

**Files:**
- 无代码变更（验证任务）；如冒烟暴露缺陷 → 修复 + 补测试后单独 commit

**Interfaces:**
- Consumes: Task 1–8 全部产物
- Produces: 波1 DoD 证据（迁移应用成功 + 发布/停用真链路落 T_WmsBin 的实证）

- [ ] **Step 1: 应用迁移到开发库**

Run: `dotnet ef database update --project CP6.Core --startup-project CP6.WebApi`
Expected: `Applying migration '..._SpaceWave1WmsBin'` → Done。

- [ ] **Step 2: 起后端 + 冒烟发布链路**

启动 `CP6.WebApi`（项目惯例命令/容器栈见记忆 new-env-setup-2026-07）。用既有 QA 账号（admin / 123456）拿 token 后依次：

1. `POST /api/space/floor/{floorId}/publish`（挑一个有已生码草稿库位的楼层；无则先 `POST /api/space/floor/{id}/generate-codes`）
2. 查库：`SELECT Id, LocationCode, WarehouseCd, Version, IsActive FROM T_WmsBin` → 应有新行，`WarehouseCd` = site 映射值或 SiteCode
3. 重复步骤 1 再发布同层 → 无新增草稿时返回 0；对同批库位人为重投事件（`GET /api/space/publish/events` 确认事件 `SUCCESS`，无重复批次行）
4. `PUT /api/space/location/{id}/deactivate`（挑无库存库位）→ `T_WmsBin.IsActive=0`、Space `Status=2`
5. 给某库位对应 `(WarehouseCd, LocationCd)` 插一条 `T_Stock`（`PhysicalQty=5`）再停用 → 返回 W-SPACE-404，`Status` 保持 1
6. 带 `{"zoneId": "<某库区>"}` 请求体发布 → 只有该库区库位落 T_WmsBin（H5）
7. `POST /api/space/floor/{id}/scene` 携带把已发布库位 `status:0` 的载荷 → 保存后查库 `Status` 仍为 1（H1）

Expected: 7 步全符合预期；`T_IntegrationEvent` 中 SPACE 事件状态 SUCCESS 且重试计数无异常增长。

- [ ] **Step 3: 全量测试基线复核 + 收尾**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS，通过数 ≥ 既有基线 + 本波新增 19 个（Task 2×2 + Task 3×9 + Task 4×3 + Task 5×1 + Task 6×1 + Task 7×1 + Task 8×2）。

若冒烟无缺陷，本 Task 无 commit；有缺陷则修复+测试后：

```bash
git commit -m "fix(space): 波1联调修复——<具体问题>

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 自检记录（写计划时已核；2026-07-05 评审修订后更新）

- **Spec 覆盖**：ch04 v1.1 五项补丁——① T_WmsBin（Task 1/3）② WarehouseCd 映射（Task 1/2）③ publishedBy 溯源落 `T_WmsBin.LastPublishedBy`（Task 3，P1 最低交付口径；`PersistEventAsync` 加 userId 参数属基建改造，本波不动共享基类）④ 停用同步 RPC（Task 4）⑤ 逐项结果 schema（Task 3）。盘点三隐患——发布非原子（Task 5 事务）、重试重复落事件（Task 5 persistEvent）、发布闸门 TOCTOU（Task 5 事务包裹闸门→提交 + 既有过滤唯一索引兜底，剩余窗口由 DB 约束拦截）。
- **评审修订覆盖（2026-07-05 用户批准）**：H1 场景保存状态机后门（Task 8）；H5 zoneId 假参数 + 闸门收窄（Task 6）；H6 停用乱序孤儿 Bin → 墓碑机制，**这是对契约 §5.1"无 bin 跳过"的有意修正**（Task 3 消费端 + Task 4 同步 RPC 两处）；H7 库存校验仓维度（Task 7）；H8 并发冲突 409（Task 7）。
- **明确不做（划出波1）**：H2/H3/H4 编辑动作↔发布联动（缩格幽灵库位/删巷道护栏/改挂 re-publish + 库位删除通道）→ **波 1.5「发布触发矩阵兑现」计划，波 1 完成后基于新代码基线编写**；`/reconcile` 采纳对账端点（契约 §8.2，随波3）；H9 采纳内存去重优化（随波3）；删除护栏 `?mode=deactivate|rehome`（契约 §7.2，并入波1.5）；错误码 BizException 化（波4）；SpaceSqlIntegrationTests 真库 CI（波5）。
- **类型一致性**：`LocationPublishService` 6 参构造在 Task 4 定义、DI 与测试帮手同步；`persistEvent` 带默认值不破坏既有调用；`WmsDeactivateRequest/Result` 仅 Task 4 内定义与消费；`ResolveWarehouseCdAsync` Task 2 定义、Task 4/7 复用；`PrecheckAsync`/`GetStockQtyAsync` 均加默认参数，既有调用点编译不破坏；Task 6/7 对 `DeactivateAsync`/`PublishFloorAsync` 的修改基于 Task 4/5 之后的代码形态（执行顺序 1→9 严格串行）。
