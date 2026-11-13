using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Wms;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_epoch_switch_requires_terminal_drain_and_preserves_originals()
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var mover = InventoryMover(h, await GrantStocktakeDashboard(h));
        await SeedInventory(h);
        var first = StockApply();
        var original = await mover.ApplyCommandAsync(first);
        await InventoryMover(other, await GrantStocktakeDashboard(other)).ApplyCommandAsync(await SeedStock(other));
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var worker = new StockOutboxWorkerV1(h.Db, StockHost(), publisher);
        await using var queue = StockQueue(h);
        var root = StockOutboxProtocolV1.Root(h.Tenant).Id;
        var runtimeHost = new StockOutboxHostV1(new StockOutboxFactoryV1(
            new DbContextOptionsBuilder<StockOutboxContextV1>().UseNpgsql(h.Db.Database.GetConnectionString()).Options),
            StockHost(), h.Tenant, privacyRestoreConfigured: false);
        var runtimeStore = new CallerTransactionOutboxV2(runtimeHost.Trusted, runtimeHost);
        var before = await Route(queue, h.Tenant);
        var switchId = Guid.NewGuid();
        var advanceId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await connection.OpenAsync();

        var pending = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Switch(connection, h.Tenant, switchId, null, before));
        Assert.Equal("STOCK_OUTBOX_SWITCH_UNRESOLVED", pending.Code);
        Assert.Equal(before.CurrentEpoch, (await Route(queue, h.Tenant)).CurrentEpoch);
        Assert.Equal(OutboxPhaseV2.Active, (await Route(queue, h.Tenant)).Phase);
        Assert.Empty(publisher.Messages);
        var foreign = await QueueState(queue, other.Tenant);

        Assert.Equal("RESOLVED", (await worker.RunAsync(h.Tenant, default)).State);
        var originalQueue = await QueueState(queue, h.Tenant);
        var ticketRow = await queue.Set<OutboxClaimTicketReceiptV2>().AsNoTracking().SingleAsync(x => x.RootId == root);
        var claim = await queue.Set<OutboxClaimReceiptV2>().AsNoTracking().SingleAsync(x => x.RootId == root);
        var drain = await Switch(connection, h.Tenant, switchId, null, before);
        var draining = await Route(queue, h.Tenant);
        Assert.Equal(OutboxPhaseV2.Draining, draining.Phase);
        Assert.Equal(1, draining.CurrentEpoch);
        Assert.Equal(drain, await Switch(connection, h.Tenant, switchId, null, before));
        var rejected = StockApply();
        await Assert.ThrowsAnyAsync<Exception>(() => mover.ApplyCommandAsync(rejected));
        Assert.Equal(1, await Operations(h));

        var advanced = await Switch(connection, h.Tenant, advanceId, switchId, draining);
        Assert.Equal(advanced, await Switch(connection, h.Tenant, advanceId, switchId, draining));
        Assert.Equal(advanced, await Switch(connection, h.Tenant, advanceId, switchId, draining, recoverOnly: true));
        await Assert.ThrowsAsync<StockOutboxSwitchOutcomeUnknown>(() =>
            Switch(connection, h.Tenant, Guid.NewGuid(), switchId, draining, recoverOnly: true));
        var active = await Route(queue, h.Tenant);
        Assert.Equal(2, active.CurrentEpoch);
        Assert.Equal(OutboxPhaseV2.Active, active.Phase);
        Assert.Equal(originalQueue, await QueueState(queue, h.Tenant));
        Assert.Equal(foreign, await QueueState(queue, other.Tenant));
        Assert.Equal(1, (await Route(queue, other.Tenant)).CurrentEpoch);
        await runtimeHost.Execute(async ct =>
        {
            var stockRoot = StockOutboxProtocolV1.Root(h.Tenant);
            CommittedClaimTicketV2 ticket;
            await using (var read = await queue.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct))
            {
                await queue.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
                var readScope = await runtimeStore.BindTransactionAsync(queue, read.GetDbTransaction(), stockRoot, true, ct);
                ticket = Assert.Single((await runtimeStore.QueryClaimV2(readScope, ticketRow.RequestId, claim.Input, ct)).Committed!);
            }
            await using var tx = await queue.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var scope = await runtimeStore.BindTransactionAsync(queue, tx.GetDbTransaction(), stockRoot, false, ct);
            var stale = new OutboxMutationV2(Guid.NewGuid(), ticket.Ticket, OutboxIntentV2.PublishConfirmed,
                StockOutboxProtocolV1.PolicyVersion);
            Assert.Equal(OutboxDecisionV2.RouteEpochChanged,
                (await runtimeStore.CompleteClaimV2(scope, ticket, stale, ct)).Decision);
        }, default);
        Assert.Equal("IDLE", (await worker.RunAsync(h.Tenant, default)).State);
        Assert.Single(publisher.Messages);

        var next = StockApply();
        await mover.ApplyCommandAsync(next);
        Assert.Equal("RESOLVED", (await worker.RunAsync(h.Tenant, default)).State);
        Assert.Equal(2, publisher.Messages.Count);
        Assert.Equal(original, await mover.ApplyCommandAsync(first));
        var bindings = await queue.Set<OutboxLeaseBindingV2>().AsNoTracking().Where(x => x.RootId == root).ToListAsync();
        Assert.Equal(1, Assert.Single(bindings, x => x.MessageId == StockOutboxProtocolV1.Message(h.Tenant, first.OperationId)).AssignedEpoch);
        Assert.Equal(2, Assert.Single(bindings, x => x.MessageId == StockOutboxProtocolV1.Message(h.Tenant, next.OperationId)).AssignedEpoch);
        await Assert.ThrowsAsync<PostgresException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cp6_stock_outbox_v1.\"Cp6_OutboxRouteV2\" SET \"CurrentEpoch\"=1 WHERE \"Id\"={root}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_epoch_switch_does_not_advance_unknown_send()
    {
        await using var h = await Seed();
        var mover = InventoryMover(h, await GrantStocktakeDashboard(h));
        await mover.ApplyCommandAsync(await SeedStock(h));
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Unknown);
        var worker = new StockOutboxWorkerV1(h.Db, StockHost(), publisher);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant, default)).State);
        await using var queue = StockQueue(h);
        var original = await QueueState(queue, h.Tenant);
        await using var connection = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await connection.OpenAsync();
        var route = await Route(queue, h.Tenant);
        var denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Switch(connection, h.Tenant, Guid.NewGuid(), null, route));
        Assert.Equal("STOCK_OUTBOX_SWITCH_UNRESOLVED", denied.Code);
        Assert.Equal(route.Version, (await Route(queue, h.Tenant)).Version);
        Assert.Equal(original, await QueueState(queue, h.Tenant));
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant, default)).State);
        Assert.Single(publisher.Messages);
    }

    private static async Task<StockApplyCommandV1> SeedStock(Harness h)
    {
        await SeedInventory(h);
        return StockApply();
    }

    private static Task<OutboxRouteControlV2> Route(StockOutboxContextV1 queue, Guid tenant)
        => queue.Set<OutboxRouteControlV2>().AsNoTracking().SingleAsync(x => x.TenantId == tenant);

    private static Task<string> QueueState(StockOutboxContextV1 queue, Guid tenant)
        => queue.Database.SqlQuery<string>($"SELECT jsonb_agg(to_jsonb(n) ORDER BY n.\"Id\")::text AS \"Value\" FROM cp6_stock_outbox_v1.\"Cp6_OutboxMessage\" n WHERE n.\"TenantId\"={tenant}").SingleAsync();

    private static async Task<string> Switch(NpgsqlConnection connection, Guid tenant, Guid operation, Guid? drain,
        OutboxRouteControlV2 expected, bool recoverOnly = false)
    {
        await using var query = new NpgsqlCommand("""
            SELECT d.oid,(SELECT system_identifier::text FROM pg_control_system()),host(inet_server_addr()),inet_server_port()
            FROM pg_database d WHERE d.datname=current_database()
            """, connection);
        RmaTenantTargetV1 target;
        await using (var row = await query.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            target = new(row.GetString(2), row.GetInt32(3), connection.Database, row.GetFieldValue<uint>(0), row.GetString(1));
        }
        return await StockOutboxEpochControlV1.ApplyAsync(connection, target, tenant, operation, drain,
            expected.CurrentEpoch, OutboxEncodingV2.Encode(expected.Version), default, recoverOnly);
    }
}
