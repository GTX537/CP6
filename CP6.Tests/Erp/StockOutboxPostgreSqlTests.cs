using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class StockPublisher(OutboxTransportDecisionV2 decision) : IOutboxPublisherV2
    {
        internal System.Collections.Concurrent.ConcurrentQueue<Cp6OutboxDispatchMessage> Messages { get; } = new();
        public ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Messages.Enqueue(message);
            return ValueTask.FromResult(new OutboxTransportResultV2(decision));
        }
    }
    private static StockOutboxHostOptionsV1 StockHost() => new()
    {
        Enabled = true, HostId = "Main.OwnedPostgreSqlTestHost", Issuer = "https://stock-outbox.test.cp6.invalid", Region = "owned-local"
    };
    private static StockOutboxContextV1 StockQueue(Harness h) => new(new DbContextOptionsBuilder<StockOutboxContextV1>()
        .UseNpgsql(h.Db.Database.GetConnectionString()).Options);
    private sealed class StockDispatchAckFault(string stage) : DbTransactionInterceptor
    {
        internal bool Fired;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            var db = eventData.Context;
            if (!Fired && db != null && (stage switch
                {
                    "claim" => db.ChangeTracker.Entries<OutboxClaimReceiptV2>().Any(),
                    "send" => db.ChangeTracker.Entries<OutboxSendAttemptV2>().Any(),
                    "complete" => db.ChangeTracker.Entries<OutboxLeaseMutationReceiptV2>().Any(),
                    _ => false
                })) { Fired = true; throw new IOException("owned dispatch commit acknowledgement lost"); }
            return Task.CompletedTask;
        }
    }
    private static async Task<int> StockNotifications(Harness h)
    {
        if(!await h.Db.Database.SqlQueryRaw<bool>("SELECT to_regclass('cp6_stock_outbox_v1.\"Cp6_OutboxMessage\"') IS NOT NULL AS \"Value\"").SingleAsync())return 0;
        return await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_stock_outbox_v1.\"Cp6_OutboxMessage\" WHERE \"TenantId\"={h.Tenant}").SingleAsync();
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stock_outbox_committed_original_has_durable_notification_even_when_commit_ack_is_lost(bool lostAck)
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var mover=InventoryMover(h,owner);var command=StockApply();fault.Armed=lostAck;
        if(lostAck)Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        else await mover.ApplyCommandAsync(command);
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await Operations(h));
        Assert.Equal(1,await StockNotifications(h));
        Assert.Equal("COMMITTED",(await mover.ReadOperationAsync(command.OperationId)).State);
        await mover.ApplyCommandAsync(command);Assert.Equal(1,await StockNotifications(h));Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_worker_publishes_only_empty_tenant_hint_and_resolves_durable_original()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        var command = StockApply(); await InventoryMover(h, owner).ApplyCommandAsync(command);
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var first = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default);
        Assert.Equal("RESOLVED", first.State); Assert.Equal(1, first.Completed);
        var sent = Assert.Single(publisher.Messages); Assert.Equal(h.Tenant, sent.TenantId);
        Assert.Equal("{}"u8.ToArray(), sent.Payload.ToArray()); Assert.Equal(command.OperationId.ToString("D"), sent.AggregateId);
        await using var queue = StockQueue(h);
        var native = await queue.Set<Cp6OutboxMessage>().SingleAsync(n => n.TenantId == h.Tenant);
        Assert.Equal(Cp6OutboxStatus.Published, native.Status); Assert.Equal(4, native.RowVersion.Length);
        var run = await queue.Set<StockOutboxRunV1>().SingleAsync(r => r.RequestId == first.RequestId);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal("IDLE", (await new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default)).State);
        Assert.Single(publisher.Messages); Assert.Single(await queue.Set<OutboxClaimReceiptV2>().Where(r => r.RootId == run.RootId).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_unknown_send_restart_keeps_original_request_and_never_sends_again()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Unknown);
        var first = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default);
        var second = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default);
        Assert.Equal("OUTCOME_UNKNOWN", first.State); Assert.Equal(first, second); Assert.Single(publisher.Messages);
        await using var queue = StockQueue(h); var root = StockOutboxProtocolV1.Root(h.Tenant).Id;
        Assert.Single(await queue.Set<OutboxClaimReceiptV2>().Where(r => r.RootId == root).ToArrayAsync());
        Assert.Single(await queue.Set<OutboxSendAttemptV2>().Where(r => r.RootId == root).ToArrayAsync());
        Assert.Null((await queue.Set<StockOutboxRunV1>().SingleAsync(r => r.RootId == root)).CompletedAtUtc);
        Assert.Empty(await queue.Set<OutboxLeaseMutationReceiptV2>().Where(r => r.RootId == root).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_worker_requires_current_tenant_scope_registration_and_enabled_tenant()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StockOutboxWorkerV1(h.Db, new() { Enabled = true }, publisher).RunAsync(h.Tenant, default));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={h.Tenant}");
        await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default));
        Assert.Empty(publisher.Messages);
        await using var queue = StockQueue(h);
        Assert.Equal(Cp6OutboxStatus.Pending, (await queue.Set<Cp6OutboxMessage>().SingleAsync(n => n.TenantId == h.Tenant)).Status);
        Assert.Empty(await queue.Set<StockOutboxRunV1>().Where(r => r.RootId == StockOutboxProtocolV1.Root(h.Tenant).Id).ToArrayAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("claim")]
    [InlineData("send")]
    [InlineData("complete")]
    public async Task Stock_outbox_commit_ack_loss_preserves_claim_send_and_completion_originals(string stage)
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        var fault = new StockDispatchAckFault(stage);
        var factory = new StockOutboxFactoryV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(h.Db.Database.GetConnectionString()).AddInterceptors(fault).Options);
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var first = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher, factory).RunAsync(h.Tenant, default);
        Assert.True(fault.Fired);
        var second = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher, factory).RunAsync(h.Tenant, default);
        if (stage == "send")
        {
            Assert.Equal("OUTCOME_UNKNOWN", first.State); Assert.Equal(first, second); Assert.Empty(publisher.Messages);
        }
        else
        {
            Assert.Single(publisher.Messages);
            Assert.Equal(stage == "claim" ? "OUTCOME_UNKNOWN" : "RESOLVED", first.State);
            Assert.Equal(stage == "claim" ? "RESOLVED" : "IDLE", second.State);
            if (stage == "claim") Assert.Equal(first.RequestId, second.RequestId);
        }
        await using var queue = StockQueue(h); var root = StockOutboxProtocolV1.Root(h.Tenant).Id;
        Assert.Single(await queue.Set<OutboxClaimReceiptV2>().Where(r => r.RootId == root).ToArrayAsync());
        Assert.Single(await queue.Set<StockOutboxRunV1>().Where(r => r.RootId == root).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_enqueue_failure_rolls_back_stock_sequence_journal_and_queue_together()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        var mover = InventoryMover(h, owner); await mover.ApplyCommandAsync(StockApply());
        var quantity = await h.Db.Stocks.Where(s => s.ProductCd == "P1").Select(s => s.PhysicalQty).SingleAsync();
        var sequences = await h.Db.WmsSequences.AsNoTracking().ToArrayAsync();
        await using var queue = StockQueue(h);
        await queue.Set<StockOutboxAuthorityV1>().Where(a => a.TenantId == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(a => a.Enabled, false));
        await Assert.ThrowsAnyAsync<Exception>(() => mover.ApplyCommandAsync(StockApply()));
        Assert.Equal(quantity, await h.Db.Stocks.Where(s => s.ProductCd == "P1").Select(s => s.PhysicalQty).SingleAsync());
        Assert.Equal(1, await Operations(h)); Assert.Equal(1, await StockNotifications(h)); Assert.Equal(1, await h.Db.StockTransactions.CountAsync());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(sequences), System.Text.Json.JsonSerializer.Serialize(await h.Db.WmsSequences.AsNoTracking().ToArrayAsync()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_native_guards_reject_unbound_enqueue_legacy_writer_and_original_rewrite()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        await using var queue = StockQueue(h);
        var legacy = new Cp6OutboxStore<StockOutboxContextV1>(queue, new StockOutboxEnvelopeValidatorV1());
        Assert.Empty(await legacy.ClaimBatchAsync("old-worker", new()));
        var bypass = await Assert.ThrowsAsync<PostgresException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_stock_outbox_v1.\"Cp6_OutboxMessage\" SET \"Status\"=2 WHERE \"TenantId\"={h.Tenant}"));
        Assert.Equal("CP6_OUTBOX_V2_LEGACY_BYPASS", bypass.MessageText);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_stock_outbox_v1.\"Source\" SET \"ResultSha256\"='changed' WHERE \"TenantId\"={h.Tenant}"));
        Assert.Equal("CP6_OUTBOX_IMMUTABLE_EVIDENCE", immutable.MessageText);
        await Assert.ThrowsAsync<PostgresException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_stock_outbox_v1.\"Cp6_OutboxRouteV2\" SET \"CurrentEpoch\"=2 WHERE \"TenantId\"={h.Tenant}"));
        legacy.Enqueue(StockOutboxProtocolV1.Envelope(h.Tenant, Guid.NewGuid()));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => queue.SaveChangesAsync());
        Assert.Equal("STOCK_OUTBOX_SOURCE_PROOF_REQUIRED", Assert.IsType<PostgresException>(failure.InnerException).MessageText);
        Assert.Equal(1, await StockNotifications(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_two_real_worker_transactions_cannot_send_the_same_original_twice()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        await using var secondSource = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        async Task Invoke(CP6.Core.EFDbContext.CP6Context db)
        {
            try { await new StockOutboxWorkerV1(db, StockHost(), publisher).RunAsync(h.Tenant, default); }
            catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" }) { }
            // Npgsql may wrap 40001 in its execution-strategy exception. The next tenant job retries;
            // other failures remain test failures, and the assertions below require exactly one actual send.
        }
        await Task.WhenAll(Invoke(h.Db), Invoke(secondSource));
        await new StockOutboxWorkerV1(h.Db, StockHost(), publisher).RunAsync(h.Tenant, default);
        Assert.Single(publisher.Messages);
        await using var queue = StockQueue(h); var root = StockOutboxProtocolV1.Root(h.Tenant).Id;
        Assert.Equal(Cp6OutboxStatus.Published, (await queue.Set<Cp6OutboxMessage>().SingleAsync(m => m.TenantId == h.Tenant)).Status);
        Assert.Single(await queue.Set<OutboxClaimReceiptV2>().Where(r => r.RootId == root).ToArrayAsync());
    }
}
