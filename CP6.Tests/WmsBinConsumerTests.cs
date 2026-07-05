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
