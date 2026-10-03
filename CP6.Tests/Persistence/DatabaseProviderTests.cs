using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CP6.Tests.Persistence;

public sealed class DatabaseProviderTests
{
    private const string SqlConnectionString = "Server=localhost;Database=provider_fixture;User Id=fixture;Password=secret-value;MultipleActiveResultSets=False;TrustServerCertificate=True";
    private const string PostgreSqlConnectionString = "Host=localhost;Database=provider_fixture;Username=fixture;Password=secret-value;Search Path=public";

    [Fact]
    public void Missing_provider_defaults_to_sql_server_without_guessing_from_connection_string()
    {
        var configuration = Configuration(new() { ["ConnectionStrings:DefaultConnection"] = PostgreSqlConnectionString });

        Assert.Equal(DatabaseProvider.SqlServer, DatabaseOptions.FromConfiguration(configuration).Provider);
    }

    [Theory]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("PostgreSql", DatabaseProvider.PostgreSql)]
    public void Canonical_provider_value_selects_requested_provider(string value, DatabaseProvider expected)
    {
        Assert.Equal(expected, DatabaseOptions.FromConfiguration(Configuration(new() { ["Database:Provider"] = value })).Provider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Postgres")]
    [InlineData("PostgreSQL")]
    [InlineData("sqlserver")]
    [InlineData("SqlServer ")]
    [InlineData("Host=localhost;Password=secret-value")]
    public void Explicit_invalid_provider_fails_without_echoing_input(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseOptions.FromConfiguration(Configuration(new() { ["Database:Provider"] = value })));

        Assert.Equal("Database:Provider must be SqlServer or PostgreSql.", exception.Message);
        Assert.DoesNotContain("secret-value", exception.ToString());
    }

    [Fact]
    public void Provider_section_without_a_scalar_value_is_invalid()
    {
        var configuration = Configuration(new() { ["Database:Provider:Unexpected"] = "SqlServer" });

        Assert.Throws<InvalidOperationException>(() => DatabaseOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void Selected_provider_does_not_change_when_configuration_changes()
    {
        var configuration = Configuration(new() { ["Database:Provider"] = "SqlServer" });
        var selected = DatabaseOptions.FromConfiguration(configuration);

        configuration["Database:Provider"] = "PostgreSql";

        Assert.Equal(DatabaseProvider.SqlServer, selected.Provider);
    }

    [Fact]
    public void Unknown_enum_value_cannot_bypass_provider_validation()
    {
        Assert.Throws<InvalidOperationException>(() => new DatabaseOptions((DatabaseProvider)999));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Connection_factory_creates_a_new_unopened_connection_for_each_call(DatabaseProvider provider)
    {
        var factory = new DatabaseConnectionFactory(new(provider));
        using var first = factory.Create(ConnectionString(provider));
        using var second = factory.Create(ConnectionString(provider));

        Assert.NotSame(first, second);
        Assert.Equal(ConnectionState.Closed, first.State);
        Assert.Equal(ConnectionState.Closed, second.State);
        Assert.Equal(ConnectionString(provider), first.ConnectionString);
        Assert.Equal(provider == DatabaseProvider.SqlServer ? typeof(SqlConnection) : typeof(NpgsqlConnection), first.GetType());
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, null)]
    [InlineData(DatabaseProvider.SqlServer, "")]
    [InlineData(DatabaseProvider.PostgreSql, " ")]
    [InlineData(DatabaseProvider.SqlServer, "Server=localhost;Password=secret-value;secret-value=invalid")]
    [InlineData(DatabaseProvider.PostgreSql, "Host=localhost;Password=secret-value;secret-value=invalid")]
    public void Connection_factory_rejects_invalid_connection_strings_without_exposing_credentials(DatabaseProvider provider, string? connectionString)
    {
        var factory = new DatabaseConnectionFactory(new(provider));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(connectionString!));

        Assert.DoesNotContain("secret-value", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void String_configuration_selects_only_one_provider_and_preserves_the_typed_builder(DatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ProviderFixtureContext>();

        var configured = DatabaseContextOptions.Configure(builder, new(provider), ConnectionString(provider));

        Assert.Same(builder, configured);
        Assert.Single(configured.Options.Extensions, extension => extension.Info.IsDatabaseProvider);
        using var context = new ProviderFixtureContext(configured.Options);
        Assert.Equal(ProviderName(provider), context.Database.ProviderName);
        Assert.Equal(ConnectionString(provider), context.Database.GetConnectionString());
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Untyped_builder_is_also_supported(DatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder();

        var configured = DatabaseContextOptions.Configure(builder, new(provider), ConnectionString(provider));

        Assert.Same(builder, configured);
        using var context = new DbContext(configured.Options);
        Assert.Equal(ProviderName(provider), context.Database.ProviderName);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Existing_connection_is_shared_and_remains_owned_by_caller(DatabaseProvider provider)
    {
        using var connection = new DatabaseConnectionFactory(new(provider)).Create(ConnectionString(provider));
        var firstBuilder = new DbContextOptionsBuilder<ProviderFixtureContext>();
        var secondBuilder = new DbContextOptionsBuilder();
        DatabaseContextOptions.Configure(firstBuilder, new(provider), connection);
        DatabaseContextOptions.Configure(secondBuilder, new(provider), connection);

        using (var first = new ProviderFixtureContext(firstBuilder.Options))
        using (var second = new DbContext(secondBuilder.Options))
        {
            Assert.Same(connection, first.Database.GetDbConnection());
            Assert.Same(connection, second.Database.GetDbConnection());
            Assert.False(RelationalOptionsExtension.Extract(firstBuilder.Options).IsConnectionOwned);
            Assert.False(RelationalOptionsExtension.Extract(secondBuilder.Options).IsConnectionOwned);
        }

        Assert.Equal(ConnectionString(provider), connection.ConnectionString);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Existing_connection_of_wrong_provider_fails_before_configuring_builder(DatabaseProvider selected)
    {
        var other = selected == DatabaseProvider.SqlServer ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer;
        using var connection = new DatabaseConnectionFactory(new(other)).Create(ConnectionString(other));
        var builder = new DbContextOptionsBuilder<ProviderFixtureContext>();

        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseContextOptions.Configure(builder, new(selected), connection));

        Assert.DoesNotContain("secret-value", exception.ToString());
        Assert.DoesNotContain(builder.Options.Extensions, extension => extension.Info.IsDatabaseProvider);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Explicit_migration_metadata_applies_to_selected_provider(DatabaseProvider provider)
    {
        var builder = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ProviderFixtureContext>(), new(provider),
            ConnectionString(provider), migrationsAssembly: "CP6.Migrations.SelectedProvider",
            migrationsHistoryTable: "__ProviderFixtureHistory", migrationsHistorySchema: "fixture_schema");
        var relational = RelationalOptionsExtension.Extract(builder.Options);

        Assert.Equal("CP6.Migrations.SelectedProvider", relational.MigrationsAssembly);
        Assert.Equal("__ProviderFixtureHistory", relational.MigrationsHistoryTableName);
        Assert.Equal("fixture_schema", relational.MigrationsHistoryTableSchema);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Existing_connection_supports_explicit_migration_metadata(DatabaseProvider provider)
    {
        using var connection = new DatabaseConnectionFactory(new(provider)).Create(ConnectionString(provider));
        var builder = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ProviderFixtureContext>(), new(provider),
            connection, migrationsAssembly: "CP6.Migrations.SelectedProvider", migrationsHistoryTable: "__ProviderFixtureHistory",
            migrationsHistorySchema: "fixture_schema");
        var relational = RelationalOptionsExtension.Extract(builder.Options);

        Assert.Same(connection, relational.Connection);
        Assert.Equal("CP6.Migrations.SelectedProvider", relational.MigrationsAssembly);
        Assert.Equal("__ProviderFixtureHistory", relational.MigrationsHistoryTableName);
        Assert.Equal("fixture_schema", relational.MigrationsHistoryTableSchema);
    }

    [Fact]
    public void Sql_server_existing_migration_selection_is_not_replaced_by_implicit_defaults()
    {
        var builder = new DbContextOptionsBuilder<ProviderFixtureContext>().UseSqlServer(SqlConnectionString, sql =>
            sql.MigrationsAssembly("CP6.Core").MigrationsHistoryTable("__EFMigrationsHistory_Space", "existing_schema"));

        DatabaseContextOptions.Configure(builder, new(DatabaseProvider.SqlServer), SqlConnectionString);
        var relational = RelationalOptionsExtension.Extract(builder.Options);

        Assert.Equal("CP6.Core", relational.MigrationsAssembly);
        Assert.Equal("__EFMigrationsHistory_Space", relational.MigrationsHistoryTableName);
        Assert.Equal("existing_schema", relational.MigrationsHistoryTableSchema);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Builder_already_configured_for_other_provider_is_rejected(DatabaseProvider selected)
    {
        var builder = new DbContextOptionsBuilder<ProviderFixtureContext>();
        if (selected == DatabaseProvider.SqlServer)
            builder.UseNpgsql(PostgreSqlConnectionString);
        else
            builder.UseSqlServer(SqlConnectionString);

        Assert.Throws<InvalidOperationException>(() => DatabaseContextOptions.Configure(builder, new(selected), ConnectionString(selected)));
        Assert.Single(builder.Options.Extensions, extension => extension.Info.IsDatabaseProvider);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Missing_history_table_with_explicit_schema_fails_instead_of_choosing_a_table(DatabaseProvider provider)
    {
        Assert.Throws<ArgumentException>(() => DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(provider),
            ConnectionString(provider), migrationsHistorySchema: "fixture_schema"));
    }

    private static IConfigurationRoot Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string ConnectionString(DatabaseProvider provider)
        => provider == DatabaseProvider.SqlServer ? SqlConnectionString : PostgreSqlConnectionString;

    private static string ProviderName(DatabaseProvider provider)
        => provider == DatabaseProvider.SqlServer ? "Microsoft.EntityFrameworkCore.SqlServer" : "Npgsql.EntityFrameworkCore.PostgreSQL";

    private sealed class ProviderFixtureContext(DbContextOptions<ProviderFixtureContext> options) : DbContext(options);
}
