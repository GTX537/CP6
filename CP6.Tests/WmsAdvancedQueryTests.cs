using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsAdvancedQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedOrderAsync(CP6Context db, string ob, int status,
        params (int line, string code, decimal qty, string product)[] lines)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = ob, WarehouseCd = "W1", Status = status });
        foreach (var (line, code, qty, product) in lines)
            db.OutboundOrderDetails.Add(new OutboundOrderDetail
            {
                OutboundNo = ob, LineNo = line, ProductCd = product, LocationCd = code,
                RequiredQty = qty, AllocatedQty = qty, ShippedQty = 0m,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPickPath_OrdersByLineNo()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "OB-1", 3,
            (3, "A-03", 1m, "P3"), (1, "A-01", 5m, "P1"), (2, "A-02", 2m, "P2"));

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-1");

        Assert.Equal("OB-1", path.TaskNo);
        Assert.Equal(3, path.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, path.Items.Select(i => i.Seq).ToArray());
        Assert.Equal("A-01", path.Items[0].LocationCode);
        Assert.Equal(5m, path.Items[0].Qty);
        Assert.Equal("P1", path.Items[0].MaterialNo);
    }

    [Fact]
    public async Task GetPickPath_SkipsNullLocationLines()
    {
        using var db = NewDb();
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OB-2", WarehouseCd = "W1", Status = 3 });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 1, ProductCd = "P1", LocationCd = "A-01", RequiredQty = 1m });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 2, ProductCd = "P2", LocationCd = null, RequiredQty = 1m });
        await db.SaveChangesAsync();

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-2");
        Assert.Equal("A-01", Assert.Single(path.Items).LocationCode);
    }

    [Fact]
    public async Task GetPickPath_UnknownOrder_EmptyItems()
    {
        using var db = NewDb();
        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("NOPE");
        Assert.Equal("NOPE", path.TaskNo);
        Assert.Empty(path.Items);
    }
}
