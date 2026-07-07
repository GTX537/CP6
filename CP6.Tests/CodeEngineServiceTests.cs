using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// Phase C 编码引擎测试（ch03 C-1..C-5）。
/// [InMemory 仅测逻辑]：过滤唯一索引/并发约束需补 Task D-9 真库集成测。
/// v1.1：new CP6Context(options) 单参构造 → CurrentTenantId 回退 TenantContext.DefaultTenant；
///         全局过滤 + SaveChanges 盖章均按默认租户，测试自洽，无需显式设 TenantId。
/// </summary>
public class CodeEngineServiceTests
{
    // ── 工厂 ─────────────────────────────────────────────────────────────

    /// <summary>每测建独立 InMemory 库（Guid 名称，避免跨测污染）。</summary>
    private static CP6Context Db() =>
        new(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CodeEngineService Svc(CP6Context db) => new(db);

    // ══════════════════════════════════════════════════════════════════════
    // C-1: 段渲染（CodeSegment.Render）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderSegment_SeqSource_PadsWidth()
    {
        // rack-seq: SeqIndex=3, Start=1, Step=1 → value=3 → PadLeft(2,'0') → "03"
        var seg = new CodeSegmentDef { Key = "rack", Source = "rack-seq", Width = 2, Pad = "0", Start = 1, Step = 1 };
        Assert.Equal("03", CodeSegment.Render(seg, new SegInput { SeqIndex = 3 }));
    }

    [Fact]
    public void RenderSegment_SeqSource_StartOffset()
    {
        // Start=10, Step=2, SeqIndex=1 → 10; SeqIndex=2 → 12
        var seg = new CodeSegmentDef { Source = "col", Start = 10, Step = 2 };
        Assert.Equal("10", CodeSegment.Render(seg, new SegInput { SeqIndex = 1 }));
        Assert.Equal("12", CodeSegment.Render(seg, new SegInput { SeqIndex = 2 }));
    }

    [Fact]
    public void RenderSegment_CodeSource_Upper()
    {
        // zone-code lower → upper
        var seg = new CodeSegmentDef { Key = "zone", Source = "zone-code", Upper = true };
        Assert.Equal("A", CodeSegment.Render(seg, new SegInput { RawCode = "a" }));
    }

    [Fact]
    public void RenderSegment_Fixed_ReturnsFixedValue()
    {
        var seg = new CodeSegmentDef { Source = "fixed", FixedValue = "WH" };
        Assert.Equal("WH", CodeSegment.Render(seg, new SegInput()));
    }

    [Fact]
    public void IsSeq_SeqSources_ReturnsTrue()
    {
        foreach (var src in new[] { "zone-seq", "aisle-seq", "rack-seq", "col", "level", "depth" })
            Assert.True(CodeSegment.IsSeq(src), $"Expected IsSeq=true for '{src}'");
    }

    [Fact]
    public void IsSeq_CodeSources_ReturnsFalse()
    {
        foreach (var src in new[] { "zone-code", "aisle-code", "rack-code", "site-code", "floor-level", "fixed" })
            Assert.False(CodeSegment.IsSeq(src), $"Expected IsSeq=false for '{src}'");
    }

    // ══════════════════════════════════════════════════════════════════════
    // C-2: 静态预检（CodePrecheck.Validate）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Static_NoZoneSegment_E303()
    {
        // 无 zone-code/zone-seq → E-SPACE-303
        var segs = new List<CodeSegmentDef>
        {
            new() { Key = "rack", Source = "rack-seq" },
            new() { Key = "col",  Source = "col" }
        };
        Assert.Contains("E-SPACE-303", CodePrecheck.Validate(segs));
    }

    [Fact]
    public void Static_NoLocationGranularity_E306()
    {
        // zone-code 但无 col/level/depth → E-SPACE-306
        var segs = new List<CodeSegmentDef>
        {
            new() { Key = "zone", Source = "zone-code" },
            new() { Key = "rack", Source = "rack-seq" }
        };
        Assert.Contains("E-SPACE-306", CodePrecheck.Validate(segs));
    }

    [Fact]
    public void Static_AisleSegNotOptional_E305()
    {
        // 巷道段未标 optional → E-SPACE-305
        var segs = new List<CodeSegmentDef>
        {
            new() { Key = "zone",  Source = "zone-code" },
            new() { Key = "aisle", Source = "aisle-code", Optional = false },  // 漏设 optional
            new() { Key = "rack",  Source = "rack-seq" },
            new() { Key = "col",   Source = "col" }
        };
        Assert.Contains("E-SPACE-305", CodePrecheck.Validate(segs));
    }

    [Fact]
    public void Static_ValidRule_NoErrors()
    {
        // zone-code + aisle-code(optional) + rack-seq + col → 全通过
        var segs = new List<CodeSegmentDef>
        {
            new() { Key = "zone",  Source = "zone-code" },
            new() { Key = "aisle", Source = "aisle-code", Optional = true },
            new() { Key = "rack",  Source = "rack-seq",   Width = 2 },
            new() { Key = "col",   Source = "col",        Width = 2 }
        };
        Assert.Empty(CodePrecheck.Validate(segs));
    }

    [Fact]
    public void Static_SiteFloorCombination_PassesZoneCheck()
    {
        // site-code + floor-level 组合等同 Zone 区分，不报 E-303
        var segs = new List<CodeSegmentDef>
        {
            new() { Source = "site-code" },
            new() { Source = "floor-level" },
            new() { Source = "rack-seq" },
            new() { Source = "col" }
        };
        Assert.DoesNotContain("E-SPACE-303", CodePrecheck.Validate(segs));
    }

    // ══════════════════════════════════════════════════════════════════════
    // C-3: 生成引擎
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>种最小层级 Site → Floor → Zone → Rack，返回 (floorId, zoneId, rackId)。</summary>
    private static (Guid floorId, Guid zoneId, Guid rackId) SeedFloorZoneRack(CP6Context db)
    {
        var siteId  = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var rackId  = Guid.NewGuid();
        db.Space_Sites .Add(new Space_Site  { Id = siteId,  SiteCode = "WH1",  SiteName = "仓库1" });
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = siteId, Level = 1, FloorCode = "F1", FloorName = "一楼" });
        db.Space_Zones .Add(new Space_Zone  { Id = zoneId,  FloorId = floorId, ZoneCode = "A", ZoneName = "A区" });
        db.Space_Racks .Add(new Space_Rack  { Id = rackId,  ZoneId = zoneId, FloorId = floorId, RackCode = "R01", X = 0, Y = 0, Cols = 5, Levels = 3, DepthCount = 1 });
        return (floorId, zoneId, rackId);
    }

    /// <summary>租户默认作用域规则：zone-code "-" rack-seq(2) "-" col(2) "-" level(2)。</summary>
    private static Space_CodeRule DefaultRule() => new()
    {
        Id        = Guid.NewGuid(),
        RuleName  = "默认规则",
        ScopeType = 0,
        ScopeId   = null,
        IsDefault = true,
        Segments  = JsonSerializer.Serialize(new List<CodeSegmentDef>
        {
            new() { Key = "zone", Source = "zone-code", Sep = "-" },
            new() { Key = "rack", Source = "rack-seq",  Width = 2, Sep = "-" },
            new() { Key = "col",  Source = "col",       Width = 2, Sep = "-" },
            new() { Key = "lvl",  Source = "level",     Width = 2, Sep = "" }
        })
    };

    /// <summary>草稿库位（Status=0, CodeOrigin=1, Placed=true）。</summary>
    private static Space_Location Draft(Guid rackId, Guid floorId, int col, int level, int depth,
        string? code = null) => new()
    {
        Id           = Guid.NewGuid(),
        RackId       = rackId,
        FloorId      = floorId,
        Col          = col, Level = level, Depth = depth,
        Status       = 0,    // 草稿
        CodeOrigin   = 1,    // 引擎生成
        Placed       = true,
        LocationCode = code
    };

    [Fact]
    public async Task Generate_SkipsPublishedLocations()
    {
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_CodeRules.Add(DefaultRule());
        var draftLoc = Draft(rackId, floorId, 1, 1, 1);
        var pubLoc   = new Space_Location
        {
            Id = Guid.NewGuid(), RackId = rackId, FloorId = floorId,
            Col = 2, Level = 1, Depth = 1,
            Status = 1, CodeOrigin = 1, Placed = true, LocationCode = "FROZEN"
        };
        db.Space_Locations.AddRange(draftLoc, pubLoc);
        await db.SaveChangesAsync();

        var res = await Svc(db).GenerateAsync(floorId, "rebuild", null);

        // 只返回草稿那条的编码
        Assert.Single(res);
        // 已发布库位编码不变
        var pub   = await db.Space_Locations.FirstAsync(l => l.Status == 1);
        Assert.Equal("FROZEN", pub.LocationCode);
        // 草稿库位有了编码
        var draft = await db.Space_Locations.FirstAsync(l => l.Status == 0);
        Assert.NotNull(draft.LocationCode);
    }

    [Fact]
    public async Task Generate_FillEmpty_DoesNotOverwriteExistingCode()
    {
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_CodeRules.Add(DefaultRule());
        var withCode    = Draft(rackId, floorId, 1, 1, 1, code: "EXISTING");
        var withoutCode = Draft(rackId, floorId, 2, 1, 1);
        db.Space_Locations.AddRange(withCode, withoutCode);
        await db.SaveChangesAsync();

        var res = await Svc(db).GenerateAsync(floorId, "fill-empty", null);

        // fill-empty 只生成空码那条
        Assert.Single(res);
        // 既有码不动
        var e = await db.Space_Locations.FirstAsync(l => l.Id == withCode.Id);
        Assert.Equal("EXISTING", e.LocationCode);
    }

    [Fact]
    public async Task Generate_BatchDuplicate_Throws_E304()
    {
        // 两个库位完全相同坐标 → 拼出相同码 → 批内重复 → E-SPACE-304 整体不写
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_CodeRules.Add(DefaultRule());
        db.Space_Locations.AddRange(
            Draft(rackId, floorId, 1, 1, 1),
            Draft(rackId, floorId, 1, 1, 1));   // 同坐标 → 同码
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => Svc(db).GenerateAsync(floorId, "rebuild", null));
        Assert.Equal("E-SPACE-304", ex.Code);
    }

    [Fact]
    public async Task Generate_Rebuild_TwoPhase_OldCodeReplaced()
    {
        // rebuild 模式两阶段：旧码全部替换为新码（InMemory 测逻辑正确性）
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_CodeRules.Add(DefaultRule());
        var loc = Draft(rackId, floorId, 1, 1, 1, code: "OLD-CODE");
        db.Space_Locations.Add(loc);
        await db.SaveChangesAsync();

        var res = await Svc(db).GenerateAsync(floorId, "rebuild", null);

        Assert.Single(res);
        var updated = await db.Space_Locations.FirstAsync(l => l.Id == loc.Id);
        Assert.NotNull(updated.LocationCode);
        Assert.NotEqual("OLD-CODE", updated.LocationCode);
    }

    [Fact]
    public async Task Generate_VariableLen_AisleAndNoAisle_UniqueCode()
    {
        // 变长唯一（ch03 §5.3）：同一 Zone 下有巷道货架 + 无巷道货架，生成码不互撞。
        // 关键：rackSeq 按 Zone 分组编号（rack1=1 因 X=0，rack2=2 因 X=1000），
        //       即使 col/level 相同，码值不同（rack1 含 aisle 段"A01"，rack2 跳过 aisle 段）。
        using var db = Db();

        // 手工种 Site / Floor / Zone / Rack×2 / Aisle，直接设好 rack1.AisleId 避免保存前查询
        var siteId  = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId  = Guid.NewGuid();
        var rack1Id = Guid.NewGuid();
        var rack2Id = Guid.NewGuid();
        var aisleId = Guid.NewGuid();

        db.Space_Sites .Add(new Space_Site  { Id = siteId,  SiteCode = "WH1", SiteName = "仓库1" });
        db.Space_Floors.Add(new Space_Floor { Id = floorId, SiteId = siteId, Level = 1, FloorCode = "F1", FloorName = "一楼" });
        db.Space_Zones .Add(new Space_Zone  { Id = zoneId,  FloorId = floorId, ZoneCode = "A", ZoneName = "A区" });
        db.Space_Aisles.Add(new Space_Aisle { Id = aisleId, ZoneId = zoneId,  AisleCode = "A01" });

        // rack1 挂巷道，X=0 → Zone 内 rackSeq=1
        db.Space_Racks.Add(new Space_Rack
        {
            Id = rack1Id, ZoneId = zoneId, FloorId = floorId, AisleId = aisleId,
            RackCode = "R01", X = 0, Y = 0, Cols = 5, Levels = 3, DepthCount = 1
        });
        // rack2 无巷道，X=1000 → Zone 内 rackSeq=2
        db.Space_Racks.Add(new Space_Rack
        {
            Id = rack2Id, ZoneId = zoneId, FloorId = floorId,
            RackCode = "R02", X = 1000, Y = 0, Cols = 5, Levels = 3, DepthCount = 1
        });

        // 规则：zone-code "-" aisle-code(optional) "-" rack-seq(2) "-" col(2) "-" level(2)
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "变长规则", ScopeType = 0, IsDefault = true,
            Segments = JsonSerializer.Serialize(new List<CodeSegmentDef>
            {
                new() { Key = "zone",  Source = "zone-code",  Sep = "-" },
                new() { Key = "aisle", Source = "aisle-code", Optional = true, Sep = "-" },
                new() { Key = "rack",  Source = "rack-seq",   Width = 2, Sep = "-" },
                new() { Key = "col",   Source = "col",        Width = 2, Sep = "-" },
                new() { Key = "lvl",   Source = "level",      Width = 2, Sep = "" }
            })
        });

        // 各放一个草稿（相同 col=1, level=1, depth=1）
        db.Space_Locations.AddRange(
            Draft(rack1Id, floorId, 1, 1, 1),   // rack1：有巷道 A01，rackSeq=1 → "A-A01-01-01-01"
            Draft(rack2Id, floorId, 1, 1, 1)    // rack2：无巷道，  rackSeq=2 → "A-02-01-01"
        );
        await db.SaveChangesAsync();

        var codes = await Svc(db).GenerateAsync(floorId, "rebuild", null);

        // 两条都成功生成（无 E-SPACE-304）
        Assert.Equal(2, codes.Count);
        // 码不互撞（变长唯一保证）：distinct count 仍为 2
        Assert.Equal(2, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // rack1（有巷道）应含巷道码 "A01"，rack2（无巷道）不含
        Assert.Contains(codes, c => c.Contains("A01"));
        Assert.Contains(codes, c => !c.Contains("A01"));
    }

    [Fact]
    public async Task Generate_ScopeZoneId_OnlyTargetZone()
    {
        // scopeZoneId 限定时只生成指定库区的库位码
        using var db = Db();
        var (floorId, zoneId, rack1Id) = SeedFloorZoneRack(db);

        // 建第二个库区和货架
        var zone2Id = Guid.NewGuid();
        var rack2Id = Guid.NewGuid();
        db.Space_Zones.Add(new Space_Zone { Id = zone2Id, FloorId = floorId, ZoneCode = "B", ZoneName = "B区" });
        db.Space_Racks.Add(new Space_Rack { Id = rack2Id, ZoneId = zone2Id, FloorId = floorId, RackCode = "R01", X = 0, Y = 0, Cols = 2, Levels = 2, DepthCount = 1 });

        db.Space_CodeRules.Add(DefaultRule());
        db.Space_Locations.AddRange(
            Draft(rack1Id, floorId, 1, 1, 1),   // zone A
            Draft(rack2Id, floorId, 1, 1, 1)    // zone B
        );
        await db.SaveChangesAsync();

        // 仅生成 zone A
        var codes = await Svc(db).GenerateAsync(floorId, "rebuild", zoneId);
        Assert.Single(codes);
    }

    // ══════════════════════════════════════════════════════════════════════
    // C-4: 实时预览（ch03 §8）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Preview_ValidRule_ReturnsStructureAndSamples()
    {
        using var db = Db();
        var req = new CodePreviewReq
        {
            Segments = new()
            {
                new() { Key = "zone",  Source = "zone-code",  Sep = "-" },
                new() { Key = "aisle", Source = "aisle-code", Optional = true, Sep = "-" },
                new() { Key = "rack",  Source = "rack-seq",   Width = 2, Sep = "-" },
                new() { Key = "col",   Source = "col",        Width = 2, Sep = "" }
            }
        };
        var resp = await Svc(db).PreviewAsync(req);

        Assert.True(resp.Precheck.Ok);
        Assert.Equal(4, resp.Structure.Count);
        Assert.NotEmpty(resp.Samples);
        // 有巷道 vs 无巷道两路样例
        Assert.NotEmpty(resp.VariableLen.WithAisle);
        Assert.NotEmpty(resp.VariableLen.WithoutAisle);
        Assert.NotEqual(resp.VariableLen.WithAisle, resp.VariableLen.WithoutAisle);
        // 两路均在 Samples 中
        Assert.Contains(resp.VariableLen.WithAisle,    resp.Samples);
        Assert.Contains(resp.VariableLen.WithoutAisle, resp.Samples);
    }

    [Fact]
    public async Task Preview_InvalidRule_PrecheckNotOk()
    {
        using var db = Db();
        var req = new CodePreviewReq
        {
            // 缺 zone + 缺 col/level/depth → 同时触 E-303 和 E-306
            Segments = new() { new() { Key = "rack", Source = "rack-seq" } }
        };
        var resp = await Svc(db).PreviewAsync(req);

        Assert.False(resp.Precheck.Ok);
        Assert.NotEmpty(resp.Precheck.Errors);
        Assert.Contains("E-SPACE-303", resp.Precheck.Errors);
        Assert.Contains("E-SPACE-306", resp.Precheck.Errors);
    }

    [Fact]
    public async Task Preview_NoAisleSegment_TwoSamplesAreSame()
    {
        // 规则无巷道段时，有/无巷道两路完全相同（无条件跳过）
        using var db = Db();
        var req = new CodePreviewReq
        {
            Segments = new()
            {
                new() { Key = "zone", Source = "zone-code", Sep = "-" },
                new() { Key = "rack", Source = "rack-seq",  Width = 2, Sep = "-" },
                new() { Key = "col",  Source = "col",       Width = 2, Sep = "" }
            }
        };
        var resp = await Svc(db).PreviewAsync(req);

        // 无巷道段时两路相同，Samples 只有一条
        Assert.Equal(resp.VariableLen.WithAisle, resp.VariableLen.WithoutAisle);
        Assert.Single(resp.Samples);
    }

    // ══════════════════════════════════════════════════════════════════════
    // C-5: 发布前编码预检（ch03 §9.2）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Precheck_EmptyCodeCount_Positive()
    {
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_Locations.Add(Draft(rackId, floorId, 1, 1, 1));   // 空码草稿
        await db.SaveChangesAsync();

        var resp = await Svc(db).PrecheckAsync(floorId);
        Assert.True(resp.EmptyCodeCount > 0);
    }

    [Fact]
    public async Task Precheck_DuplicateGroups_NotEmpty()
    {
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        db.Space_Locations.AddRange(
            Draft(rackId, floorId, 1, 1, 1, code: "DUP-CODE"),
            Draft(rackId, floorId, 2, 1, 1, code: "DUP-CODE")    // 相同码 → 重复组
        );
        await db.SaveChangesAsync();

        var resp = await Svc(db).PrecheckAsync(floorId);
        Assert.NotEmpty(resp.DuplicateGroups);
        Assert.Equal(2, resp.DuplicateGroups[0].Count);   // 两个 LocationId 在同一组
    }

    [Fact]
    public async Task Precheck_NoIssues_AllCountsZero()
    {
        using var db = Db();
        var (floorId, _, rackId) = SeedFloorZoneRack(db);
        // 所有草稿都有唯一码且已落位
        db.Space_Locations.AddRange(
            Draft(rackId, floorId, 1, 1, 1, code: "CODE-001"),
            Draft(rackId, floorId, 2, 1, 1, code: "CODE-002")
        );
        await db.SaveChangesAsync();

        var resp = await Svc(db).PrecheckAsync(floorId);
        Assert.Equal(0, resp.EmptyCodeCount);
        Assert.Empty(resp.DuplicateGroups);
        Assert.Equal(0, resp.UnplacedDraftCount);
    }

    [Fact]
    public async Task Precheck_MissingRule_PrecheckErrorsContainsE301()
    {
        // 有 Zone 但无任何规则 → PrecheckErrors 含 E-SPACE-301
        using var db = Db();
        var (floorId, _, _) = SeedFloorZoneRack(db);
        // 不加 CodeRule
        await db.SaveChangesAsync();

        var resp = await Svc(db).PrecheckAsync(floorId);
        Assert.Contains("E-SPACE-301", resp.PrecheckErrors);
    }
}
