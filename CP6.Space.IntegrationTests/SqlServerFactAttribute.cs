namespace CP6.Space.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvVar = "CP6_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar} to run SQL Server integration tests.";
    }
}
