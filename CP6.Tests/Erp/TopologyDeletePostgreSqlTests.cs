using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static string HierarchyDeletePath(bool rack, Guid id) => $"api/space/{(rack ? "rack" : "aisle")}/{id}";

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyTopologyDelete_current_publication_prevents_default_delete(bool rack)
    {
        await using var h = await LocationPublishSeed();
        var db = h.Db;
        var source = await db.Space_Racks.AsNoTracking().SingleAsync(x => x.RackCode == "R01");
        var locations = await db.Space_Locations.Where(x => x.RackId == source.Id).ToArrayAsync();
        foreach (var row in locations) row.Status = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ids = locations.Select(x => x.Id).ToArray();
        var oldAisle = source.AisleId!.Value;
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_delete_publish_order() RETURNS trigger LANGUAGE plpgsql AS $order$
            BEGIN PERFORM pg_advisory_xact_lock(929651); RETURN NEW; END $order$;
            CREATE CONSTRAINT TRIGGER cp6_test_delete_publish_order AFTER INSERT ON public."T_IntegrationEvent"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."SourceModule"='SPACE')
            EXECUTE FUNCTION public.cp6_test_delete_publish_order();
            """, control);
        await install.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> publish, remove;
        FenceWait first, second;
        var suffix = rack ? "rack" : "aisle";
        try
        {
            await using (var tx = await control.BeginTransactionAsync())
            {
                await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(929651)", control, tx);
                await hold.ExecuteNonQueryAsync();
                publish = host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(source.FloorId), new { zoneId = source.ZoneId }));
                first = await WaitForFence(observer, control.ProcessID, "%COMMIT%", null, publish);
                remove = host.Send(OperationsRequest(actor, HttpMethod.Delete, HierarchyDeletePath(rack, rack ? source.Id : oldAisle)));
                second = await SerialNativeWait(observer, first.Backend, control.ProcessID, "topology-delete-" + suffix, remove);
                await tx.RollbackAsync();
            }
            using var released = await publish;
            using var deleted = await remove;
            var count = await PublishedCount(released);
            var body = await deleted.Content.ReadAsStringAsync();
            var originalGuard = rack ? "E-SPACE-403" : "E-SPACE-402";
            var sourceStill = await db.Space_Racks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == source.Id);
            var aisleStill = await db.Space_Aisles.AnyAsync(x => x.Id == oldAisle);
            var live = await db.Space_Locations.AsNoTracking().Where(x => ids.Contains(x.Id) && x.Status == 1).OrderBy(x => x.Id).ToArrayAsync();
            var bins = await db.WmsBins.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).OrderBy(x => x.Id).ToArrayAsync();
            var stockAfter = await PalletStockRows(h);
            var guard = body.Contains(originalGuard, StringComparison.Ordinal);
            await DeviceEvidence("legacy-topology-default-" + suffix + ".json", new
            {
                Publish = (int)released.StatusCode, Delete = (int)deleted.StatusCode, count, Guard = guard, first, second,
                RackPreserved = sourceStill is not null, AislePreserved = aisleStill,
                AttachmentPreserved = sourceStill?.AisleId == oldAisle, Live = live.Length, Bins = bins.Length,
                StockBefore = stock, StockAfter = stockAfter
            });
            Assert.Equal(ids.Length, count);
            Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
            Assert.True(guard, body);
            Assert.True(aisleStill);
            Assert.Equal(oldAisle, sourceStill?.AisleId);
            Assert.Equal(ids.Length, live.Length);
            Assert.Equal(live.Select(x => (x.Id, x.LocationCode, x.Version)), bins.Select(x => (x.Id, (string?)x.LocationCode, x.Version)));
            Assert.Equal(stock, stockAfter);
            Assert.Equal(first.Backend, second.Blocker);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_delete_publish_order ON public."T_IntegrationEvent";
                DROP FUNCTION public.cp6_test_delete_publish_order();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
