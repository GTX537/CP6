using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TopologyDeactivate_publication_is_stopped_before_final_remove(bool rack)
    {
        await using var h = await LocationPublishSeed();
        var db = h.Db;
        var source = await db.Space_Racks.AsNoTracking().SingleAsync(x => x.RackCode == "R01");
        var rows = await db.Space_Locations.Where(x => x.RackId == source.Id).ToArrayAsync();
        foreach (var row in rows) row.Status = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ids = rows.Select(x => x.Id).ToArray();
        var aisle = source.AisleId!.Value;
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_topology_stop_order() RETURNS trigger LANGUAGE plpgsql AS $order$
            BEGIN PERFORM pg_advisory_xact_lock(933661); RETURN NEW; END $order$;
            CREATE CONSTRAINT TRIGGER cp6_test_topology_stop_order AFTER INSERT ON public."T_IntegrationEvent"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."SourceModule"='SPACE')
            EXECUTE FUNCTION public.cp6_test_topology_stop_order();
            """, control);
        await install.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> publish, remove;
        FenceWait first, second;
        var kind = rack ? "rack" : "aisle";
        try
        {
            await using (var tx = await control.BeginTransactionAsync())
            {
                await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(933661)", control, tx);
                await hold.ExecuteNonQueryAsync();
                publish = host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(source.FloorId), new { zoneId = source.ZoneId }));
                first = await WaitForFence(observer, control.ProcessID, "%COMMIT%", null, publish);
                remove = host.Send(OperationsRequest(actor, HttpMethod.Delete, HierarchyDeletePath(rack, rack ? source.Id : aisle) + "?mode=deactivate"));
                second = await SerialNativeWait(observer, first.Backend, control.ProcessID, "topology-stop-" + kind, remove);
                await tx.RollbackAsync();
            }
            using var published = await publish;
            using var removed = await remove;
            var count = await PublishedCount(published);
            var remaining = await db.Space_Locations.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync();
            var bins = await db.WmsBins.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync();
            var events = await db.IntegrationEvents.AsNoTracking().Where(x => x.SourceModule == "SPACE").OrderBy(x => x.SourceNo).ToArrayAsync();
            var afterSeq = await SceneDeltaSequence(h);
            var rackExists = await db.Space_Racks.AnyAsync(x => x.Id == source.Id);
            var aisleExists = await db.Space_Aisles.AnyAsync(x => x.Id == aisle);
            var stockAfter = await PalletStockRows(h);
            await DeviceEvidence("topology-deactivate-publish-" + kind + ".json", new
            {
                Publish = (int)published.StatusCode, Delete = (int)removed.StatusCode, Count = count, first, second,
                RackExists = rackExists, AisleExists = aisleExists,
                Remaining = remaining.Select(x => new { x.Status, x.Version }).ToArray(),
                Bins = bins.Select(x => new { x.IsActive, x.Version }).ToArray(),
                Events = events.Select(x => new { x.SourceNo, x.Status }).ToArray(),
                SequenceBefore = seq, SequenceAfter = afterSeq, StockBefore = stock, StockAfter = stockAfter
            });
            Assert.Equal(ids.Length, count);
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(first.Backend, second.Blocker);
            Assert.Equal(rack, aisleExists);
            Assert.Equal(!rack, rackExists);
            if (rack)
            {
                Assert.Empty(remaining);
                Assert.Empty(bins);
            }
            else
            {
                Assert.Equal(ids.Length, remaining.Length);
                Assert.All(remaining, x => Assert.Equal(2, x.Status));
                Assert.All(bins, x => Assert.False(x.IsActive));
            }
            Assert.Equal(ids.Length + 1, events.Length);
            Assert.Equal(seq + ids.Length + 1, afterSeq);
            Assert.Equal(stock, stockAfter);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_topology_stop_order ON public."T_IntegrationEvent";
                DROP FUNCTION public.cp6_test_topology_stop_order();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
