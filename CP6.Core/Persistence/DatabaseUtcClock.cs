using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Persistence;

/// <summary>Reads the database's advancing UTC wall clock, including inside a long transaction.</summary>
public static class DatabaseUtcClock
{
    /// <summary>
    /// SQL Server uses SYSUTCDATETIME; PostgreSQL uses clock_timestamp, rather than transaction time.
    /// The result is a DateTime with Utc Kind. This does not change business date/default semantics.
    /// </summary>
    public static async Task<DateTime> ReadUtcNowAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = context.Database.IsSqlServer()
            ? "SELECT SYSUTCDATETIME() AS [Value]"
            : context.Database.IsNpgsql()
                ? "SELECT clock_timestamp() AS \"Value\""
                : throw new InvalidOperationException("Database UTC clock requires SQL Server or PostgreSQL.");
        cancellationToken.ThrowIfCancellationRequested();
        var now = await context.Database.SqlQueryRaw<DateTime>(query).SingleAsync(cancellationToken);
        return DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
