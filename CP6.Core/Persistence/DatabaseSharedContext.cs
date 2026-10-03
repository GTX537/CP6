using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Persistence;

/// <summary>Creates a child context on its caller's connection and existing transaction.</summary>
public static class DatabaseSharedContext
{
    public static Task<TContext> CreateAsync<TContext>(DbContext caller, DatabaseContextKind target,
        Func<DbContextOptions<TContext>, TContext> create, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        // ProviderName is the caller's explicit EF configuration, never a connection-string heuristic.
        var database = new DatabaseOptions(caller.Database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => DatabaseProvider.SqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("The caller context must explicitly configure a supported database provider.")
        });
        return CreateAsync(database, caller.Database.GetDbConnection(),
            caller.Database.CurrentTransaction?.GetDbTransaction(), target, create, cancellationToken);
    }

    public static async Task<TContext> CreateAsync<TContext>(DatabaseOptions database, DbConnection connection,
        DbTransaction? transaction, DatabaseContextKind target,
        Func<DbContextOptions<TContext>, TContext> create, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(create);
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction is not null && !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("The caller transaction must belong to the same physical database connection.");

        var profile = DatabaseMigrationProfile.For(database, target);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<TContext>(), database, connection,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        var context = create(options) ?? throw new InvalidOperationException("The shared context factory returned no context.");
        try
        {
            var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
            if (!ReferenceEquals(context.Database.GetDbConnection(), connection) || relational.IsConnectionOwned ||
                relational.MigrationsAssembly != profile.MigrationsAssembly ||
                relational.MigrationsHistoryTableName != profile.HistoryTable ||
                relational.MigrationsHistoryTableSchema != profile.HistorySchema)
                throw new InvalidOperationException("The child context factory must use the supplied non-owning connection and migration profile.");
            cancellationToken.ThrowIfCancellationRequested();
            if (transaction is not null)
                await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
