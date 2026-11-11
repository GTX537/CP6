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
    public async Task LegacyDeactivate_local_failures_keep_both_sides_and_do_not_notify(bool deferred)
    {
        await using var h = await LocationDeactivateSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        using var hub = await PublishHub.Connect(host);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var notification = hub.ReadNotice(deadline.Token);
        var location = await SceneIngressLocation(h, "FROZEN");
        var seq = await SceneDeltaSequence(h);
        var before = await SceneDeltaRows(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await control.OpenAsync();
        var trigger = deferred
            ? """CREATE CONSTRAINT TRIGGER cp6_test_deactivate_fault AFTER UPDATE ON public."Space_Location" DEFERRABLE INITIALLY DEFERRED"""
            : """CREATE TRIGGER cp6_test_deactivate_fault BEFORE UPDATE ON public."Space_Location" """;
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_deactivate_fault() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated standalone deactivation failure'; END $fault$;
            {{trigger}}
            FOR EACH ROW WHEN (NEW."Id"='{{location}}' AND NEW."Status"=2)
            EXECUTE FUNCTION public.cp6_test_deactivate_fault();
            """, control);
        await install.ExecuteNonQueryAsync();
        int status, afterSeq, afterLocationStatus;
        long afterLocationVersion, afterBinVersion;
        string body, after;
        bool afterBinActive;
        try
        {
            using var failed = await Send();
            status = (int)failed.StatusCode;
            body = await failed.Content.ReadAsStringAsync();
            after = await SceneDeltaRows(h);
            afterSeq = await SceneDeltaSequence(h);
            var row = await h.Db.Space_Locations.AsNoTracking().SingleAsync(x => x.Id == location);
            var bin = await h.Db.WmsBins.AsNoTracking().SingleAsync(x => x.Id == location);
            (afterLocationStatus, afterLocationVersion, afterBinActive, afterBinVersion) = (row.Status, row.Version, bin.IsActive, bin.Version);
            Assert.Equal(1, afterLocationStatus);
            Assert.Equal(3, afterLocationVersion);
            Assert.Equal(seq, afterSeq);
            Assert.Equal(stock, await PalletStockRows(h));
            Assert.False(notification.IsCompleted);
        }
        finally
        {
            await using var remove = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_deactivate_fault ON public."Space_Location";
                DROP FUNCTION public.cp6_test_deactivate_fault();
                """, control);
            await remove.ExecuteNonQueryAsync();
        }
        // Actual readback is complete; the fixture can remove the fault and attempt the original command again.
        using var recovered = await Send();
        Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
        var receipt = await DeactivationReceipt(h, location, seq);
        var notice = await notification;
        AssertPublishNotice(notice, receipt.Batch, 1);
        var stockAfter = await PalletStockRows(h);
        Assert.Equal(stock, stockAfter);
        var unknown = body.Contains("SPACE_DEACTIVATE_OUTCOME_UNKNOWN", StringComparison.Ordinal);
        var unavailable = body.Contains("SPACE_DEACTIVATE_AUTHORITY_UNAVAILABLE", StringComparison.Ordinal);
        await DeviceEvidence("legacy-deactivate-" + (deferred ? "commit" : "write") + ".json", new
        {
            Status = status, Deferred = deferred, Unknown = unknown, Unavailable = unavailable, Before = before, After = after,
            SequenceBefore = seq, SequenceAfter = afterSeq, LocationStatus = afterLocationStatus, LocationVersion = afterLocationVersion,
            BinActive = afterBinActive, BinVersion = afterBinVersion, StockBefore = stock, StockAfter = stockAfter, Receipt = receipt, Notice = notice
        });
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(deferred, unknown);
        Assert.Equal(!deferred, unavailable);
        Assert.Equal(before, after);
        Assert.True(afterBinActive);
        Assert.Equal(3, afterBinVersion);
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Put, DeactivatePath(location)));
    }
}
