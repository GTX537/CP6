namespace CP6.ErpIntegration.SqlTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OwnedFixtureSelectionCollection
{
    public const string Name = "WP6 ERP fixture selection environment";
}

[Collection(OwnedFixtureSelectionCollection.Name)]
public sealed class OwnedFixtureSelectionTests
{
    [Fact]
    public void Erp_role_without_erp_aliases_rejects_constructor_before_legacy_database_creation()
    {
        using var environment = new SelectionEnvironment("erp");
        var error = Assert.Throws<InvalidOperationException>(() => new SqlDatabaseFixture());
        Assert.Contains("CP6_ERP_TEST_PROVIDER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_provider_without_connection_cannot_fall_back_to_legacy_sql()
    {
        using var environment = new SelectionEnvironment("erp");
        Environment.SetEnvironmentVariable("CP6_ERP_TEST_PROVIDER", "SqlServer");
        var error = Assert.Throws<InvalidOperationException>(() => new SqlDatabaseFixture());
        Assert.Equal("CP6_COMPAT_CONNECTION_REQUIRED", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("space")]
    public async Task Unselected_or_other_valid_role_preserves_an_unused_legacy_fixture(string? role)
    {
        using var environment = new SelectionEnvironment(role);
        var fixture = new SqlDatabaseFixture();
        Assert.False(fixture.IsPostgreSql);
        Assert.Contains("legacy isolated SQL database lifecycle", fixture.SetupSummary, StringComparison.Ordinal);
        // No InitializeAsync: legacy database creation is outside this pure selection contract.
        await fixture.DisposeAsync();
    }

    private sealed class SelectionEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> previous;

        public SelectionEnvironment(string? role)
        {
            string[] keys = ["CP6_COMPAT_SCOPE", "CP6_COMPAT_DATABASE_NAME", "CP6_TEST_DATABASE_OWNER",
                "CP6_ERP_TEST_PROVIDER", "CP6_ERP_TEST_CONNECTION", "CP6_C03_TEST_SQL"];
            previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable("CP6_TEST_DATABASE_OWNER", "a1b2c3d4e5f60718293a4b5c6d7e8f90");
            if (role is null) return;
            Environment.SetEnvironmentVariable("CP6_COMPAT_SCOPE", "WP6");
            Environment.SetEnvironmentVariable("CP6_COMPAT_DATABASE_NAME", "CP6Compat_WP6_20261003_a1b2c3d4_" + role);
        }

        public void Dispose()
        {
            foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }
}
