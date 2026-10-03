using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CP6.Tests.Persistence;

public sealed class DatabaseSearchPathTests
{
    private const string Input = "Host=localhost;Database=fixture;Username=fixture;Password=do-not-print";

    [Fact]
    public void Factory_pins_missing_search_path_to_public_before_opening()
    {
        using var connection = new DatabaseConnectionFactory(new(DatabaseProvider.PostgreSql)).Create(Input);
        Assert.Equal("public", new NpgsqlConnectionStringBuilder(connection.ConnectionString).SearchPath);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Ef_string_path_uses_the_same_fixed_public_schema()
    {
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(DatabaseProvider.PostgreSql), Input).Options;
        Assert.Equal("public", new NpgsqlConnectionStringBuilder(RelationalOptionsExtension.Extract(options).ConnectionString!).SearchPath);
    }

    [Fact]
    public void Caller_owned_closed_connection_is_pinned_before_open_without_replacing_it()
    {
        using var connection = new NpgsqlConnection(Input);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(DatabaseProvider.PostgreSql), connection).Options;
        Assert.Same(connection, RelationalOptionsExtension.Extract(options).Connection);
        Assert.False(RelationalOptionsExtension.Extract(options).IsConnectionOwned);
        Assert.Equal("public", new NpgsqlConnectionStringBuilder(connection.ConnectionString).SearchPath);
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("$user, public")]
    [InlineData("public,pg_catalog")]
    [InlineData("Public")]
    public void Unsupported_search_path_fails_before_provider_or_connection_use(string searchPath)
    {
        var input = new NpgsqlConnectionStringBuilder(Input) { SearchPath = searchPath }.ConnectionString;
        var builder = new DbContextOptionsBuilder();
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseContextOptions.Configure(builder, new(DatabaseProvider.PostgreSql), input));
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(builder.Options.Extensions, extension => extension.Info.IsDatabaseProvider);
        using var existing = new NpgsqlConnection(input);
        Assert.Throws<InvalidOperationException>(() => DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(DatabaseProvider.PostgreSql), existing));
        Assert.Equal(System.Data.ConnectionState.Closed, existing.State);
    }
}
