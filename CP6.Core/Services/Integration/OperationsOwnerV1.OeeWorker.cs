using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Integration;

/// <summary>Assembly-internal capability issued only by the fixed host tenant loop, never a browser/user-name input.</summary>
internal sealed record OeeWorkerInvocationV1(CP6Context Context, Guid Tenant);

internal interface IOeeWorkerV1
{
    Task<int> RecalculateAsync(CP6.Entity.DTOs.Mes.OeeRecalcRequest request, Guid enumeratedTenant, CancellationToken ct);
}

public sealed partial class OperationsOwnerV1
{
    internal async Task<T> ExecuteOeeWorkerAsync<T>(OeeWorkerInvocationV1 invocation,
        Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (!ReferenceEquals(invocation.Context, db) || invocation.Tenant == Guid.Empty || db.CurrentTenantId != invocation.Tenant
            || !db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries().Any(e => IsBusiness(e.Entity.GetType().Name)))
            throw new OrderPriceCorrectionException("OEE_WORKER_SCOPE_UNAVAILABLE", 503);
        var retained = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        IDbContextTransaction? tx = null; var committing = false;
        async Task<Guid> RequireTenant()
        {
            var now = DateTime.Now;
            var revision = await db.Sys_Tenants.AsNoTracking().Where(t => t.Id == invocation.Tenant && t.Enable
                && (t.ExpireDate == null || t.ExpireDate > now)).Select(t => (Guid?)t.AuthenticationPolicyRevision).SingleOrDefaultAsync(ct);
            if (db.CurrentTenantId != invocation.Tenant || revision == null)
                throw new OrderPriceCorrectionException("OEE_WORKER_TENANT_UNAVAILABLE", 403);
            return revision.Value;
        }
        try
        {
            tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            await LockGraph(true, ct);
            var original = await RequireTenant();
            var value = await body(ct);
            if (original != await RequireTenant()) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return value;
        }
        catch (Exception error)
        {
            Exception? rollback = null;
            if (tx != null) try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception failure) { rollback = failure; }
            if (committing || rollback != null) throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN", 503,
                rollback == null ? error : new AggregateException(error, rollback));
            if (error is OrderPriceCorrectionException or DbUpdateConcurrencyException) throw;
            if (error is OperationCanceledException && ct.IsCancellationRequested) throw;
            throw new OrderPriceCorrectionException("OEE_WORKER_SCOPE_UNAVAILABLE", 503, error);
        }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) if (!retained.Contains(entry.Entity)) entry.State = EntityState.Detached;
            if (tx != null) await tx.DisposeAsync();
        }
    }
}
