using CP6.Core.Persistence;

namespace CP6.WebApi.Configuration;

internal static class DatabaseRuntimeSupport
{
    public static void EnsureSupported(DatabaseOptions database, bool databaseInitializationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        // DatabaseOptions rejects unknown providers. Both supported providers use
        // the same startup path for the API and one-shot initialization.
    }
}
