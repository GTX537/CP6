using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task LegacyPublish_zone_scope_and_empty_batch_keep_the_public_contract()
    {
        await using var h = await LocationPublishSeed(otherReady: false);
        await using var other = await LocationPublishSeed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        using var hub = await PublishHub.Connect(host);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var actor = await Login(host, h);
        var floor = await SceneDeltaFloor(h);
        var zone = await PublishZone(h);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        var otherBefore = await SceneDeltaRows(other);
        var notification = hub.ReadNotice(deadline.Token);
        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { zoneId = zone }));
        Assert.Equal(1, await PublishedCount(response));
        var notice = await notification;
        var batch = await AssertLegacyPublished(h, 1, seq);
        AssertPublishNotice(notice, batch, 1);
        Assert.Equal(1, await h.Db.Space_Locations.CountAsync(x => x.LocationCode == null && x.Status == 0 && x.Version == 0));
        Assert.Equal("OLD-1", await h.Db.Space_Locations.Where(x => x.Status == 1 && x.LocationCode != "FROZEN").Select(x => x.LocationCode).SingleAsync());
        var beforeEmpty = await SceneDeltaRows(h);
        using var empty = await host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { zoneId = zone }));
        Assert.Equal(0, await PublishedCount(empty));
        using var missing = await host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(Guid.NewGuid()), new { }));
        Assert.Equal(0, await PublishedCount(missing));
        using var noNotice = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.ReadNotice(noNotice.Token));
        var otherAfter = await SceneDeltaRows(other);
        var stockAfter = await PalletStockRows(h);
        Assert.Equal(otherBefore, otherAfter);
        Assert.Equal(stock, stockAfter);
        Assert.Equal(beforeEmpty, await SceneDeltaRows(h));
        Assert.Equal(seq + 1, await SceneDeltaSequence(h));
        await DeviceEvidence("legacy-publish-normal.json", new { Status = (int)response.StatusCode, Batch = batch,
            Notice = notice, Published = 1, Empty = 0, Missing = 0, NoEmptyNotice = true, StockBefore = stock,
            StockAfter = stockAfter, OtherBefore = otherBefore, OtherAfter = otherAfter });
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPublish_failed_commit_never_signals_success_and_has_explicit_outcome()
    {
        await using var h = await LocationPublishSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        using var hub = await PublishHub.Connect(host);
        var actor = await Login(host, h);
        var floor = await SceneDeltaFloor(h);
        var zone = await PublishZone(h);
        var before = await SceneDeltaRows(h);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_publish_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN
                PERFORM pg_advisory_xact_lock(924621);
                RAISE EXCEPTION 'simulated legacy publication commit failure';
            END $fault$;
            CREATE CONSTRAINT TRIGGER cp6_test_publish_commit_failure AFTER INSERT ON public."T_IntegrationEvent"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."SourceModule"='SPACE')
            EXECUTE FUNCTION public.cp6_test_publish_commit_failure();
            """, control);
        await install.ExecuteNonQueryAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var notification = hub.ReadNotice(deadline.Token);
        int status;
        string body, after, whileHeld;
        int afterSeq;
        FenceWait wait;
        bool noticePending;
        try
        {
            Task<HttpResponseMessage> pending;
            await using (var tx = await control.BeginTransactionAsync())
            {
                await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(924621)", control, tx);
                await hold.ExecuteNonQueryAsync();
                pending = host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { zoneId = zone }));
                wait = await WaitForFence(observer, control.ProcessID, "%COMMIT%", null, pending);
                whileHeld = await SceneDeltaRows(h);
                noticePending = !notification.IsCompleted;
                await tx.RollbackAsync();
            }
            using var failed = await pending;
            status = (int)failed.StatusCode;
            body = await failed.Content.ReadAsStringAsync();
            after = await SceneDeltaRows(h);
            afterSeq = await SceneDeltaSequence(h);
            Assert.Equal(before, whileHeld);
            Assert.Equal(before, after);
            Assert.Equal(seq, afterSeq);
            Assert.Equal(stock, await PalletStockRows(h));
            Assert.True(noticePending);
            Assert.False(notification.IsCompleted);
        }
        finally
        {
            await using var remove = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_publish_commit_failure ON public."T_IntegrationEvent";
                DROP FUNCTION public.cp6_test_publish_commit_failure();
                """, control);
            await remove.ExecuteNonQueryAsync();
        }
        // Read back the whole local write set before starting a new, differently scoped request.
        using var recovered = await host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { }));
        Assert.Equal(2, await PublishedCount(recovered));
        var notice = await notification;
        var batch = await AssertLegacyPublished(h, 2, seq);
        AssertPublishNotice(notice, batch, 2);
        var stockAfter = await PalletStockRows(h);
        Assert.Equal(stock, stockAfter);
        var unknown = body.Contains("SPACE_PUBLISH_OUTCOME_UNKNOWN", StringComparison.Ordinal);
        await DeviceEvidence("legacy-publish-commit.json", new { Status = status, Unknown = unknown, Wait = wait, NoticePending = noticePending,
            Before = before, WhileHeld = whileHeld, After = after, SequenceBefore = seq, SequenceAfter = afterSeq,
            StockBefore = stock, StockAfter = stockAfter, RecoveredBatch = batch, RecoveredNotice = notice });
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, status);
        Assert.True(unknown);
    }

    private static void AssertPublishNotice(JsonElement notice, string batch, int count)
    {
        Assert.Equal(batch, notice.GetProperty("batchNo").GetString());
        Assert.Equal(count, notice.GetProperty("count").GetInt32());
        Assert.Equal("SUCCESS", notice.GetProperty("status").GetString());
    }
}
