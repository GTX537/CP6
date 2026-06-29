using CP6.Core.EFDbContext;
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "b" }, "u"));
        Assert.Equal("E-SPACE-501", ex.Message);
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
}
