using CP6.Tests.DatabaseCompatibility;
using CP6.Tests.Infra;

namespace CP6.Tests.Persistence;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OwnedFixtureSelectionCollection
{
    public const string Name = "WP6 fixture selection environment";
}

[Collection(OwnedFixtureSelectionCollection.Name)]
public sealed class OwnedFixtureSelectionTests
{
    [Fact]
    public void Core_role_without_core_aliases_is_required_and_rejected_before_connection()
    {
        using var environment = new SelectionEnvironment("corewms");
        Assert.Null(new CoreBusinessFactAttribute().Skip);
        var error = Assert.Throws<InvalidOperationException>(() => new CoreBusinessRelationalFixture());
        Assert.Contains("CP6_CORE_TEST_PROVIDER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_role_without_reports_aliases_is_required_and_rejected_before_connection()
    {
        using var environment = new SelectionEnvironment("reports");
        Assert.Null(new Wp5ReportsFactAttribute().Skip);
        var error = Assert.Throws<InvalidOperationException>(() => new Wp5ReportsRelationalFixture());
        Assert.Equal("WP5_REPORTS_PROVIDER_INVALID: explicitly select SqlServer or PostgreSql.", error.Message);
    }

    [Fact]
    public async Task Wms_role_without_wms_aliases_rejects_selection_instead_of_taking_legacy_sql()
    {
        using var environment = new SelectionEnvironment("corewms");
        Assert.Null(new WmsProductionFactAttribute().Skip);
        var fixture = new WmsProductionSqlFixture();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync());
        Assert.Equal("WMS_REQUIRED_DATABASE_PROVIDER_INVALID", error.Message);
        Assert.Equal(string.Empty, fixture.ConnectionString);
    }

    [Theory]
    [InlineData("space")]
    [InlineData("runtime")]
    public void Other_valid_roles_leave_unselected_core_reports_and_wms_cases_inactive(string role)
    {
        using var environment = new SelectionEnvironment(role);
        Assert.NotNull(new CoreBusinessFactAttribute().Skip);
        Assert.NotNull(new Wp5ReportsFactAttribute().Skip);
        Assert.NotNull(new WmsProductionFactAttribute().Skip);
    }

    [Fact]
    public async Task Shared_owner_without_wp6_selection_preserves_legacy_unselected_behavior()
    {
        using var environment = new SelectionEnvironment(null);
        Assert.NotNull(new CoreBusinessFactAttribute().Skip);
        Assert.NotNull(new Wp5ReportsFactAttribute().Skip);
        Assert.NotNull(new WmsProductionFactAttribute().Skip);
        await new CoreBusinessRelationalFixture().InitializeAsync();
        await new Wp5ReportsRelationalFixture().InitializeAsync();
        await new WmsProductionSqlFixture().InitializeAsync();
    }

    private sealed class SelectionEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> previous;

        public SelectionEnvironment(string? role)
        {
            string[] keys = ["CP6_COMPAT_SCOPE", "CP6_COMPAT_DATABASE_NAME", "CP6_TEST_DATABASE_OWNER",
                "CP6_CORE_TEST_PROVIDER", "CP6_CORE_TEST_CONNECTION", "CP6_WP5_CORE_TEST_PROVIDER",
                "CP6_WP5_CORE_TEST_CONNECTION", "CP6_WMS_TEST_PROVIDER", "CP6_WMS_TEST_CONNECTION",
                "CP6_TEST_SQLSERVER", "CP6_TEST_POSTGRES"];
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
