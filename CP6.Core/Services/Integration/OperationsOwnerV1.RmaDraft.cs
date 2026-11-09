using System.Data;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

public sealed partial class OperationsOwnerV1
{
    internal async Task<RmaApplicationResultV1> ExecuteRmaDraftAsync(TenantRmaReadStore storage, Guid operation,
        Func<OperationsAccessV1, RmaTenantReadContext, MainOwnerOperationJournalV1.Original?, CancellationToken,
            Task<RmaApplicationResultV1>> body, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct = linked.Token;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries().Any(e => IsBusiness(e.Entity.GetType().Name)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

        var committed = false;
        try
        {
            // Only the ordinary transaction takes the authentication root. Keep the shared read fences
            // on this connection until the ordinary transaction finishes; this connection performs no DML.
            await using var authorization = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var subject = await currentActor(ct);
            var result = await storage.WriteDraftAsync(db, subject, async (tenantDb, token) =>
            {
                await LockGraph(false, token, Rma);
                var current = await currentActor(token);
                if (current != subject) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                var original = await MainOwnerOperationJournalV1.Read(tenantDb, tenantDb.Binding.TenantId,
                    RmaApplicationOwnerV1.JournalOwner, operation, token);
                var authority = await AuthorityCoreAsync(Rma, original is null ? "add" : "query", current, false, token);
                var access = new OperationsAccessV1(db, this, authority);
                var value = await body(access, tenantDb, original, token);
                await ValidateFinalAuthorityAsync(access, subject, token);
                return value;
            }, ct);
            committed = true;
            return result;
        }
        catch (Exception error) when (committed)
        {
            throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN", 503, error);
        }
        catch (Exception error) when (!IsExpectedOwnerFailure(error, ct))
        {
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
        }
    }
}
