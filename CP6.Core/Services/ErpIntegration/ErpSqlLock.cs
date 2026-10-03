using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

internal static class ErpSqlLock
{
    public static async Task AcquireAsync(DbContext db, string resource, CancellationToken ct)
    {
        if (!(db.Database.IsSqlServer() || db.Database.IsNpgsql()) || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("C03_REQUIRES_SQL_TRANSACTION");
        try
        {
            if (!await DatabaseResourceLocks.TryAcquireTransactionAsync(db, resource, 10000, ct))
                throw new TimeoutException("C03_SQL_CONTENTION");
        }
        catch (DatabaseResourceLockDeadlockException exception) when (exception.Provider == DatabaseProvider.SqlServer)
        {
            throw new TimeoutException("C03_SQL_CONTENTION", exception);
        }
    }
}
