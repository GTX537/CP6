using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TopologyDeactivate_failed_item_has_no_half_state_and_recovers(bool rack, bool deferred)
    {
        var seeded = await TopologyDeactivateSeed(two: false);
        await using var h = seeded.Harness;
        var input = seeded.Input;
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        using var hub = await PublishHub.Connect(host);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var notification = hub.ReadNotice(deadline.Token);
        var before = await SceneDeltaRows(h);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await control.OpenAsync();
        var trigger = deferred
            ? """CREATE CONSTRAINT TRIGGER cp6_test_topology_item_fault AFTER UPDATE ON public."Space_Location" DEFERRABLE INITIALLY DEFERRED"""
            : """CREATE TRIGGER cp6_test_topology_item_fault BEFORE UPDATE ON public."Space_Location" """;
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_topology_item_fault() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated topology item failure'; END $fault$;
            {{trigger}}
            FOR EACH ROW WHEN (NEW."Id"='{{input.First}}' AND NEW."Status"=2)
            EXECUTE FUNCTION public.cp6_test_topology_item_fault();
            """, control);
        await install.ExecuteNonQueryAsync();
        int status, afterSeq;
        string after, body;
        TopologyItem state;
        try
        {
            using var failed = await Send();
            status = (int)failed.StatusCode;
            body = await failed.Content.ReadAsStringAsync();
            after = await SceneDeltaRows(h);
            state = await TopologyItemState(h, input.First);
            afterSeq = await SceneDeltaSequence(h);
            Assert.Equal(1, state.Status);
            Assert.Equal(3, state.Version);
            Assert.Equal(seq, afterSeq);
            Assert.Equal(stock, await PalletStockRows(h));
            Assert.False(notification.IsCompleted);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_topology_item_fault ON public."Space_Location";
                DROP FUNCTION public.cp6_test_topology_item_fault();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
        using var resumed = await Send();
        Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
        var receipt = await TopologyStopFinished(h, input, rack, seq, 1);
        var notice = await notification;
        Assert.Equal(1, notice.GetProperty("count").GetInt32());
        var stockAfter = await PalletStockRows(h);
        Assert.Equal(stock, stockAfter);
        var unknown = body.Contains("SPACE_TOPOLOGY_OUTCOME_UNKNOWN", StringComparison.Ordinal);
        var unavailable = body.Contains("SPACE_TOPOLOGY_AUTHORITY_UNAVAILABLE", StringComparison.Ordinal);
        await DeviceEvidence("topology-deactivate-fault-" + (rack ? "rack" : "aisle") + (deferred ? "-commit" : "-write") + ".json",
            new { Status = status, Deferred = deferred, Unknown = unknown, Unavailable = unavailable, Before = before, After = after,
                State = state, SequenceBefore = seq, SequenceAfter = afterSeq, StockBefore = stock, StockAfter = stockAfter, Notice = notice, Recovered = receipt });
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(deferred, unknown);
        Assert.Equal(!deferred, unavailable);
        Assert.Equal(before, after);
        Assert.Equal(new(1, 3, true, 3), state);
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Delete, input.Path(rack)));
    }
}
