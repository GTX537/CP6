using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CP6.Core.Persistence;

public static class DatabaseContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, DatabaseOptions database, string connectionString,
        string? migrationsAssembly = null, string? migrationsHistoryTable = null, string? migrationsHistorySchema = null)
    {
        ValidateOptions(builder, database, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema);
        using var validatedConnection = new DatabaseConnectionFactory(database).Create(connectionString);

        return database.Provider switch
        {
            DatabaseProvider.SqlServer => builder.UseSqlServer(connectionString,
                sql => ConfigureMigrations(sql, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema)),
            DatabaseProvider.PostgreSql => builder.UseNpgsql(connectionString,
                pg => ConfigureMigrations(pg, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema)),
            _ => throw new InvalidOperationException("Database:Provider must be SqlServer or PostgreSql.")
        };
    }

    /// <summary>Shares the caller's actual connection without transferring its ownership to a context.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, DatabaseOptions database, DbConnection connection,
        string? migrationsAssembly = null, string? migrationsHistoryTable = null, string? migrationsHistorySchema = null)
    {
        ValidateOptions(builder, database, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema);
        new DatabaseConnectionFactory(database).ValidateConnection(connection);

        return database.Provider switch
        {
            DatabaseProvider.SqlServer => builder.UseSqlServer(connection, contextOwnsConnection: false,
                sql => ConfigureMigrations(sql, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema)),
            DatabaseProvider.PostgreSql => builder.UseNpgsql(connection, contextOwnsConnection: false,
                pg => ConfigureMigrations(pg, migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema)),
            _ => throw new InvalidOperationException("Database:Provider must be SqlServer or PostgreSql.")
        };
    }

    public static DbContextOptionsBuilder<TContext> Configure<TContext>(DbContextOptionsBuilder<TContext> builder, DatabaseOptions database,
        string connectionString, string? migrationsAssembly = null, string? migrationsHistoryTable = null, string? migrationsHistorySchema = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)Configure((DbContextOptionsBuilder)builder, database, connectionString,
            migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema);

    public static DbContextOptionsBuilder<TContext> Configure<TContext>(DbContextOptionsBuilder<TContext> builder, DatabaseOptions database,
        DbConnection connection, string? migrationsAssembly = null, string? migrationsHistoryTable = null, string? migrationsHistorySchema = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)Configure((DbContextOptionsBuilder)builder, database, connection,
            migrationsAssembly, migrationsHistoryTable, migrationsHistorySchema);

    private static void ValidateOptions(DbContextOptionsBuilder builder, DatabaseOptions database, string? migrationsAssembly,
        string? migrationsHistoryTable, string? migrationsHistorySchema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        if (migrationsAssembly is not null && string.IsNullOrWhiteSpace(migrationsAssembly))
            throw new ArgumentException("An explicit migrations assembly must be non-empty.", nameof(migrationsAssembly));
        if (migrationsHistoryTable is not null && string.IsNullOrWhiteSpace(migrationsHistoryTable))
            throw new ArgumentException("An explicit migrations history table must be non-empty.", nameof(migrationsHistoryTable));
        if (migrationsHistorySchema is not null && string.IsNullOrWhiteSpace(migrationsHistorySchema))
            throw new ArgumentException("An explicit migrations history schema must be non-empty.", nameof(migrationsHistorySchema));
        if (migrationsHistorySchema is not null && migrationsHistoryTable is null)
            throw new ArgumentException("A migrations history schema requires an explicit history table.", nameof(migrationsHistorySchema));

        var providerAssembly = database.Provider == DatabaseProvider.SqlServer
            ? typeof(SqlServerDbContextOptionsExtensions).Assembly
            : typeof(NpgsqlDbContextOptionsBuilderExtensions).Assembly;

        if (builder.Options.Extensions.Any(extension => extension.Info.IsDatabaseProvider && extension.GetType().Assembly != providerAssembly))
            throw new InvalidOperationException("The DbContext options already select a different Database:Provider.");
    }

    private static void ConfigureMigrations(SqlServerDbContextOptionsBuilder builder, string? migrationsAssembly,
        string? migrationsHistoryTable, string? migrationsHistorySchema)
    {
        if (migrationsAssembly is not null)
            builder.MigrationsAssembly(migrationsAssembly);
        if (migrationsHistoryTable is not null)
            builder.MigrationsHistoryTable(migrationsHistoryTable, migrationsHistorySchema);
    }

    private static void ConfigureMigrations(NpgsqlDbContextOptionsBuilder builder, string? migrationsAssembly,
        string? migrationsHistoryTable, string? migrationsHistorySchema)
    {
        if (migrationsAssembly is not null)
            builder.MigrationsAssembly(migrationsAssembly);
        if (migrationsHistoryTable is not null)
            builder.MigrationsHistoryTable(migrationsHistoryTable, migrationsHistorySchema);
    }
}
