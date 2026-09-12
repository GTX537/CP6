using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.ErpIntegration;

internal static class ErpSqlLock
{
    public static async Task AcquireAsync(DbContext db, string resource, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("C03_REQUIRES_SQL_TRANSACTION");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new TimeoutException("C03_SQL_CONTENTION");
    }
}
