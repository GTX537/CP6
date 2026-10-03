using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

internal static class SpaceResourceLocks
{
    public static async Task<bool> TryAcquireTransactionAsync(
        DbContext context, string resource, int timeoutMs, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return true;
        try
        {
            return await DatabaseResourceLocks.TryAcquireTransactionAsync(context, resource, timeoutMs, cancellationToken);
        }
        catch (DatabaseResourceLockDeadlockException exception) when (exception.Provider == DatabaseProvider.SqlServer)
        {
            // Existing Space callers map native application-lock -3 to their domain busy error.
            // Transaction isolation, rollback and any fresh-context retry remain caller-owned.
            return false;
        }
    }
}
