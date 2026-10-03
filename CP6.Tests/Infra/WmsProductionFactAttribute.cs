using CP6.DatabaseCompatibility.Testing;
using Xunit;

namespace CP6.Tests.Infra;

/// <summary>Explicit WP4 database inputs make these tests required; legacy SQL discovery keeps its existing skip behavior.</summary>
public sealed class WmsProductionFactAttribute : FactAttribute
{
    public const string ProviderVariable = "CP6_WMS_TEST_PROVIDER";
    public const string ConnectionVariable = "CP6_WMS_TEST_CONNECTION";
    public const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";

    public WmsProductionFactAttribute()
    {
        if (!UsesTaskOwnedInputs)
            Skip = new SqlServerFactAttribute().Skip;
    }

    internal static bool UsesTaskOwnedInputs =>
        Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null
        || OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.CoreWms);
}
