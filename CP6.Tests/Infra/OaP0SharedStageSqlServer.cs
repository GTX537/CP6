using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace CP6.Tests.Infra;

internal static partial class OaP0SharedStageSqlServer
{
    public const string SharedStageEnvVar = "CP6_OA_P0_SHARED_STAGE";

    public static string? GetValidatedConnectionString()
    {
        var source = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(source)) return null;

        if (!string.Equals(
                Environment.GetEnvironmentVariable(SharedStageEnvVar),
                "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OA P0 SQL tests require the explicitly shared isolated-stage mode.");
        }

        var builder = new SqlConnectionStringBuilder(source);
        if (!StageDatabaseName().IsMatch(builder.InitialCatalog))
        {
            throw new InvalidOperationException(
                "OA P0 SQL tests require a validated CP6OaP0Stage database.");
        }
        return builder.ConnectionString;
    }

    [GeneratedRegex(
        "^CP6OaP0Stage_[0-9]{14}_[0-9a-f]{8}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StageDatabaseName();
}
