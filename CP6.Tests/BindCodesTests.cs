using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// I-1 绑码测试：绑后 Placed/RackId/AbsX 填且 Code/Id/Version 不变；状态不合法→E-004。
/// </summary>
public class BindCodesTests
{
    private static (CP6Context db, SceneService svc) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var geo = new LocationGeometryService(db);
        var execution = new SpaceExecutionContextAccessor();
        execution.Push(SpaceExecutionContext.ForUser(
            TenantContext.DefaultTenant,
            "test-user",
            "Test User",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N")));
        var publish = new LocationPublishService(db, new TenantContext(), new CodeEngineService(db),
            new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer(), execution, execution),
            new StubWmsStockQuery(), new CP6.Core.Services.Wms.WmsBinDeactivator(db), new NoOpSpaceNotifier(),
            execution, execution);
        return (db, new SceneService(db, geo, publish));
    }

    [Fact]
    public async Task BindCodes_SetsPlacedRackIdAbsX_DoesNotChangeCodeIdVersion()
    {
        var (db, svc) = Make();

        var rackId  = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var locId   = Guid.NewGuid();
        const string locCode = "WH1-Z1-R001-01-02-01";

        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, FloorId = floorId, ZoneId = Guid.NewGuid(),
            RackCode = "R1", X = 1000, Y = 2000, Z = 0, RotationZ = 0,
            Cols = 3, Levels = 3, DepthCount = 1, CellW = 1200, CellH = 1500, CellD = 1000
        });
        // 采纳态待绑定：Status=1, !Placed, CodeOrigin=2
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, LocationCode = locCode,
            Status = 1, Placed = false, CodeOrigin = 2, Version = 5
        });
        await db.SaveChangesAsync();

        await svc.BindCodesAsync(rackId, new[] { (locId, 1, 2, 1) }, "u");

        var loc = await db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locId);
        Assert.NotNull(loc);
        Assert.True(loc!.Placed);
        Assert.Equal(rackId, loc.RackId);
        // AbsX = rack.X + (col - 0.5)*CellW = 1000 + 0.5*1200 = 1600
        Assert.Equal(1600, loc.AbsX);
        // Code / Id / Status / Version 不变
        Assert.Equal(locCode, loc.LocationCode);
        Assert.Equal(locId, loc.Id);
        Assert.Equal(1, loc.Status);
        Assert.Equal(5, loc.Version);
    }

    [Fact]
    public async Task BindCodes_DraftStatus_ThrowsE004()
    {
        var (db, svc) = Make();

        var rackId = Guid.NewGuid();
        var locId  = Guid.NewGuid();

        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(),
            RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        });
        // Status=0 (草稿) 不允许绑码
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, Status = 0, Placed = false, CodeOrigin = 2
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.BindCodesAsync(rackId, new[] { (locId, 1, 1, 1) }, "u"));
        Assert.Equal("E-SPACE-004", ex.Code);
    }

    [Fact]
    public async Task BindCodes_AlreadyPlaced_ThrowsE004()
    {
        var (db, svc) = Make();

        var rackId = Guid.NewGuid();
        var locId  = Guid.NewGuid();

        db.Space_Racks.Add(new Space_Rack
        {
            Id = rackId, ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(),
            RackCode = "R1", Cols = 1, Levels = 1, DepthCount = 1,
            CellW = 1000, CellH = 1000, CellD = 1000
        });
        // Already placed
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, Status = 1, Placed = true, CodeOrigin = 2
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.BindCodesAsync(rackId, new[] { (locId, 1, 1, 1) }, "u"));
        Assert.Equal("E-SPACE-004", ex.Code);
    }
}
