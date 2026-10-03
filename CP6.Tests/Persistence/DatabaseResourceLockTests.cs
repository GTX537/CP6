using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence;

public sealed class DatabaseResourceLockTests
{
    // These values were calculated independently of the production helper:
    // SHA256(UTF8(resource)), first eight digest bytes interpreted as signed big endian.
    public static TheoryData<string, long> PostgreSqlKeyVectors => new()
    {
        { "cp6:space:floor-edit:11111111111141118111111111111111:22222222222242228222222222222222:33333333333343338333333333333333", 5450251417918794356L },
        { "CP6:Seed:SpaceAuditPermission:v1", -8128330429859714458L },
        { "CP6:SpaceIntegrationEvent:OccurredAtUtc:v1", 1758760524055182398L },
        { "c03:message:0123456789abcdef", 7211430008690815696L },
        { "resource", 6767031285991106541L },
        { "Resource", -1478724186056112996L },
        { "resource ", -2642325522617836797L },
        { "资源:楼层", -770984511167209867L },
    };

    [Theory]
    [MemberData(nameof(PostgreSqlKeyVectors))]
    public void PostgreSql_key_matches_independent_stable_signed_big_endian_vectors(string resource, long expected)
    {
        Assert.Equal(expected, DatabaseResourceLocks.PostgreSqlKey(resource));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void PostgreSql_key_rejects_missing_resource(string? resource)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => DatabaseResourceLocks.PostgreSqlKey(resource!));

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void PostgreSql_key_rejects_resource_longer_than_sql_application_lock_limit()
    {
        var exception = Assert.Throws<ArgumentException>(() => DatabaseResourceLocks.PostgreSqlKey(new string('a', 256)));

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public async Task Transaction_lock_requires_a_context()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(null!, "resource", 0));

        Assert.Equal("context", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Transaction_lock_rejects_missing_resource_before_accessing_database(string? resource)
    {
        using var context = RelationalContext(DatabaseProvider.SqlServer);

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, resource!, 0));

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public async Task Transaction_lock_rejects_oversized_resource()
    {
        using var context = RelationalContext(DatabaseProvider.SqlServer);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, new string('a', 256), 0));

        Assert.Equal("resource", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Transaction_lock_rejects_negative_timeout_instead_of_waiting_forever(int timeoutMs)
    {
        using var context = RelationalContext(DatabaseProvider.SqlServer);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, "resource", timeoutMs));

        Assert.Equal("timeoutMs", exception.ParamName);
    }

    [Fact]
    public async Task Transaction_lock_rejects_in_memory_provider()
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase(nameof(Transaction_lock_rejects_in_memory_provider)).Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, "resource", 0));

        Assert.Contains("SQL Server or PostgreSQL", exception.Message);
    }

    [Fact]
    public async Task Transaction_lock_rejects_other_relational_providers()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, "resource", 0));

        Assert.Contains("SQL Server or PostgreSQL", exception.Message);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Transaction_lock_requires_caller_transaction_without_opening_connection(DatabaseProvider provider)
    {
        using var context = RelationalContext(provider);
        var connection = context.Database.GetDbConnection();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseResourceLocks.TryAcquireTransactionAsync(context, "resource", 0));

        Assert.Contains("caller transaction", exception.Message);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Session_lock_requires_caller_connection()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DatabaseResourceLocks.TryAcquireSessionAsync(null!, DatabaseProvider.SqlServer, "resource", 0));

        Assert.Equal("connection", exception.ParamName);
    }

    [Fact]
    public async Task Session_lock_rejects_unknown_provider()
    {
        using var connection = Connection(DatabaseProvider.SqlServer);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            DatabaseResourceLocks.TryAcquireSessionAsync(connection, (DatabaseProvider)99, "resource", 0));

        Assert.Equal("provider", exception.ParamName);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer)]
    public async Task Session_lock_rejects_provider_connection_mismatch_without_opening_it(
        DatabaseProvider selected, DatabaseProvider connectionProvider)
    {
        using var connection = Connection(connectionProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseResourceLocks.TryAcquireSessionAsync(connection, selected, "resource", 0));

        Assert.Contains("does not match", exception.Message);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Session_lock_requires_already_open_caller_connection_and_preserves_its_configuration(DatabaseProvider provider)
    {
        using var connection = Connection(provider);
        var original = connection.ConnectionString;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseResourceLocks.TryAcquireSessionAsync(connection, provider, "resource", 0));

        Assert.Contains("open", exception.Message);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(original, connection.ConnectionString);
    }

    [Fact]
    public async Task Session_lock_rejects_negative_timeout_before_connection_access()
    {
        using var connection = Connection(DatabaseProvider.SqlServer);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            DatabaseResourceLocks.TryAcquireSessionAsync(connection, DatabaseProvider.SqlServer, "resource", -1));

        Assert.Equal("timeoutMs", exception.ParamName);
    }

    private static DbContext RelationalContext(DatabaseProvider provider)
    {
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(provider), ConnectionString(provider)).Options;
        return new DbContext(options);
    }

    private static DbConnection Connection(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(ConnectionString(provider)),
        DatabaseProvider.PostgreSql => new NpgsqlConnection(ConnectionString(provider)),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static string ConnectionString(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? "Server=database.cp6.test;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False"
        : "Host=database.cp6.test;Database=fixture;Username=fixture;Search Path=public";
}
