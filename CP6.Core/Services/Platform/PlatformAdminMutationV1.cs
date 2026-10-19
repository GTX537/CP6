using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Platform;

internal static class PlatformAdminMutationV1
{
    // A stable database row also serializes the native DML triggers. Updating it (rather than
    // only locking it) rejects a stale repeatable-read snapshot with PostgreSQL 40001.
    internal static async Task ExecuteAsync(CP6Context db, Func<CancellationToken, Task> mutation, CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("PLATFORM_ADMIN_REQUIRES_CLEAN_CONTEXT");
        var original = db.ChangeTracker.Entries().ToDictionary(x => x.Entity, x => x.CurrentValues.Clone());
        var caller = db.Database.CurrentTransaction;
        if (caller is not null && !caller.SupportsSavepoints)
            throw new InvalidOperationException("PLATFORM_ADMIN_REQUIRES_SAVEPOINTS");
        await using var owned = caller is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var savepoint = "platform_admin_" + Guid.NewGuid().ToString("N")[..16];
        var saved = false;
        try
        {
            if (caller is not null) { await caller.CreateSavepointAsync(savepoint, ct); saved = true; }
            await db.Database.SqlQueryRaw<long>("SELECT cp6_platform_admin_guard_v1.acquire() AS \"Value\"").SingleAsync(ct);
            await mutation(ct);
            if (owned is not null) await owned.CommitAsync(ct);
            else await caller!.ReleaseSavepointAsync(savepoint, ct);
        }
        catch (Exception error)
        {
            try
            {
                if (owned is not null) await owned.RollbackAsync(CancellationToken.None);
                else if (saved) await caller!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            catch (Exception rollback) { throw new AggregateException("PLATFORM_ADMIN_TRANSACTION_OUTCOME_UNKNOWN", error, rollback); }
            // No unrelated pending changes were accepted. Restore pre-command tracking so a
            // caller can continue its transaction without accidentally saving a failed grant.
            foreach (var entry in db.ChangeTracker.Entries().ToArray())
            {
                if (!original.TryGetValue(entry.Entity, out var values)) entry.State = EntityState.Detached;
                else { entry.CurrentValues.SetValues(values); entry.OriginalValues.SetValues(values); entry.State = EntityState.Unchanged; }
            }
            if (IsSurvivorViolation(error)) throw new InvalidOperationException("E-SEC-037", error);
            throw;
        }
    }

    internal static bool IsSurvivorViolation(Exception error)
        => error is PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: "cp6_platform_admin_survivor_v1" }
            || error is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: "cp6_platform_admin_survivor_v1" } };
}
