using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Space;

public class ConnectorServiceTests
{
    private static (CP6Context, ConnectorService) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new ConnectorService(db));
    }

    [Fact]
    public async Task Create_then_AddStops_then_ListBySite_returns_connector_with_stops()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "电梯1" }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 500, Y = 500 }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f2, X = 500, Y = 500 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Single(list);
        Assert.Equal("E1", list[0].ConnectorCode);
        Assert.Equal(2, list[0].Stops.Count);
    }

    [Fact]
    public async Task Create_DuplicateCode_same_site_throws_E501()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "b" }, "u"));
        Assert.Equal("E-SPACE-501", ex.Code);
    }

    [Fact]
    public async Task UpsertStop_same_floor_twice_updates_not_duplicates()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid(); var f1 = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 100, Y = 100 }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 200, Y = 200 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Single(list[0].Stops);
        Assert.Equal(200, list[0].Stops[0].X);
    }

    [Fact]
    public async Task Create_with_no_cost_applies_type_default_elevator()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "电梯" }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal(20, list[0].WaitSec);
        Assert.Equal(6, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Create_with_explicit_cost_not_overridden()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "S1", ConnectorType = 2, Name = "楼梯", WaitSec = 5, TravelSecPerFloor = 30 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal(5, list[0].WaitSec);
        Assert.Equal(30, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Update_changes_cost_name_type()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        await svc.UpdateAsync(cid, new ConnectorUpdateDto { Name = "b", ConnectorType = 3, WaitSec = 0, TravelSecPerFloor = 9 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal("b", list[0].Name);
        Assert.Equal(3, list[0].ConnectorType);
        Assert.Equal(9, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Update_missing_throws_E502()
    {
        var (_, svc) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(
            () => svc.UpdateAsync(Guid.NewGuid(), new ConnectorUpdateDto { Name = "x", ConnectorType = 1 }, "u"));
        Assert.Equal("E-SPACE-502", ex.Code);
    }
}
