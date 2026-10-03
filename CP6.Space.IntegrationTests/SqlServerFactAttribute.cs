namespace CP6.Space.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvVar = "CP6_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (!SpaceRelationalFixture.IsSelected && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar} to run SQL Server integration tests.";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LegacySqlServerFactAttribute : FactAttribute
{
    public LegacySqlServerFactAttribute()
    {
        if (SpaceRelationalFixture.IsSelected || SpaceMigrationTestDatabase.IsSelected)
            Skip = "Legacy SQL Server recovery requires its separate database lifecycle outside the selected Space test lanes.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)))
            Skip = $"Set {SqlServerFactAttribute.EnvVar} to run legacy SQL Server recovery tests.";
    }
}
