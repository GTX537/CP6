using System.Data;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Persistence;

public sealed class DatabaseUtcClockTests
{
    [Fact]
    public async Task Database_clock_requires_context()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => DatabaseUtcClock.ReadUtcNowAsync(null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public async Task Database_clock_rejects_application_clock_fallback_for_in_memory_provider()
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase(nameof(Database_clock_rejects_application_clock_fallback_for_in_memory_provider)).Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseUtcClock.ReadUtcNowAsync(context));

        Assert.Contains("SQL Server or PostgreSQL", exception.Message);
    }

    [Fact]
    public async Task Database_clock_rejects_unsupported_relational_provider()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseUtcClock.ReadUtcNowAsync(context));

        Assert.Contains("SQL Server or PostgreSQL", exception.Message);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Database_clock_honors_cancellation_before_opening_connection(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? "Server=database.cp6.test;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False"
            : "Host=database.cp6.test;Database=fixture;Username=fixture;Search Path=public";
        using var context = new DbContext(DatabaseContextOptions.Configure(new DbContextOptionsBuilder(), new(provider), connectionString).Options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DatabaseUtcClock.ReadUtcNowAsync(context, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
