using CP6.Core.Persistence;

namespace CP6.WebApi.Configuration;

internal static class DatabaseRuntimeSupport
{
    public static void EnsureSupported(DatabaseOptions database, bool databaseInitializationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Provider == DatabaseProvider.PostgreSql && !databaseInitializationOnly)
            throw new InvalidOperationException(
                "Database:Provider=PostgreSql is not yet supported by the application runtime. " +
                "Database compatibility work must complete model, migrations, SQL and messaging validation before startup is enabled.");
    }
}
