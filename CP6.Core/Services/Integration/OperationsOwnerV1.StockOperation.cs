using System.Data;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

public sealed partial class OperationsOwnerV1
{
    internal async Task<T> ExecuteStockOperationAsync<T>(TenantStockReadStore storage, StockWriteEntry entry,
        Func<StockMovementWriteContextV1, OperationsAccessV1, CancellationToken, Task<T>> body,
        Func<StockMovementWriteContextV1, CancellationToken, Task<string>> actionAtFence,
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct = linked.Token;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries().Any(e => IsBusiness(e.Entity.GetType().Name)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

        var committed = false;
        try
        {
            // Current signed session and field policy are read through the existing authority connection.
            // The ordinary transaction acquires every write fence and owns all business reads and writes.
            await using var authorization = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            var subject = await currentActor(ct);
            var result = await storage.WriteAsync(db, subject, entry, async (writer, token) =>
            {
                await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_adjust_owner_v1.lock_graph({writer.CurrentTenantId})", token);
                var current = await currentActor(token);
                if (current != subject) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                var action = await actionAtFence(writer, token);
                var authority = await AuthorityCoreAsync(Stock, action, current, false, token, writer);
                var access = new OperationsAccessV1(writer, this, authority);
                var result = await body(writer, access, token);
                // The new department mapping revisions are visible in the ordinary transaction before commit.
                await ValidateFinalAuthorityAsync(access, subject, token, writer);
                return result;
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
