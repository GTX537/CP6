using System.Reflection;

namespace CP6.Space.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpaceLegacyRecoverySelectionCollection
{
    public const string Name = "Space legacy recovery selection environment";
}

[Collection(SpaceLegacyRecoverySelectionCollection.Name)]
public sealed class SpaceLegacyRecoverySelectionTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Selected_Space_provider_does_not_enable_legacy_backup_restore(string provider)
    {
        var variables = new[]
        {
            SpaceRelationalFixture.ProviderVariable,
            SpaceRelationalFixture.ConnectionVariable,
            SpaceMigrationTestDatabase.ProviderVariable,
            SpaceMigrationTestDatabase.ConnectionVariable,
            SqlServerFactAttribute.EnvVar
        };
        var original = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            const string sql = "Server=localhost;Database=wp5_selector_only;Integrated Security=true;TrustServerCertificate=true";
            var selectedConnection = provider == "PostgreSql"
                ? "Host=localhost;Database=wp5_selector_only;Username=unused"
                : sql;
            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ProviderVariable, null);
            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ConnectionVariable, null);
            Environment.SetEnvironmentVariable(SpaceRelationalFixture.ProviderVariable, provider);
            Environment.SetEnvironmentVariable(SpaceRelationalFixture.ConnectionVariable, selectedConnection);
            Environment.SetEnvironmentVariable(SqlServerFactAttribute.EnvVar, sql);

            // Inspect the actual recovery fact without constructing its class or invoking its database lifecycle.
            var method = typeof(SpaceReleaseRehearsalRecoverySqlServerTests).GetMethod(
                nameof(SpaceReleaseRehearsalRecoverySqlServerTests.Checksum_backup_restore_preserves_published_and_wms_state))!;
            var fact = method.GetCustomAttribute<FactAttribute>();
            Assert.NotNull(fact);
            Assert.False(string.IsNullOrWhiteSpace(fact.Skip),
                "Selecting a WP5 Space provider must not enable the legacy SQL backup/restore fact, even when CP6_TEST_SQLSERVER is set.");

            Environment.SetEnvironmentVariable(SpaceRelationalFixture.ProviderVariable, null);
            Environment.SetEnvironmentVariable(SpaceRelationalFixture.ConnectionVariable, null);
            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ProviderVariable, provider);
            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ConnectionVariable, selectedConnection);
            Assert.False(string.IsNullOrWhiteSpace(method.GetCustomAttribute<FactAttribute>()!.Skip),
                "Selecting the owned Space migration lane must not enable the legacy SQL backup/restore lifecycle.");

            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ProviderVariable, null);
            Environment.SetEnvironmentVariable(SpaceMigrationTestDatabase.ConnectionVariable, null);
            Assert.Null(method.GetCustomAttribute<FactAttribute>()!.Skip);
            Environment.SetEnvironmentVariable(SqlServerFactAttribute.EnvVar, null);
            Assert.False(string.IsNullOrWhiteSpace(method.GetCustomAttribute<FactAttribute>()!.Skip),
                "The unselected legacy lane must still require CP6_TEST_SQLSERVER.");
        }
        finally
        {
            foreach (var variable in original)
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
        }
    }
}
