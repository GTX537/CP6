using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Space;
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

    // 波5 终审守卫：消费端 bin==null 分支现要求对应 Space_Location 行仍在，否则拒绝复活锚（SKIPPED）。
    // 既有用例本就在测「库位存在」的语义，故补种 Space_Location 行（只补数据，断言不动）。
    private static async Task SeedLoc(CP6Context db, params Guid[] ids)
    {
        foreach (var id in ids)
            db.Space_Locations.Add(new Space_Location { Id = id, LocationCode = "SEED", Status = 1 });
        await db.SaveChangesAsync();
    }

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
        await SeedLoc(db, id);

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
        await SeedLoc(db, id);
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
        await SeedLoc(db, id);
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
    public async Task Upsert_AnchorCollision_DifferentLocationId_Rejected()
    {
        // 终审 #3：同 (WarehouseCd, LocationCode) 锚被不同 LocationId 占用 → 走业务拒绝链，
        // 而非 Add 撞唯一索引走异常毒化链。
        using var db = Db();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        await SeedLoc(db, idA, idB);
        await new WmsBinConsumer(db).ConsumeAsync(Batch(Upsert(idA, "A-01-01-01", 1)));   // bin A 占住锚

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(Upsert(idB, "A-01-01-01", 1)));

        Assert.False(r.Success);
        Assert.Equal("REJECTED", r.Items.Single().Status);
        Assert.Equal(1, await db.WmsBins.CountAsync());          // 仍只有 A，B 未落库
        Assert.Equal(idA, (await db.WmsBins.SingleAsync()).Id);
    }

    [Fact]
    public async Task Upsert_SameBatch_DuplicateLocationId_NoDoubleAdd()
    {
        // 终审 #3：FindAsync/Local 双查兜掉批内同 LocationId 双 Add——第二条按已见 Added 实体走版本门，
        // 不抛、行数=1。
        using var db = Db();
        var id = Guid.NewGuid();
        await SeedLoc(db, id);

        var r = await new WmsBinConsumer(db).ConsumeAsync(
            Batch(Upsert(id, "A-01-01-01", 1), Upsert(id, "A-01-01-01", 1)));

        Assert.Equal(2, r.Items.Count);
        Assert.Equal("UPSERTED", r.Items[0].Status);
        Assert.Equal("SKIPPED", r.Items[1].Status);              // 同版本走幂等门
        Assert.Equal(1, await db.WmsBins.CountAsync());
    }

    [Fact]
    public void DetachOwnWrites_DetachesAddedWmsBins()
    {
        // 终审 #2：InMemory 难以可靠诱发 SaveChanges 持久失败（Local 双查已消除同批双 Add 诱发路径），
        // 故直接锁定 detach 语义——Add 一个 WmsBin 后调清理方法，断言 Entry.State==Detached。
        using var db = Db();
        var consumer = new WmsBinConsumer(db);
        var bin = new WmsBin { Id = Guid.NewGuid(), LocationCode = "A-01-01-01", WarehouseCd = "WH1", Version = 1 };
        db.WmsBins.Add(bin);
        Assert.Equal(EntityState.Added, db.Entry(bin).State);

        consumer.DetachOwnWrites();

        Assert.Equal(EntityState.Detached, db.Entry(bin).State);
    }

    [Fact]
    public async Task Deactivate_NoBin_CreatesTombstone()
    {
        // H6 乱序防护（对契约 §5.1 的修正）：bin 不存在的 DEACTIVATE 落墓碑而非跳过，
        // 否则迟到重试的旧版 UPSERT 会复活已停用库位。
        using var db = Db();
        var id = Guid.NewGuid();
        await SeedLoc(db, id);
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
        var id = Guid.NewGuid();
        await SeedLoc(db, id);   // 库位存在（采纳态无楼层）→ 守卫放行，落到「缺 WarehouseCd」幂等跳过
        var item = Upsert(id, "A-01-01-01", 2, warehouseCd: null);
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
        await SeedLoc(db, id);
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
        await SeedLoc(db, id);
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
    public async Task MixedBatch_TwoUpsertOneDeactivate_EquivalentToPerItem()
    {
        // 波5 批量化等价守卫：一次混合批（UPSERT×2 + DEACTIVATE×1，含 REJECTED）的结果，
        // 必须与逐条单项批（各自独立 ConsumeAsync）的最终 bin 状态/Version/逐项 Status 完全一致。
        // 覆盖三处预载：按 Id 命中（B/A 已存在）、库存 GroupBy（A 锚有 5 库存→REJECTED）、锚查（C 新建）。
        var idA = Guid.NewGuid();   // 已存在 v1，将被 DEACTIVATE，但锚上有库存 → REJECTED
        var idB = Guid.NewGuid();   // 已存在 v1，将被 UPSERT 到 v2
        var idC = Guid.NewGuid();   // 全新，UPSERT 新建

        async Task Seed(CP6Context db)
        {
            await SeedLoc(db, idA, idB, idC);   // idC 在批中新建 UPSERT，其库位须存在方能建 bin
            var c = new WmsBinConsumer(db);
            await c.ConsumeAsync(Batch(Upsert(idA, "A-01-01-01", 1)));
            await c.ConsumeAsync(Batch(Upsert(idB, "B-01-01-01", 1)));
            db.Stocks.Add(new Stock
            {
                Id = Guid.NewGuid(), WarehouseCd = "WH1", LocationCd = "A-01-01-01",
                ProductCd = "P1", LotNo = "", PhysicalQty = 5m
            });
            await db.SaveChangesAsync();
        }

        LocationPublishItem Deact(Guid id, string code, long v)
        {
            var it = Upsert(id, code, v);
            it.Op = "DEACTIVATE";
            return it;
        }

        // --- 批量路径：三项一次消费 ---
        using var batched = Db();
        await Seed(batched);
        var rBatch = await new WmsBinConsumer(batched).ConsumeAsync(Batch(
            Upsert(idB, "B-01-01-01", 2),         // UPSERT 升版
            Upsert(idC, "C-01-01-01", 1),         // UPSERT 新建
            Deact(idA, "A-01-01-01", 2)));        // DEACTIVATE 被库存拦 → REJECTED

        // --- 逐条路径：同三项各自独立消费 ---
        using var perItem = Db();
        await Seed(perItem);
        var pc = new WmsBinConsumer(perItem);
        var p1 = await pc.ConsumeAsync(Batch(Upsert(idB, "B-01-01-01", 2)));
        var p2 = await pc.ConsumeAsync(Batch(Upsert(idC, "C-01-01-01", 1)));
        var p3 = await pc.ConsumeAsync(Batch(Deact(idA, "A-01-01-01", 2)));

        // 逐项 Status 一致（批内顺序 = B,C,A）
        Assert.Equal("UPSERTED", rBatch.Items[0].Status);
        Assert.Equal("UPSERTED", rBatch.Items[1].Status);
        Assert.Equal("REJECTED", rBatch.Items[2].Status);
        Assert.Equal(p1.Items.Single().Status, rBatch.Items[0].Status);
        Assert.Equal(p2.Items.Single().Status, rBatch.Items[1].Status);
        Assert.Equal(p3.Items.Single().Status, rBatch.Items[2].Status);

        // 整批 Success：含 REJECTED → false（逐条中 A 项批也 false）
        Assert.False(rBatch.Success);
        Assert.False(p3.Success);

        // 最终 bin 状态/Version 两路径逐一相等
        async Task AssertBin(Guid id, bool expectActive, long expectVersion)
        {
            var b = await batched.WmsBins.SingleAsync(x => x.Id == id);
            var p = await perItem.WmsBins.SingleAsync(x => x.Id == id);
            Assert.Equal(expectActive, b.IsActive);
            Assert.Equal(expectVersion, b.Version);
            Assert.Equal(p.IsActive, b.IsActive);
            Assert.Equal(p.Version, b.Version);
        }
        await AssertBin(idB, expectActive: true, expectVersion: 2);   // 升版成功
        await AssertBin(idC, expectActive: true, expectVersion: 1);   // 新建
        await AssertBin(idA, expectActive: true, expectVersion: 1);   // REJECTED：仍 active、版本不动
        Assert.Equal(3, await batched.WmsBins.CountAsync());
        Assert.Equal(await perItem.WmsBins.CountAsync(), await batched.WmsBins.CountAsync());
    }

    [Fact]
    public async Task Deactivate_NoStock_SetsInactive_AndVersion()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        await SeedLoc(db, id);
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

    [Fact]
    public async Task Consume_LocationDeleted_NoBin_RefusesToReviveAnchor()
    {
        // 波5 终审守卫：库位已删除（Space_Location 无行）+ 无 bin → bin==null 两分支拒绝复活锚。
        // 窗口：库位事件 Failed → 重试期间用户删该停用位（T6 清墓碑 + Space_Location 硬删）→ 迟到重试到达。
        // DEACTIVATE 不得重建孤儿墓碑；UPSERT 更不得重建 IsActive=true 幻影 bin。
        using var db = Db();
        var idDeact = Guid.NewGuid();
        var idUpsert = Guid.NewGuid();
        var deact = Upsert(idDeact, "A-01-01-01", 2);
        deact.Op = "DEACTIVATE";
        var upsert = Upsert(idUpsert, "B-01-01-01", 2);

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(deact, upsert));

        Assert.Equal(2, r.Items.Count);
        Assert.All(r.Items, i => Assert.Equal("SKIPPED", i.Status));
        Assert.All(r.Items, i => Assert.Contains("库位已删除", i.Reason));
        Assert.Equal(0, await db.WmsBins.CountAsync());   // 零新行：孤儿墓碑/幻影 bin 都未落库
        Assert.True(r.Success);                            // 无 REJECTED，纯 SKIPPED 收敛
        Assert.True(r.AllSkipped);
    }

    [Fact]
    public async Task Consume_LocationExists_NoBin_OriginalSemanticsPreserved()
    {
        // 对照组：库位存在（Space_Location 有行）+ 无 bin → 守卫放行，原语义不变
        //（DEACTIVATE 落墓碑 / UPSERT 建活跃 bin）。
        using var db = Db();
        var idDeact = Guid.NewGuid();
        var idUpsert = Guid.NewGuid();
        await SeedLoc(db, idDeact, idUpsert);
        var deact = Upsert(idDeact, "A-01-01-01", 2);
        deact.Op = "DEACTIVATE";
        var upsert = Upsert(idUpsert, "B-01-01-01", 2);

        var r = await new WmsBinConsumer(db).ConsumeAsync(Batch(deact, upsert));

        Assert.Equal("DEACTIVATED", r.Items.Single(i => i.LocationId == idDeact).Status);
        Assert.Equal("UPSERTED", r.Items.Single(i => i.LocationId == idUpsert).Status);
        Assert.False((await db.WmsBins.SingleAsync(b => b.Id == idDeact)).IsActive);   // 墓碑落库
        Assert.True((await db.WmsBins.SingleAsync(b => b.Id == idUpsert)).IsActive);    // 活跃 bin 建
    }
}
