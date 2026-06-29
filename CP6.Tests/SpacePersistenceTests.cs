using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// Space P1 地基 schema 落库测试（ch00 A-3）。验证 9 表往返 + 真·多租户基建：
/// 不显式设 TenantId → SaveChanges 自动盖当前租户；查询经 CP6Context 全局过滤自动按租户隔离。
/// 直接构造 CP6Context（共享 InMemory 库名 + 注入不同 TenantContext），因 TestHelper 每调用生成独立库。
/// </summary>
public class SpacePersistenceTests
{
    private static CP6Context Ctx(string dbName, Guid tenant) =>
        new CP6Context(
            new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(dbName).Options,
            new TenantContext { CurrentTenantId = tenant });

    [Fact]
    public async Task Site_RoundTrips_AndStampsTenant()
    {
        var t = Guid.NewGuid();
        var db = Guid.NewGuid().ToString();

        // 不显式设 TenantId —— SaveChanges 自动盖当前租户
        using (var ctx = Ctx(db, t))
        {
            ctx.Space_Sites.Add(new Space_Site { SiteCode = "WH1", SiteName = "本社倉庫" });
            await ctx.SaveChangesAsync();
        }
        using (var ctx = Ctx(db, t))
        {
            var got = await ctx.Space_Sites.SingleAsync();   // 全局过滤自动按 t 限定，无显式 .Where
            Assert.Equal("WH1", got.SiteCode);
            Assert.Equal(t, got.TenantId);                   // 已自动盖章
        }
        // 另一个租户看不到（行级硬墙）
        using (var ctx = Ctx(db, Guid.NewGuid()))
            Assert.Empty(await ctx.Space_Sites.ToListAsync());
    }

    [Fact]
    public async Task AllNineTables_RoundTrip_AndStampTenant()
    {
        var t = Guid.NewGuid();
        var db = Guid.NewGuid().ToString();
        var rackId = Guid.NewGuid();

        using (var ctx = Ctx(db, t))
        {
            ctx.Space_Sites.Add(new Space_Site { SiteCode = "S", SiteName = "s" });
            ctx.Space_Floors.Add(new Space_Floor { FloorCode = "F", FloorName = "f" });
            ctx.Space_Zones.Add(new Space_Zone { ZoneCode = "Z", ZoneName = "z", Polygon = "[[0,0],[1,0],[1,1]]" });
            ctx.Space_Aisles.Add(new Space_Aisle { AisleCode = "L1" });
            ctx.Space_Racks.Add(new Space_Rack { Id = rackId, RackCode = "R", Cols = 2, Levels = 3, CellW = 1000, CellH = 1000, CellD = 1000 });
            ctx.Space_Locations.Add(new Space_Location { RackId = rackId, LocationCode = "R-01-01", Placed = true, Status = 1 });
            ctx.Space_Templates.Add(new Space_Template { TemplateCode = "T", TemplateName = "t" });
            ctx.Space_CodeRules.Add(new Space_CodeRule { RuleName = "default", Segments = "[]", IsDefault = true });
            ctx.Space_Markers.Add(new Space_Marker { Text = "正門" });
            await ctx.SaveChangesAsync();
        }
        using (var ctx = Ctx(db, t))
        {
            Assert.Equal(1, await ctx.Space_Sites.CountAsync());
            Assert.Equal(1, await ctx.Space_Floors.CountAsync());
            Assert.Equal(1, await ctx.Space_Zones.CountAsync());
            Assert.Equal(1, await ctx.Space_Aisles.CountAsync());
            Assert.Equal(1, await ctx.Space_Racks.CountAsync());
            Assert.Equal(1, await ctx.Space_Templates.CountAsync());
            Assert.Equal(1, await ctx.Space_CodeRules.CountAsync());
            Assert.Equal(1, await ctx.Space_Markers.CountAsync());

            var loc = await ctx.Space_Locations.SingleAsync();
            Assert.Equal("R-01-01", loc.LocationCode);
            Assert.Equal(rackId, loc.RackId);
            Assert.Equal(t, loc.TenantId);   // 盖章贯穿全 9 表
        }
    }
}
