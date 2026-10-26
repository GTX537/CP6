using System.Data;
using CP6.Core.EFDbContext;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration.StockOutbox;

internal static class StockOutboxEnqueueV1
{
    /// <summary>Caller owns commit/rollback. A second model shares the exact open connection and native transaction.</summary>
    internal static async Task Enqueue(CP6Context source, Guid operation, CancellationToken ct)
    {
        if(!source.Database.IsNpgsql() || source.CurrentTenantId == Guid.Empty || operation == Guid.Empty
            || source.Database.CurrentTransaction?.GetDbTransaction() is not NpgsqlTransaction native
            || !ReferenceEquals(native.Connection, source.Database.GetDbConnection())
            || native.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new InvalidOperationException("STOCK_OUTBOX_SOURCE_TRANSACTION_REQUIRED");
        var original = await new MainOwnerOperationJournalV1(source).Read(StockOutboxProtocolV1.SourceOwner, operation, ct);
        if(original is not { State: "COMMITTED" }
            || original.Contract is not (StockOutboxProtocolV1.SourceContract or StockOutboxProtocolV1.CreationSourceContract))
            throw new InvalidOperationException("STOCK_OUTBOX_COMMITTED_SOURCE_REQUIRED");
        var tenant = source.CurrentTenantId;
        var root = StockOutboxProtocolV1.Root(tenant);
        await using var queue = new StockOutboxContextV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(source.Database.GetDbConnection(), contextOwnsConnection: false).Options);
        await queue.Database.UseTransactionAsync(native, ct);
        if(!ReferenceEquals(queue.Database.GetDbConnection(), source.Database.GetDbConnection())
            || !ReferenceEquals(queue.Database.CurrentTransaction!.GetDbTransaction(), native))
            throw new InvalidOperationException("STOCK_OUTBOX_NATIVE_TRANSACTION_MISMATCH");
        // Same lock order as the dispatch adapter: actual tenant, authority row, route root.
        await queue.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"={tenant} FOR UPDATE", ct);
        var authority = await queue.Set<StockOutboxAuthorityV1>().AsNoTracking().SingleOrDefaultAsync(a => a.RootId == root.Id, ct);
        if(authority == null)
        {
            queue.Add(new StockOutboxAuthorityV1 { RootId = root.Id, TenantId = tenant });
            queue.Add(new OutboxRouteControlV2 { Id = root.Id, OwnerId = root.OwnerId, TenantId = tenant, QueueDomainId = root.QueueDomainId,
                RouteId = root.RouteId, CurrentEpoch = 1, Phase = OutboxPhaseV2.Active, LockKey = StockOutboxProtocolV1.RouteLock(root),
                PublisherSetReference = StockOutboxProtocolV1.Publisher });
            await queue.SaveChangesAsync(ct);
        }
        await queue.Database.ExecuteSqlInterpolatedAsync($"SELECT \"RootId\" FROM cp6_stock_outbox_v1.\"Authority\" WHERE \"RootId\"={root.Id} FOR UPDATE", ct);
        authority = await queue.Set<StockOutboxAuthorityV1>().AsNoTracking().SingleAsync(a => a.RootId == root.Id, ct);
        if(!authority.Enabled || authority.TenantId != tenant || authority.PolicyVersion != StockOutboxProtocolV1.PolicyVersion
            || authority.PublisherReference != StockOutboxProtocolV1.Publisher)
            throw new InvalidOperationException("STOCK_OUTBOX_ROUTE_UNAVAILABLE");
        await queue.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({StockOutboxProtocolV1.RouteLock(root)})", ct);
        var control = await queue.Set<OutboxRouteControlV2>().FromSqlInterpolated($"SELECT *,xmin FROM cp6_stock_outbox_v1.\"Cp6_OutboxRouteV2\" WHERE \"Id\"={root.Id} FOR UPDATE")
            .AsNoTracking().SingleAsync(ct);
        if(control.Phase != OutboxPhaseV2.Active || control.CurrentEpoch != 1 || control.PublisherSetReference != StockOutboxProtocolV1.Publisher
            || control.OwnerId != root.OwnerId || control.TenantId != tenant || control.QueueDomainId != root.QueueDomainId
            || control.RouteId != root.RouteId || control.ProfileId != OutboxEncodingV2.Profile || control.ProfileVersion != 1
            || control.LockKey != StockOutboxProtocolV1.RouteLock(root))
            throw new InvalidOperationException("STOCK_OUTBOX_ROUTE_UNAVAILABLE");
        var message = new Cp6OutboxStore<StockOutboxContextV1>(queue, new StockOutboxEnvelopeValidatorV1())
            .Enqueue(StockOutboxProtocolV1.Envelope(tenant, operation));
        await queue.SaveChangesAsync(ct);
        queue.Add(new OutboxLeaseBindingV2 { NativeId = message.Id, RootId = root.Id, MessageId = message.MessageId,
            RawEnvelopeSha256 = message.PayloadSha256, AssignedEpoch = control.CurrentEpoch });
        queue.Add(new StockOutboxSourceV1 { NativeId = message.Id, TenantId = tenant, OperationId = operation,
            ResultSha256 = MainOwnerOperationJournalV1.Digest(original.Result) });
        await queue.SaveChangesAsync(ct);
    }
}
