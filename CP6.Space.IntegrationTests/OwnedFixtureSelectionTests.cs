namespace CP6.Space.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OwnedFixtureSelectionCollection
{
    public const string Name = "WP6 Space fixture selection environment";
}

[Collection(OwnedFixtureSelectionCollection.Name)]
public sealed class OwnedFixtureSelectionTests
{
    [Fact]
    public void Space_role_without_business_aliases_rejects_before_connection_and_does_not_select_history()
    {
        using var environment = new SelectionEnvironment("space");
        Assert.True(SpaceRelationalFixture.IsSelected);
        Assert.False(SpaceMigrationTestDatabase.IsSelected);
        Assert.Null(new SqlServerFactAttribute().Skip);
        Assert.NotNull(new SpaceMigrationFactAttribute().Skip);
        var error = Assert.Throws<InvalidOperationException>(() => new SpaceRelationalFixture());
        Assert.Contains("CP6_SPACE_TEST_PROVIDER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_role_without_history_aliases_rejects_before_connection_and_does_not_select_business()
    {
        using var environment = new SelectionEnvironment("spacehistory");
        Assert.True(SpaceMigrationTestDatabase.IsSelected);
        Assert.False(SpaceRelationalFixture.IsSelected);
        Assert.NotNull(new SqlServerFactAttribute().Skip);
        var error = Assert.Throws<InvalidOperationException>(() => new SpaceMigrationFactAttribute());
        Assert.Contains("CP6_SPACE_MIGRATION_TEST_PROVIDER", error.Message, StringComparison.Ordinal);
        // OpenIfSelectedAsync must fail before it can dereference output or open any connection.
        var openError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SpaceMigrationTestDatabase.OpenIfSelectedAsync(null!));
        Assert.Equal(error.Message, openError.Message);
    }

    [Fact]
    public void Other_valid_role_does_not_activate_space_discovery_or_migration_validation()
    {
        using var environment = new SelectionEnvironment("corewms");
        Assert.False(SpaceRelationalFixture.IsSelected);
        Assert.False(SpaceMigrationTestDatabase.IsSelected);
        Assert.NotNull(new SqlServerFactAttribute().Skip);
        Assert.NotNull(new SpaceMigrationFactAttribute().Skip);
    }

    [Fact]
    public async Task No_wp6_scope_preserves_unselected_legacy_behavior()
    {
        using var environment = new SelectionEnvironment(null);
        Assert.False(SpaceRelationalFixture.IsSelected);
        Assert.False(SpaceMigrationTestDatabase.IsSelected);
        Assert.NotNull(new SqlServerFactAttribute().Skip);
        Assert.NotNull(new SpaceMigrationFactAttribute().Skip);
        await new SpaceRelationalFixture().InitializeAsync();
        Assert.Null(await SpaceMigrationTestDatabase.OpenIfSelectedAsync(null!));
    }

    private sealed class SelectionEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> previous;

        public SelectionEnvironment(string? role)
        {
            string[] keys = ["CP6_COMPAT_SCOPE", "CP6_COMPAT_DATABASE_NAME", "CP6_TEST_DATABASE_OWNER",
                "CP6_SPACE_TEST_PROVIDER", "CP6_SPACE_TEST_CONNECTION", "CP6_SPACE_MIGRATION_TEST_PROVIDER",
                "CP6_SPACE_MIGRATION_TEST_CONNECTION", "CP6_TEST_SQLSERVER", "CP6_TEST_POSTGRES"];
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
