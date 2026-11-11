using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record TopologyStop(Guid Rack, Guid Aisle, Guid First, Guid Second)
    {
        public string Path(bool rack) => HierarchyDeletePath(rack, rack ? Rack : Aisle) + "?mode=deactivate";
    }
    private sealed record TopologyItem(int? Status, long? Version, bool? Active, long? BinVersion);

    private async Task<(Harness Harness, TopologyStop Input)> TopologyDeactivateSeed(bool two = true, bool stocked = true)
    {
        var h = await HierarchySeed();
        var db = h.Db;
        var rack = await db.Space_Racks.AsNoTracking().SingleAsync(x => x.RackCode == "R01");
        db.Space_Locations.RemoveRange(await db.Space_Locations.Where(x => x.RackId == rack.Id).ToArrayAsync());
        await db.SaveChangesAsync();
        var suffix = h.Tenant.ToString("N")[..12];
        var first = Guid.Parse("00000001-0000-0000-0000-" + suffix);
        var second = Guid.Parse("00000002-0000-0000-0000-" + suffix);
        await Add(first, "PART-A", 1, 0m);
        if (two) await Add(second, "PART-B", 2, stocked ? 1.25m : 0m);
        db.ChangeTracker.Clear();
        return (h, new(rack.Id, rack.AisleId!.Value, first, second));

        async Task Add(Guid id, string code, int col, decimal quantity)
        {
            db.Space_Locations.Add(new() { Id = id, RackId = rack.Id, FloorId = rack.FloorId, Col = col, Level = 1, Depth = 1,
                Status = 1, Version = 3, CodeOrigin = 1, Placed = true, LocationCode = code });
            db.WmsBins.Add(new() { Id = id, LocationCode = code, WarehouseCd = "SIM", Version = 3, IsActive = true });
            db.Stocks.Add(new() { WarehouseCd = "SIM", LocationCd = code, ProductCd = "SIM-PART", LotNo = "LOT",
                PhysicalQty = quantity, AvailableQty = quantity });
            // Fixture insertion order and UUID prefixes both place the safe item first in the original native plan.
            await db.SaveChangesAsync();
        }
    }

    private static async Task<TopologyItem> TopologyItemState(Harness h, Guid id)
    {
        var row = await h.Db.Space_Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        var bin = await h.Db.WmsBins.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return new(row?.Status, row?.Version, bin?.IsActive, bin?.Version);
    }

    private static async Task<object> TopologyStopFinished(Harness h, TopologyStop input, bool rack, int seq, int completed)
    {
        Assert.Equal(rack, await h.Db.Space_Aisles.AnyAsync(x => x.Id == input.Aisle));
        if (rack)
        {
            Assert.False(await h.Db.Space_Racks.AnyAsync(x => x.Id == input.Rack));
            Assert.Equal(new(null, null, null, null), await TopologyItemState(h, input.First));
            if (completed == 2) Assert.Equal(new(null, null, null, null), await TopologyItemState(h, input.Second));
        }
        else
        {
            Assert.Null(await h.Db.Space_Racks.Where(x => x.Id == input.Rack).Select(x => x.AisleId).SingleAsync());
            Assert.Equal(new(2, 4, false, 4), await TopologyItemState(h, input.First));
            if (completed == 2) Assert.Equal(new(2, 4, false, 4), await TopologyItemState(h, input.Second));
        }
        var events = await h.Db.IntegrationEvents.AsNoTracking().Where(x => x.SourceModule == "SPACE").OrderBy(x => x.SourceNo).ToArrayAsync();
        Assert.Equal(completed, events.Length);
        Assert.All(events, e => Assert.Equal("SKIPPED", e.Status));
        Assert.Equal(seq + completed, await SceneDeltaSequence(h));
        return new { Rack = rack, Completed = completed, SequenceBefore = seq, SequenceAfter = await SceneDeltaSequence(h),
            Events = events.Select(x => new { x.SourceNo, x.Status }).ToArray(),
            First = await TopologyItemState(h, input.First), Second = completed == 2 ? await TopologyItemState(h, input.Second) : null };
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TopologyDeactivate_keeps_prior_success_when_the_next_location_has_stock(bool rack)
    {
        var seeded = await TopologyDeactivateSeed();
        await using var h = seeded.Harness;
        var input = seeded.Input;
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var actor = await Login(host, h);
        using var hub = await PublishHub.Connect(host);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var notification = hub.ReadNotice(deadline.Token);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        using var partial = await Send();
        var first = await TopologyItemState(h, input.First);
        var second = await TopologyItemState(h, input.Second);
        var afterSeq = await SceneDeltaSequence(h);
        var body = await partial.Content.ReadAsStringAsync();
        var stockAfterPartial = await PalletStockRows(h);
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Contains("E-SPACE-401", body);
        Assert.Equal(new(2, 4, false, 4), first);
        Assert.Equal(new(1, 3, true, 3), second);
        Assert.True(await h.Db.Space_Racks.AnyAsync(x => x.Id == input.Rack));
        Assert.True(await h.Db.Space_Aisles.AnyAsync(x => x.Id == input.Aisle));
        Assert.Equal(seq + 1, afterSeq);
        Assert.Equal(stock, stockAfterPartial);
        var firstNotice = await notification;
        var firstBatch = await h.Db.IntegrationEvents.Where(x => x.SourceModule == "SPACE").Select(x => x.SourceNo).SingleAsync();
        AssertPublishNotice(firstNotice, firstBatch, 1);
        // The test explicitly changes the second simulated stock before asking to continue.
        var simulatedStock = await h.Db.Stocks.SingleAsync(x => x.WarehouseCd == "SIM" && x.LocationCd == "PART-B");
        simulatedStock.PhysicalQty = simulatedStock.AvailableQty = 0m;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var resumeStock = await PalletStockRows(h);
        var resumedNotice = hub.ReadNotice(deadline.Token);
        using var resumed = await Send();
        Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
        var receipt = await TopologyStopFinished(h, input, rack, seq, 2);
        var finalNotice = await resumedNotice;
        Assert.Equal(1, finalNotice.GetProperty("count").GetInt32());
        Assert.NotEqual(firstBatch, finalNotice.GetProperty("batchNo").GetString());
        Assert.Equal(resumeStock, await PalletStockRows(h));
        await DeviceEvidence("topology-deactivate-partial-" + (rack ? "rack" : "aisle") + ".json",
            new { Status = (int)partial.StatusCode, First = first, Second = second, SequenceBefore = seq, SequenceAfter = afterSeq,
                StockBefore = stock, StockAfter = stockAfterPartial, FirstNotice = firstNotice, FinalNotice = finalNotice, Recovered = receipt });
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Delete, input.Path(rack)));
    }
}
