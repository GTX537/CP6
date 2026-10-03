using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Space;
using CP6.Tests.Infra;
using CP6.Tests.DatabaseCompatibility;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace CP6.Tests;

/// <summary>
/// Space runtime 真库集成测试（ch04 D-9）：WP5 显式选择 SQL Server/PostgreSQL 时使用真实 Core 迁移。
///
/// 验证原生数据库语义；已选择 WP5 provider 时配置、迁移或连接失败均使测试失败：
///   · 过滤唯一索引：Space_Location 的 (TenantId, LocationCode) UNIQUE WHERE [LocationCode] IS NOT NULL
///     ——非空码租户内唯一，草稿期多行 NULL 码不互撞（CP6Context OnModelCreating HasFilter）。
///   · 两阶段换码：草稿→发布重排时经 NULL 中转规避唯一冲突（ch00 §4.6 / ch03 §7）。
///   · RowVersion 乐观锁：数据库生成的版本令牌使陈旧第二写抛 DbUpdateConcurrencyException。
///
/// WP5 使用 runner 自有库、每 case 独立租户，不建删数据库。
/// 原 CP6_TEST_SQLSERVER 入口仍保留每 case EnsureCreated/EnsureDeleted 的历史夹具。
/// </summary>
[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed class SpaceSqlIntegrationTests : IDisposable
{
    private readonly string? _connString;
    private readonly Wp5ReportsRelationalFixture _database;
    private readonly Guid _tenant = Guid.NewGuid();

    public SpaceSqlIntegrationTests(Wp5ReportsRelationalFixture database, ITestOutputHelper output)
    {
        _database = database;
        if (Wp5ReportsFactAttribute.IsSelected)
        {
            output.WriteLine(database.SetupSummary);
            return;
        }

        var baseConn = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrEmpty(baseConn))
            return;   // 未选择任何原生数据库：属性会跳过这些测试，不建库。

        // 从传入连接串派生唯一名临时库（Database 段被覆盖）
        _connString = new SqlConnectionStringBuilder(baseConn)
        {
            InitialCatalog = $"CP6Test_{Guid.NewGuid():N}"
        }.ConnectionString;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();   // 建全模型 schema（含 Space_Location 过滤唯一索引 + rowversion）
    }

    private CP6Context NewContext()
    {
        if (Wp5ReportsFactAttribute.IsSelected)
            return _database.CreateContext(_tenant);

        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(_connString!)
            .Options;
        return new CP6Context(options);
    }

    [SpaceCoreRelationalFact]
    public async Task AnalyticsControlTower_TranslatesNullableFloorFilter_OnSqlServer()
    {
        using var ctx = NewContext();
        var siteId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        ctx.Space_Sites.Add(new Space_Site
        {
            Id = siteId, SiteCode = "SQL-WH", SiteName = "SQL Warehouse", WarehouseCd = "SQL-WH",
        });
        ctx.Space_Floors.Add(new Space_Floor
        {
            Id = floorId, SiteId = siteId, FloorCode = "F1", FloorName = "Floor 1", Level = 1,
        });
        ctx.Space_Zones.Add(new Space_Zone
        {
            Id = zoneId, FloorId = floorId, ZoneCode = "A", ZoneName = "Zone A", ZoneType = 1,
        });
        ctx.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, FloorId = floorId, ZoneId = zoneId, RackCode = "R1",
            Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000,
        });
        ctx.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId, LocationCode = "SQL-LOC-001",
            Placed = true, Status = 1, AbsX = 100, AbsY = 100, Capacity = 10, CapacityUom = 1,
        });
        await ctx.SaveChangesAsync();

        var service = new SpaceAnalyticsService(
            ctx,
            new StubWmsStockQuery(),
            new StubWmsAnalyticsQuery(),
            NullLogger<SpaceAnalyticsService>.Instance,
            new UtcTenantClock(),
            TimeProvider.System);

        var tower = await service.GetControlTowerAsync(siteId);

        Assert.Equal(1, tower.TotalLocations);
        Assert.Equal(floorId, Assert.Single(tower.Floors).FloorId);
    }

    public void Dispose()
    {
        if (Wp5ReportsFactAttribute.IsSelected) return;
        if (_connString == null) return;
        try
        {
            using var ctx = NewContext();
            ctx.Database.EnsureDeleted();   // 删临时库
        }
        catch
        {
            // 兜底：清理失败不掩盖测试结果（临时库名含 Guid，不复用）
        }
    }

    private sealed class UtcTenantClock : ITenantClock
    {
        public TimeZoneInfo GetTenantTimeZone() => TimeZoneInfo.Utc;
    }

    // ── D-9.1: 过滤唯一索引 ──────────────────────────────────────────────

    /// <summary>同租户内两行相同非空 LocationCode → 第二次写入触发唯一索引冲突（DbUpdateException）。</summary>
    [SpaceCoreRelationalFact]
    public void UniqueIndex_SameNonNullCode_SecondInsertThrows()
    {
        using (var ctx = NewContext())
        {
            ctx.Space_Locations.Add(new Space_Location { LocationCode = "DUP-001", Status = 1 });
            ctx.SaveChanges();
        }

        using var ctx2 = NewContext();
        ctx2.Space_Locations.Add(new Space_Location { LocationCode = "DUP-001", Status = 1 });

        var ex = Assert.Throws<DbUpdateException>(() => ctx2.SaveChanges());
        var failure = DatabaseFailureClassifier.Classify(ex);
        Assert.Equal(DatabaseFailureKind.UniqueConstraint, failure.Kind);
        if (ctx2.Database.IsNpgsql())
            Assert.Equal("23505", failure.SqlState);
        else
            Assert.Contains(failure.DatabaseErrorCode, new int?[] { 2601, 2627 });
    }

    /// <summary>过滤索引 HasFilter([LocationCode] IS NOT NULL)：多行 NULL 码不互撞，可共存。</summary>
    [SpaceCoreRelationalFact]
    public void UniqueIndex_TwoNullCodes_BothCoexist()
    {
        using (var ctx = NewContext())
        {
            ctx.Space_Locations.Add(new Space_Location { LocationCode = null, Status = 0 });
            ctx.Space_Locations.Add(new Space_Location { LocationCode = null, Status = 0 });
            ctx.SaveChanges();   // 不应抛：NULL 被过滤索引排除
        }

        using var ctx2 = NewContext();
        Assert.Equal(2, ctx2.Space_Locations.Count(x => x.LocationCode == null));
    }

    // ── D-9.2: 两阶段重排 ────────────────────────────────────────────────

    /// <summary>两阶段换码：直接互换会撞唯一索引，经 NULL 中转（腾空→占用→回填）可成功交换两码。</summary>
    [SpaceCoreRelationalFact]
    public void TwoPhaseReorder_SwapCodes_NullIntermediate_Succeeds()
    {
        Guid id1, id2;
        using (var ctx = NewContext())
        {
            var l1 = new Space_Location { LocationCode = "S-A", Status = 1 };
            var l2 = new Space_Location { LocationCode = "S-B", Status = 1 };
            ctx.Space_Locations.AddRange(l1, l2);
            ctx.SaveChanges();
            id1 = l1.Id;
            id2 = l2.Id;
        }

        // 目标：l1 ← "S-B"，l2 ← "S-A"。经 NULL 中转规避 "S-A"/"S-B" 唯一冲突。
        using (var ctx = NewContext())
        {
            var l1 = ctx.Space_Locations.Single(x => x.Id == id1);
            l1.LocationCode = null;          // 阶段一：腾空 "S-A"
            ctx.SaveChanges();

            var l2 = ctx.Space_Locations.Single(x => x.Id == id2);
            l2.LocationCode = "S-A";         // 阶段二：l2 占用刚腾空的 "S-A"（原 "S-B" 释放）
            ctx.SaveChanges();

            l1.LocationCode = "S-B";         // 阶段三：l1 回填 "S-B"
            ctx.SaveChanges();
        }

        using var verify = NewContext();
        Assert.Equal("S-B", verify.Space_Locations.Single(x => x.Id == id1).LocationCode);
        Assert.Equal("S-A", verify.Space_Locations.Single(x => x.Id == id2).LocationCode);
    }

    // ── D-9.3: RowVersion 并发测试 ────────────────────────────────────────

    /// <summary>两上下文并发改同一行：先写者提交后 rowversion 改变，后写者 WHERE RowVersion 命中 0 行 → DbUpdateConcurrencyException。</summary>
    [SpaceCoreRelationalFact]
    public void RowVersion_ConcurrentUpdate_SecondThrows()
    {
        Guid id;
        using (var seed = NewContext())
        {
            var loc = new Space_Location { LocationCode = "RV-001", Status = 1 };
            seed.Space_Locations.Add(loc);
            seed.SaveChanges();
            id = loc.Id;
        }

        // 两个独立上下文各自加载同一行（各持相同的初始 RowVersion 快照）
        using var ctxA = NewContext();
        using var ctxB = NewContext();
        var a = ctxA.Space_Locations.Single(x => x.Id == id);
        var b = ctxB.Space_Locations.Single(x => x.Id == id);

        a.Status = 2;
        ctxA.SaveChanges();   // 先写者成功，DB rowversion 递增

        b.Status = 0;
        // 后写者 UPDATE ... WHERE Id=@id AND RowVersion=@stale → 影响 0 行
        Assert.Throws<DbUpdateConcurrencyException>(() => ctxB.SaveChanges());
    }
}

public sealed class SpaceCoreRelationalFactAttribute : FactAttribute
{
    public SpaceCoreRelationalFactAttribute()
    {
        if (!Wp5ReportsFactAttribute.IsSelected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)))
            Skip = "Select the WP5 Core provider/connection or the legacy SQL Server fixture.";
    }
}
