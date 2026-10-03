using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CP6.Tests.Persistence;

public sealed class DatabaseSharedContextTests
{
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.Core, null)]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.Space, "__EFMigrationsHistory_Space")]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.IdentityPriority, null)]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.ErpIntegration, null)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.Core, "__EFMigrationsHistory")]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.Space, "__EFMigrationsHistory_Space")]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.IdentityPriority, "__EFMigrationsHistory_IdentityPriority")]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.ErpIntegration, "__EFMigrationsHistory_ErpIntegration")]
    public async Task Child_keeps_explicit_caller_provider_connection_and_target_migration_identity(
        DatabaseProvider provider, DatabaseContextKind target, string? history)
    {
        await using var caller = Caller(provider);
        var connection = caller.Database.GetDbConnection();
        await using var child = await ChildAsync(caller, target);
        var options = child.GetService<IDbContextOptions>();
        var relational = RelationalOptionsExtension.Extract(options);

        Assert.Equal(caller.Database.ProviderName, child.Database.ProviderName);
        Assert.Same(connection, child.Database.GetDbConnection());
        Assert.False(relational.IsConnectionOwned);
        Assert.Equal(history, relational.MigrationsHistoryTableName);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "public" : null, relational.MigrationsHistoryTableSchema);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "CP6.Persistence.PostgreSql" : null, relational.MigrationsAssembly);
        Assert.Null(child.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Single(options.Extensions, extension => extension.Info.IsDatabaseProvider);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Explicit_connection_overload_preserves_external_connection_and_non_ownership(DatabaseProvider provider)
    {
        await using var caller = Caller(provider);
        var connection = caller.Database.GetDbConnection();
        await using (var child = await DatabaseSharedContext.CreateAsync<CP6Context>(new(provider), connection,
            null, DatabaseContextKind.Core, options => new(options)))
        {
            Assert.Same(connection, child.Database.GetDbConnection());
            Assert.False(RelationalOptionsExtension.Extract(child.GetService<IDbContextOptions>()).IsConnectionOwned);
        }
        Assert.Equal(ConnectionState.Closed, connection.State);
        using var command = connection.CreateCommand();
        Assert.Same(connection, command.Connection);
    }

    [Fact]
    public async Task Unsupported_explicit_caller_provider_is_rejected_without_connection_inference()
    {
        await using var caller = new DbContext(new DbContextOptionsBuilder().UseInMemoryDatabase("shared-context-provider").Options);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseSharedContext.CreateAsync<CP6Context>(caller, DatabaseContextKind.Core, options => new(options)));
        Assert.Contains("provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer)]
    public async Task Explicit_provider_must_match_actual_connection_before_child_is_created(
        DatabaseProvider connectionProvider, DatabaseProvider selectedProvider)
    {
        await using var caller = Caller(connectionProvider);
        var created = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSharedContext.CreateAsync<CP6Context>(
            new(selectedProvider), caller.Database.GetDbConnection(), null, DatabaseContextKind.Core,
            options => { created = true; return new(options); }));
        Assert.False(created);
        Assert.Equal(ConnectionState.Closed, caller.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Transaction_on_another_physical_connection_is_rejected_before_child_creation()
    {
        await using var caller = Caller(DatabaseProvider.SqlServer);
        await using var other = Caller(DatabaseProvider.SqlServer);
        using var transaction = new ConnectionOnlyTransaction(other.Database.GetDbConnection());
        var created = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSharedContext.CreateAsync<CP6Context>(
            new(DatabaseProvider.SqlServer), caller.Database.GetDbConnection(), transaction, DatabaseContextKind.Core,
            options => { created = true; return new(options); }));
        Assert.False(created);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Child_factory_must_use_the_supplied_physical_connection(DatabaseProvider provider)
    {
        await using var caller = Caller(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSharedContext.CreateAsync<CP6Context>(
            caller, DatabaseContextKind.Core, _ => Caller(provider)));
        Assert.Equal(ConnectionState.Closed, caller.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task Cancellation_before_creation_does_not_invoke_factory(DatabaseProvider provider)
    {
        await using var caller = Caller(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var created = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DatabaseSharedContext.CreateAsync<CP6Context>(
            caller, DatabaseContextKind.Core,
            options => { created = true; return new(options); }, cancellation.Token));
        Assert.False(created);
        Assert.Equal(ConnectionState.Closed, caller.Database.GetDbConnection().State);
    }

    private static CP6Context Caller(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? "Server=database.cp6.test;Database=fixture;Integrated Security=True"
            : "Host=database.cp6.test;Database=fixture;Username=fixture;Search Path=public";
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), new(provider), connection).Options);
    }

    private static async Task<DbContext> ChildAsync(DbContext caller, DatabaseContextKind target) => target switch
    {
        DatabaseContextKind.Core => await DatabaseSharedContext.CreateAsync<CP6Context>(caller, target,
            options => new(options, new TenantContext())),
        DatabaseContextKind.Space => await DatabaseSharedContext.CreateAsync<SpaceContext>(caller, target,
            options => new(options, new ExecutionContext(), new SystemSpaceClock())),
        DatabaseContextKind.IdentityPriority => await DatabaseSharedContext.CreateAsync<IdentityMessagingContext>(caller,
            target, options => new(options)),
        DatabaseContextKind.ErpIntegration => await DatabaseSharedContext.CreateAsync<ErpIntegrationContext>(caller,
            target, options => new(options)),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private sealed class ExecutionContext : ISpaceExecutionContext
    {
        public Guid TenantId => TenantContext.DefaultTenant;
        public Guid ActorId => Guid.Parse("6481ce94-87da-4c23-bc9f-d19986b20f9e");
    }

    // This fixture tests the pre-enlistment connection identity check, not native transaction behavior.
    private sealed class ConnectionOnlyTransaction(DbConnection connection) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() => throw new NotSupportedException();
        public override void Rollback() => throw new NotSupportedException();
    }
}
