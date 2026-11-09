using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_restore_candidate_cannot_redispatch_the_sources_original()
    {
        await using var source = await Seed();
        await using var other = await Seed();
        var mover = InventoryMover(source, await GrantStocktakeDashboard(source));
        var otherMover = InventoryMover(other, await GrantStocktakeDashboard(other));
        await SeedInventory(source);
        await SeedInventory(other);
        var privacySetup = await PrivacyDispositionSetup(source);
        await EngineeringAction(source, "privacy-disposition", "execute");
        await RestorePermissions(source);

        var command = StockApply();
        var committed = await mover.ApplyCommandAsync(command);
        var connection = source.Db.Database.GetConnectionString()!;
        await using var candidate = await PrivacyRestoreCandidateFixture.Create(connection);
        await using var target = PrivacyRestoreNativeV1.Context(candidate.Connection, source.Tenant);
        Assert.NotEqual((await PrivacyRestoreNativeV1.Database(source.Db, default)).Oid,
            (await PrivacyRestoreNativeV1.Database(target, default)).Oid);
        Assert.Equal(PrivacyRestoreNativeV1.MarkerPrefix + candidate.Id.ToString("D"),
            await PrivacyRestoreNativeV1.Marker(target, default));
        var pending = await StockRestoreOriginal(source.Db, source.Tenant, command.OperationId);
        Assert.NotNull(pending);
        Assert.Equal(pending, await StockRestoreOriginal(target, source.Tenant, command.OperationId));

        var sourcePublisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var sourceWorker = new StockOutboxWorkerV1(source.Db, StockHost(), sourcePublisher);
        Assert.Equal("RESOLVED", (await sourceWorker.RunAsync(source.Tenant, default)).State);
        Assert.Equal($"stock-hint:{source.Tenant:N}:{command.OperationId:N}", Assert.Single(sourcePublisher.Messages).MessageId);
        var published = await StockRestoreOriginal(source.Db, source.Tenant, command.OperationId);
        Assert.NotEqual(pending, published);

        var otherResult = await otherMover.ApplyCommandAsync(StockApply(command.OperationId));
        var otherPublisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var otherWorker = new StockOutboxWorkerV1(other.Db, StockHost(), otherPublisher);
        Assert.Equal("RESOLVED", (await otherWorker.RunAsync(other.Tenant, default)).State);
        Assert.Equal($"stock-hint:{other.Tenant:N}:{command.OperationId:N}", Assert.Single(otherPublisher.Messages).MessageId);

        var guard = new PrivacyRestoreOptionsV1 { ControlConnectionString = connection };
        var denial = await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(
            () => PrivacyRestoreAdmissionV1.AcquireAsync(target, guard, default));
        Assert.Equal(503, denial.Status);
        Assert.Equal("PRIVACY_RESTORE_QUARANTINED", denial.Message);

        var candidatePublisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        await AssertStockRestoreRefused(new StockOutboxWorkerV1(target, StockHost(), candidatePublisher), source.Tenant);
        await using var targetOther = PrivacyRestoreNativeV1.Context(candidate.Connection, other.Tenant);
        await AssertStockRestoreRefused(new StockOutboxWorkerV1(targetOther, StockHost(), candidatePublisher), other.Tenant);

        var runtime = await RestoreRuntime(source);
        await using var privacyDb = server.Context(new TenantContext { CurrentTenantId = source.Tenant }, runtime);
        var actor = source with { Db = privacyDb };
        var approver = privacySetup.Approver with { Db = privacyDb };
        var disposition = await PrivacyApprovedPlan(actor, privacySetup.Target, privacySetup.Policy, approver);
        await PrivacyDispositionOwner(actor).ExecuteAsync(disposition.Plan, PrivacyExecuteCommand(disposition), PrivacySession(actor));
        var registration = RestoreRegistration(candidate);
        var plan = await ApprovedRestore(actor, approver, registration, runtime, candidate.Id);
        var restored = await PrivacyRestorer(actor, registration, runtime)
            .ExecuteRestoreAsync(plan.PlanId, RestoreCommand(plan), PrivacySession(actor));
        Assert.Equal("APPLIED", restored.State);
        await using (var admission = await PrivacyRestoreAdmissionV1.AcquireAsync(target, guard, default))
            Assert.NotNull(admission);

        await AssertStockRestoreRefused(new StockOutboxWorkerV1(target, StockHost(), candidatePublisher,
            privacyRestoreConfigured: true), source.Tenant);
        Assert.Empty(candidatePublisher.Messages);
        await AssertStockRestorePending(candidate.Connection, source.Tenant);
        Assert.Equal(pending, await StockRestoreOriginal(target, source.Tenant, command.OperationId));
        Assert.Null(await StockRestoreOriginal(target, other.Tenant, command.OperationId));
        Assert.Equal(published, await StockRestoreOriginal(source.Db, source.Tenant, command.OperationId));
        Assert.Equal(new StockOperationV1(command.OperationId, "COMMITTED", committed),
            await mover.ReadOperationAsync(command.OperationId));
        Assert.Equal(new StockOperationV1(command.OperationId, "COMMITTED", otherResult),
            await otherMover.ReadOperationAsync(command.OperationId));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_restore_dispatch_rechecks_the_actual_queue_at_transaction_authority()
    {
        await using var source = await Seed();
        var mover = InventoryMover(source, await GrantStocktakeDashboard(source));
        await SeedInventory(source);
        var command = StockApply();
        await mover.ApplyCommandAsync(command);
        var pending = await StockRestoreOriginal(source.Db, source.Tenant, command.OperationId);
        Assert.NotNull(pending);

        await using var candidate = await PrivacyRestoreCandidateFixture.Create(source.Db.Database.GetConnectionString()!);
        await using var target = PrivacyRestoreNativeV1.Context(candidate.Connection, source.Tenant);
        var queueOptions = new DbContextOptionsBuilder<StockOutboxContextV1>().UseNpgsql(candidate.Connection);
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var candidateFactory = new StockOutboxFactoryV1(queueOptions.Options);
        await AssertStockRestoreRefused(new StockOutboxWorkerV1(source.Db, StockHost(), publisher, candidateFactory), source.Tenant);

        await SetStockRestoreMarker(candidate, false, default);
        try
        {
            await AssertStockRestoreRefused(new StockOutboxWorkerV1(source.Db, StockHost(), publisher, candidateFactory,
                privacyRestoreConfigured: true), source.Tenant);

            var marker = new MarkStockRestoreAtQueueTransaction(ct => SetStockRestoreMarker(candidate, true, ct));
            var factory = new StockOutboxFactoryV1(queueOptions.AddInterceptors(marker).Options);
            await AssertStockRestoreRefused(new StockOutboxWorkerV1(source.Db, StockHost(), publisher, factory), source.Tenant);
            Assert.True(marker.Fired);
        }
        finally
        {
            await SetStockRestoreMarker(candidate, true, default);
        }

        Assert.Empty(publisher.Messages);
        await AssertStockRestorePending(candidate.Connection, source.Tenant);
        Assert.Equal(pending, await StockRestoreOriginal(target, source.Tenant, command.OperationId));
        await AssertStockRestorePending(source.Db.Database.GetConnectionString()!, source.Tenant);
        Assert.Equal(pending, await StockRestoreOriginal(source.Db, source.Tenant, command.OperationId));
    }

    private static async Task AssertStockRestoreRefused(StockOutboxWorkerV1 worker, Guid tenant)
    {
        var denial = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => worker.RunAsync(tenant, default));
        Assert.Equal("STOCK_OUTBOX_RESTORE_DISPATCH_FORBIDDEN", denial.Code);
    }

    private static async Task AssertStockRestorePending(string connection, Guid tenant)
    {
        await using var queue = new StockOutboxContextV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(connection).Options);
        var root = StockOutboxProtocolV1.Root(tenant).Id;
        Assert.Equal(Cp6OutboxStatus.Pending, (await queue.Set<Cp6OutboxMessage>().SingleAsync(n => n.TenantId == tenant)).Status);
        Assert.False(await queue.Set<StockOutboxRunV1>().AnyAsync(r => r.RootId == root));
        Assert.False(await queue.Set<OutboxClaimReceiptV2>().AnyAsync(r => r.RootId == root));
        Assert.False(await queue.Set<OutboxSendAttemptV2>().AnyAsync(r => r.RootId == root));
        Assert.False(await queue.Set<OutboxLeaseMutationReceiptV2>().AnyAsync(r => r.RootId == root));
    }

    private static async Task SetStockRestoreMarker(PrivacyRestoreCandidateFixture candidate, bool marked, CancellationToken ct)
    {
        var comment = marked ? $"'{PrivacyRestoreNativeV1.MarkerPrefix}{candidate.Id:D}'" : "NULL";
        await using var connection = new NpgsqlConnection(candidate.Connection);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"COMMENT ON DATABASE \"{candidate.Name}\" IS {comment}", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class MarkStockRestoreAtQueueTransaction(Func<CancellationToken, Task> mark) : DbTransactionInterceptor
    {
        internal bool Fired { get; private set; }

        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                await mark(cancellationToken);
                Fired = true;
            }
            return result;
        }
    }

    private static Task<string?> StockRestoreOriginal(CP6Context db, Guid tenant, Guid operation) =>
        db.Database.SqlQuery<string?>($"""
            SELECT jsonb_build_array(to_jsonb(n), to_jsonb(s), to_jsonb(b), to_jsonb(r), to_jsonb(j))::text AS "Value"
            FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" n
            JOIN cp6_stock_outbox_v1."Source" s ON s."NativeId"=n."Id"
            JOIN cp6_stock_outbox_v1."Cp6_OutboxBindingV2" b ON b."NativeId"=n."Id"
            JOIN cp6_stock_outbox_v1."Cp6_OutboxRouteV2" r ON r."Id"=b."RootId"
            JOIN cp6_main_owner_v1.operations j ON j.tenant_id=s."TenantId"
              AND j.owner=s."SourceOwner" AND j.operation_id=s."OperationId"
            WHERE n."TenantId"={tenant} AND s."OperationId"={operation} AND j.owner={StockOutboxProtocolV1.SourceOwner}
            """).SingleOrDefaultAsync();
}
