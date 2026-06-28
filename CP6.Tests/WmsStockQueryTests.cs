using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsStockQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IWmsStockQuery Stock(CP6Context db) => new WmsStockQuery(db);

    [Fact]
    public async Task GetStockQtyAsync_SumsPhysicalQty_Decimal()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "",
            PhysicalQty = 2.5m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P2", LotNo = "",
            PhysicalQty = 1.5m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        Assert.Equal(4.0m, await Stock(db).GetStockQtyAsync("A-01"));
        Assert.Equal(0m, await Stock(db).GetStockQtyAsync("NOPE"));
    }
}
