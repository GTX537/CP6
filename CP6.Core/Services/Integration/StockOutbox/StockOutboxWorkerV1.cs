using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Integration.StockOutbox;

internal sealed record StockOutboxObservationV1(string State, Guid? RequestId, int Completed);

/// <summary>Durable request identity precedes the claim. A restart queries the same original claim and
/// completion receipts. Unknown transport/send attempts retain the request; they never create a new claim.</summary>
internal sealed class StockOutboxWorkerV1(CP6Context source, StockOutboxHostOptionsV1 options,
    IOutboxPublisherV2 publisher, IDbContextFactory<StockOutboxContextV1>? testFactory = null)
{
    private const int BatchSize = 4; // Four ten-second send budgets fit inside the one-minute native lease.
    internal async Task<StockOutboxObservationV1> RunAsync(Guid enumeratedTenant, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!options.Enabled) return new("DISABLED", null, 0);
        if (enumeratedTenant == Guid.Empty || source.CurrentTenantId != enumeratedTenant || !source.Database.IsNpgsql()
            || source.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_TENANT_SCOPE_REQUIRED");
        var factory = testFactory ?? new StockOutboxFactoryV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(source.Database.GetConnectionString()).Options);
        var root = StockOutboxProtocolV1.Root(enumeratedTenant);
        await using (var db = await factory.CreateDbContextAsync(ct))
            if (!await db.Set<StockOutboxAuthorityV1>().AnyAsync(a => a.RootId == root.Id && a.TenantId == enumeratedTenant, ct))
                return new("IDLE", null, 0);
        var host = new StockOutboxHostV1(factory, options, enumeratedTenant);
        var store = new CallerTransactionOutboxV2(host.Trusted, host);
        var dispatcher = new OutboxDispatcherV2<StockOutboxContextV1>(factory, host.Trusted, host, publisher);
        StockOutboxObservationV1 result = new("IDLE", null, 0);
        await host.Execute(async token =>
        {
            var request = await GetOrCreateRun(factory, host, store, root, token);
            if (request is null) return;
            var original = await Inspect(factory, host, store, root, request.Value, token);
            if (!original.Resolved)
            {
                // The package queries committed originals before attempting any send. Persisted send
                // attempts reject a second transport call, even when the first process disappeared.
                await dispatcher.DispatchBatchV2(root, request.Value, BatchSize, token);
                token.ThrowIfCancellationRequested();
                original = await Inspect(factory, host, store, root, request.Value, token);
            }
            if (original.Resolved)
            {
                await CompleteRun(factory, host, store, root, request.Value, token);
                result = new("RESOLVED", request, original.Count);
            }
            else result = new("OUTCOME_UNKNOWN", request, original.Count);
        }, ct);
        return result;
    }

    private static async Task<Guid?> GetOrCreateRun(IDbContextFactory<StockOutboxContextV1> factory,
        StockOutboxHostV1 host, CallerTransactionOutboxV2 store, OutboxRootKeyV2 root, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var capability = await store.BindTransactionAsync(db, tx.GetDbTransaction(), root, false, ct);
        await host.AuthorizeAsync(capability, OutboxAccessV2.ReadReceipt, ct);
        var active = await db.Set<StockOutboxRunV1>().AsNoTracking().SingleOrDefaultAsync(r => r.RootId == root.Id && r.CompletedAtUtc == null, ct);
        if (active != null) return active.RequestId;
        await host.AuthorizeAsync(capability, OutboxAccessV2.Claim, ct);
        // Do not create an immutable empty receipt on every idle poll.
        var available = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_stock_outbox_v1.\"Cp6_OutboxMessage\" n JOIN cp6_stock_outbox_v1.\"Cp6_OutboxBindingV2\" b ON b.\"NativeId\"=n.\"Id\" WHERE b.\"RootId\"={root.Id} AND n.\"TenantId\"={root.TenantId} AND n.\"Status\"=0 AND n.\"AvailableAtUtc\"<=clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        if (!available) return null;
        var run = new StockOutboxRunV1 { RootId = root.Id, RequestId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Add(run); await db.SaveChangesAsync(ct);
        // If acknowledgement is lost, the next invocation reads this active row before allocating a new ID.
        await tx.CommitAsync(ct);
        return run.RequestId;
    }

    private static async Task<(bool Resolved, int Count)> Inspect(IDbContextFactory<StockOutboxContextV1> factory,
        StockOutboxHostV1 host, CallerTransactionOutboxV2 store, OutboxRootKeyV2 root, Guid request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var capability = await store.BindTransactionAsync(db, tx.GetDbTransaction(), root, true, ct);
        await host.AuthorizeAsync(capability, OutboxAccessV2.ReadReceipt, ct);
        var claim = await db.Set<OutboxClaimReceiptV2>().AsNoTracking().SingleOrDefaultAsync(r => r.RootId == root.Id && r.RequestId == request, ct);
        if (claim == null) return (false, 0);
        var confirmed = await store.QueryClaimV2(capability, request, claim.Input, ct);
        if (confirmed.Decision != OutboxDecisionV2.Committed || confirmed.Committed == null) return (false, 0);
        var count = 0;
        foreach (var committed in confirmed.Committed)
        {
            var ticket = committed.Ticket;
            var receipt = await db.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking()
                .SingleOrDefaultAsync(r => r.RootId == root.Id && r.MutationId == ticket.ClaimReceiptRef, ct);
            if (receipt == null) continue;
            using var parsed = JsonDocument.Parse(receipt.Input);
            var mutation = parsed.RootElement.GetProperty("Input").Deserialize<OutboxMutationV2>();
            if (parsed.RootElement.GetProperty("Kind").GetString() != "Complete" || mutation == null
                || mutation.Ticket != ticket || mutation.MutationId != ticket.ClaimReceiptRef || receipt.NativeId != ticket.NativeId
                || mutation.PolicyVersion != StockOutboxProtocolV1.PolicyVersion
                || CallerTransactionOutboxV2.MutationInput(mutation) != receipt.Input)
                throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_ORIGINAL_RECEIPT_CONFLICT");
            var observed = await store.QueryClaimMutationV2(capability, mutation.MutationId, receipt.Input, ct);
            if (observed.Decision == OutboxDecisionV2.Committed && observed.Transition is "Published" or "RetryScheduled" or "DeadLettered") count++;
        }
        return (count == confirmed.Committed.Count, count);
    }

    private static async Task CompleteRun(IDbContextFactory<StockOutboxContextV1> factory, StockOutboxHostV1 host,
        CallerTransactionOutboxV2 store, OutboxRootKeyV2 root, Guid request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var capability = await store.BindTransactionAsync(db, tx.GetDbTransaction(), root, false, ct);
        await host.AuthorizeAsync(capability, OutboxAccessV2.ReadReceipt, ct);
        var run = await db.Set<StockOutboxRunV1>().SingleAsync(r => r.RootId == root.Id && r.RequestId == request, ct);
        if (run.CompletedAtUtc != null) return;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); // Native trigger independently rejects missing original claim/completion receipts.
        await tx.CommitAsync(ct);
    }
}
