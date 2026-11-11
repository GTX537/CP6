using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record TopologyMove(bool Rack, Guid Source, Guid Target, Guid RackId, Guid AisleId,
        Guid Location, Guid FloorId, int Sequence, long Version)
    {
        public string Path => HierarchyDeletePath(Rack, Source) + (Rack ? "?mode=rehome&targetRackId=" : "?mode=rehome&targetAisleId=") + Target;
    }

    private static async Task<TopologyMove> PrepareTopologyMove(Harness h, bool rack)
    {
        var db = h.Db;
        var source = await db.Space_Racks.AsNoTracking().SingleAsync(x => x.RackCode == "R01");
        var location = await db.Space_Locations.AsNoTracking().SingleAsync(x => x.LocationCode == "FROZEN");
        var target = Guid.NewGuid();
        if (rack)
            db.Space_Racks.Add(new() { Id = target, FloorId = source.FloorId, ZoneId = source.ZoneId, AisleId = source.AisleId,
                RackCode = "MOVE-R", X = 410, Y = 20, Z = 30, Cols = source.Cols, Levels = source.Levels, DepthCount = source.DepthCount,
                CellW = source.CellW, CellH = source.CellH, CellD = source.CellD });
        else
            db.Space_Aisles.Add(new() { Id = target, ZoneId = source.ZoneId, AisleCode = "MOVE-A", Polygon = HierarchyPolygon,
                Centerline = "[[0,0],[1200,0]]" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(rack, rack ? source.Id : source.AisleId!.Value, target, source.Id, source.AisleId!.Value,
            location.Id, source.FloorId, await SceneDeltaSequence(h), location.Version);
    }

    private static async Task<object> ReadTopologyMove(Harness h, TopologyMove input)
    {
        var db = h.Db;
        var destinationRack = input.Rack ? input.Target : input.RackId;
        var destinationAisle = input.Rack ? input.AisleId : input.Target;
        var row = await db.Space_Locations.AsNoTracking().SingleAsync(x => x.Id == input.Location);
        var rack = await db.Space_Racks.AsNoTracking().SingleAsync(x => x.Id == destinationRack);
        var bin = await db.WmsBins.AsNoTracking().SingleAsync(x => x.Id == input.Location);
        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceModule == "SPACE" && x.TargetModule == "WMS");
        var path = JsonSerializer.Deserialize<LocationPath>(bin.PathJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var absent = input.Rack ? !await db.Space_Racks.AnyAsync(x => x.Id == input.Source) : !await db.Space_Aisles.AnyAsync(x => x.Id == input.Source);
        var seq = await SceneDeltaSequence(h);
        Assert.True(absent);
        Assert.Equal(destinationAisle, rack.AisleId);
        Assert.Equal(destinationRack, row.RackId);
        Assert.Equal(input.FloorId, row.FloorId);
        Assert.Equal("FROZEN", row.LocationCode);
        Assert.Equal(1, row.Status);
        Assert.Equal(input.Rack ? 2910 : 2510, row.AbsX);
        Assert.Equal(input.Version + 1, row.Version);
        Assert.Equal(row.Version, bin.Version);
        Assert.True(bin.IsActive);
        Assert.Equal("FROZEN", bin.LocationCode);
        Assert.Equal("SIM", bin.WarehouseCd);
        Assert.Equal(input.Rack ? "MOVE-R" : "R01", path.RackCode);
        Assert.Equal(input.Rack ? "A1" : "MOVE-A", path.AisleCode);
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(h.Tenant, evt.TenantId);
        Assert.Equal(input.Sequence + 1, seq);
        Assert.Equal(2, await db.Space_Locations.CountAsync(x => x.RackId == destinationRack && x.Status == 0));
        return new { Absent = absent, row.AbsX, row.LocationCode, LocationVersion = row.Version, BinVersion = bin.Version,
            BinActive = bin.IsActive, bin.WarehouseCd, path.RackCode, path.AisleCode, Event = evt.Status,
            Batch = evt.SourceNo, SequenceBefore = input.Sequence, SequenceAfter = seq };
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyTopologyDelete_real_rehome_keeps_codes_and_commits_projection(bool rack)
    {
        await using var h = await HierarchySeed();
        await using var other = await HierarchySeed();
        var input = await PrepareTopologyMove(h, rack);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var actor = await Login(host, h);
        var stock = await PalletStockRows(h);
        var otherBefore = await SceneDeltaRows(other);
        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Delete, input.Path));
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        var receipt = await ReadTopologyMove(h, input);
        var stockAfter = await PalletStockRows(h);
        var otherAfter = await SceneDeltaRows(other);
        Assert.Equal(stock, stockAfter);
        Assert.Equal(otherBefore, otherAfter);
        await DeviceEvidence("legacy-topology-rehome-" + (rack ? "rack" : "aisle") + ".json",
            new { Status = (int)result.StatusCode, Receipt = receipt, StockBefore = stock, StockAfter = stockAfter, OtherBefore = otherBefore, OtherAfter = otherAfter });
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyTopologyDelete_rehome_commit_failure_is_explicit_and_observed_separately()
    {
        await using var h = await HierarchySeed();
        var input = await PrepareTopologyMove(h, rack: true);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var before = await SceneDeltaRows(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_topology_rehome_fault() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated topology rehome commit failure'; END $fault$;
            CREATE CONSTRAINT TRIGGER cp6_test_topology_rehome_fault AFTER INSERT ON public."T_IntegrationEvent"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."SourceModule"='SPACE')
            EXECUTE FUNCTION public.cp6_test_topology_rehome_fault();
            """, control);
        await install.ExecuteNonQueryAsync();
        int status, seq;
        string after;
        bool unknown;
        try
        {
            using var failed = await Send();
            status = (int)failed.StatusCode;
            unknown = (await failed.Content.ReadAsStringAsync()).Contains("SPACE_TOPOLOGY_OUTCOME_UNKNOWN", StringComparison.Ordinal);
            after = await SceneDeltaRows(h);
            seq = await SceneDeltaSequence(h);
            Assert.Equal(before, after);
            Assert.Equal(input.Sequence, seq);
            Assert.Equal(stock, await PalletStockRows(h));
        }
        finally
        {
            await using var remove = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_topology_rehome_fault ON public."T_IntegrationEvent";
                DROP FUNCTION public.cp6_test_topology_rehome_fault();
                """, control);
            await remove.ExecuteNonQueryAsync();
        }
        using var recovered = await Send();
        Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
        var receipt = await ReadTopologyMove(h, input);
        var stockAfter = await PalletStockRows(h);
        Assert.Equal(stock, stockAfter);
        await DeviceEvidence("legacy-topology-rehome-commit.json", new { Status = status, Unknown = unknown, Before = before, After = after,
            SequenceBefore = input.Sequence, SequenceAfter = seq, StockBefore = stock, StockAfter = stockAfter, Recovered = receipt });
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, status);
        Assert.True(unknown);
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Delete, input.Path));
    }
}
