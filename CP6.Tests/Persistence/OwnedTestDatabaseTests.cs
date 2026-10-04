using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.Tests.Persistence;

public sealed class OwnedTestDatabaseTests
{
    private const string Owner = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string Name = "CP6Compat_WP6_20261101_a1b2c3d4_corewms";
    private const string Secret = "private-fixture-password";

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Explicit_owned_input_accepts_a_dynamic_date_and_preserves_the_selected_provider(DatabaseProvider provider)
    {
        var selected = Parse(provider);
        Assert.Equal(provider, selected.Database.Provider);
        Assert.Equal(Name, selected.DatabaseName);
        Assert.Equal(Owner, selected.Owner);
        Assert.Equal(DatabaseFixtureRole.CoreWms, selected.Role);
        Assert.DoesNotContain(Secret, selected.ToString());
    }

    [Fact]
    public void Shared_owner_alone_does_not_select_wp6_or_parse_an_unselected_connection()
    {
        string? Read(string key) => key == OwnedTestDatabase.OwnerVariable ? Owner : null;
        Assert.False(OwnedTestDatabase.IsRequested(Read));
        Assert.Null(OwnedTestDatabase.FromEnvironment(new(DatabaseProvider.SqlServer), "invalid-secret-input",
            [DatabaseFixtureRole.CoreWms], "CP6Compat.Tests", Read));
    }

    [Theory]
    [InlineData("CP6_COMPAT_SCOPE")]
    [InlineData("CP6_COMPAT_DATABASE_NAME")]
    public void Either_wp6_field_selects_strict_validation_even_when_empty(string key)
        => Assert.True(OwnedTestDatabase.IsRequested(name => name == key ? "" : null));

    [Fact]
    public void Matching_role_is_selected_without_provider_aliases_but_other_valid_roles_are_not()
    {
        string? Read(string key) => key switch
        {
            OwnedTestDatabase.ScopeVariable => "WP6",
            OwnedTestDatabase.NameVariable => Name,
            _ => null
        };
        Assert.True(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.CoreWms, Read));
        Assert.False(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.Space, Read));
        Assert.False(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.SpaceHistory, Read));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CP6Compat_WP5_20261101_a1b2c3d4_corewms")]
    [InlineData("CP6Compat_WP6_20260230_a1b2c3d4_spacehistory")]
    [InlineData("CP6Compat_WP6_20261101_a1b2c3d4_unknown")]
    public void Incomplete_or_invalid_wp6_name_cannot_suppress_a_required_fixture(string? name)
    {
        string? Read(string key) => key switch
        {
            OwnedTestDatabase.ScopeVariable => "WP6",
            OwnedTestDatabase.NameVariable => name,
            _ => null
        };
        Assert.True(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.CoreWms, Read));
        Assert.True(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.SpaceHistory, Read));
    }

    [Fact]
    public void Legacy_owner_without_wp6_selection_does_not_activate_any_modern_role()
    {
        string? Read(string key) => key == OwnedTestDatabase.OwnerVariable ? Owner : null;
        Assert.False(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.CoreWms, Read));
        Assert.False(OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.SpaceHistory, Read));
    }

    [Theory]
    [InlineData("WP6", null, Owner, "CP6_COMPAT_DATABASE_NAME_INVALID")]
    [InlineData(null, Name, Owner, "CP6_COMPAT_SCOPE_INVALID")]
    [InlineData("", Name, Owner, "CP6_COMPAT_SCOPE_INVALID")]
    [InlineData("wp6", Name, Owner, "CP6_COMPAT_SCOPE_INVALID")]
    [InlineData("WP6", Name, null, "CP6_COMPAT_OWNER_INVALID")]
    public void Partial_or_noncanonical_protocol_never_falls_back_to_a_legacy_lane(
        string? scope, string? name, string? owner, string expected)
    {
        string? Read(string key) => key switch
        {
            OwnedTestDatabase.ScopeVariable => scope,
            OwnedTestDatabase.NameVariable => name,
            OwnedTestDatabase.OwnerVariable => owner,
            _ => null
        };
        AssertSafe(expected, () => OwnedTestDatabase.FromEnvironment(new(DatabaseProvider.SqlServer), Sql(),
            [DatabaseFixtureRole.CoreWms], "CP6Compat.Tests", Read));
    }

    [Theory]
    [InlineData("", "CP6_COMPAT_OWNER_INVALID")]
    [InlineData("A1B2C3D4E5F60718293A4B5C6D7E8F90", "CP6_COMPAT_OWNER_INVALID")]
    [InlineData("a1b2c3d4", "CP6_COMPAT_OWNER_INVALID")]
    [InlineData("00112233445566778899aabbccddeeff", "CP6_COMPAT_DATABASE_OWNER_MISMATCH")]
    public void Owner_requires_full_lowercase_receipt_and_exact_database_prefix(string owner, string expected)
        => AssertSafe(expected, () => Parse(DatabaseProvider.SqlServer, owner: owner));

    [Theory]
    [InlineData("CP6Compat_WP5_20261101_a1b2c3d4_corewms")]
    [InlineData("CP6Compat_WP6_20260230_a1b2c3d4_corewms")]
    [InlineData("CP6Compat_WP6_2026111_a1b2c3d4_corewms")]
    [InlineData("CP6Compat_WP6_20261101_a1b2c3d4_corewms_extra")]
    [InlineData("CP6Compat_WP6_20261101_a1b2c3d4_unknown")]
    [InlineData("CP6Compat_WP6_20261101_a1b2c3d4_corewms\n")]
    public void Database_name_requires_valid_date_and_one_known_role(string name)
        => AssertSafe("CP6_COMPAT_DATABASE_NAME_INVALID", () => Parse(DatabaseProvider.SqlServer, name: name));

    [Fact]
    public void A_valid_database_of_another_role_is_rejected_before_connection()
        => AssertSafe("CP6_COMPAT_DATABASE_ROLE_MISMATCH", () => Parse(DatabaseProvider.SqlServer,
            name: Name.Replace("corewms", "spacehistory", StringComparison.Ordinal)));

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Actual_connection_database_must_equal_the_receipt_name(DatabaseProvider provider)
        => AssertSafe("CP6_COMPAT_CONNECTION_DATABASE_MISMATCH", () => Parse(provider,
            connection: provider == DatabaseProvider.SqlServer ? Sql("other") : Pg("other")));

    [Theory]
    [InlineData("localhost")]
    [InlineData("tcp:localhost,1433")]
    [InlineData("localhost\\NamedFixture")]
    [InlineData("127.0.0.1,1433")]
    [InlineData("[::1],1433")]
    public void Sql_endpoint_is_portable_across_literal_loopback_ports_and_instances(string endpoint)
    {
        var builder = new SqlConnectionStringBuilder(Sql()) { DataSource = endpoint };
        Assert.Equal(Name, Parse(DatabaseProvider.SqlServer, connection: builder.ConnectionString).DatabaseName);
    }

    [Theory]
    [InlineData("database.example")]
    [InlineData("localhost.example")]
    [InlineData(".")]
    [InlineData("(local)")]
    public void Sql_nonliteral_or_ambiguous_endpoint_is_rejected(string endpoint)
    {
        var builder = new SqlConnectionStringBuilder(Sql()) { DataSource = endpoint };
        AssertSafe("CP6_COMPAT_LOOPBACK_REQUIRED", () => Parse(DatabaseProvider.SqlServer, connection: builder.ConnectionString));
    }

    [Theory]
    [InlineData("MultipleActiveResultSets", "true", "CP6_COMPAT_SQL_MARS_FORBIDDEN")]
    [InlineData("AttachDBFilename", "C:\\fixture-secret.mdf", "CP6_COMPAT_SQL_ATTACH_FORBIDDEN")]
    public void Sql_external_file_and_mars_cannot_enter_an_owned_test_lane(string key, string value, string expected)
    {
        var builder = new SqlConnectionStringBuilder(Sql()) { [key] = value };
        AssertSafe(expected, () => Parse(DatabaseProvider.SqlServer, connection: builder.ConnectionString));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void PostgreSql_connection_pins_public_and_suppresses_server_error_details(string host)
    {
        var builder = new NpgsqlConnectionStringBuilder(Pg()) { Host = host, IncludeErrorDetail = true };
        var selected = Parse(DatabaseProvider.PostgreSql, connection: builder.ConnectionString);
        var result = new NpgsqlConnectionStringBuilder(selected.ConnectionString);
        Assert.Equal("public", result.SearchPath);
        Assert.False(result.IncludeErrorDetail);
    }

    [Theory]
    [InlineData("database.example")]
    [InlineData("localhost,127.0.0.1")]
    [InlineData("/var/run/postgresql")]
    [InlineData("@abstractsocket")]
    public void PostgreSql_requires_one_literal_loopback_host(string host)
    {
        var builder = new NpgsqlConnectionStringBuilder(Pg()) { Host = host };
        AssertSafe("CP6_COMPAT_LOOPBACK_REQUIRED", () => Parse(DatabaseProvider.PostgreSql, connection: builder.ConnectionString));
    }

    [Fact]
    public void PostgreSql_multiplexing_is_not_a_shared_transaction_connection()
    {
        var builder = new NpgsqlConnectionStringBuilder(Pg()) { Multiplexing = true };
        AssertSafe("CP6_COMPAT_PG_MULTIPLEXING_FORBIDDEN", () => Parse(DatabaseProvider.PostgreSql, connection: builder.ConnectionString));
    }

    [Fact]
    public void PostgreSql_custom_search_path_is_rejected_without_echoing_it()
    {
        var builder = new NpgsqlConnectionStringBuilder(Pg()) { SearchPath = Secret };
        AssertSafe("CP6_COMPAT_PG_SEARCH_PATH_INVALID", () => Parse(DatabaseProvider.PostgreSql, connection: builder.ConnectionString));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Malformed_or_missing_connections_only_emit_fixed_safe_codes(DatabaseProvider provider)
    {
        AssertSafe("CP6_COMPAT_CONNECTION_INVALID", () => Parse(provider, connection: $"Password={Secret};{Secret}=invalid"));
        AssertSafe("CP6_COMPAT_CONNECTION_REQUIRED", () => Parse(provider, connection: ""));
    }

    private static OwnedTestDatabase Parse(DatabaseProvider provider, string? connection = null, string name = Name, string owner = Owner)
        => OwnedTestDatabase.Parse(new(provider), connection ?? (provider == DatabaseProvider.SqlServer ? Sql() : Pg()),
            "WP6", name, owner, [DatabaseFixtureRole.CoreWms], "CP6Compat.Tests");

    private static string Sql(string name = Name) => $"Server=localhost;Database={name};User Id=fixture;Password={Secret};TrustServerCertificate=true;MultipleActiveResultSets=false";
    private static string Pg(string name = Name) => $"Host=localhost;Database={name};Username=fixture;Password={Secret}";

    private static void AssertSafe(string expected, Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Equal(expected, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(Secret, exception.ToString());
    }
}
