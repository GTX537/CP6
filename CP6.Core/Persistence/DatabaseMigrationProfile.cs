namespace CP6.Core.Persistence;

public enum DatabaseContextKind
{
    Core,
    Space,
    IdentityPriority,
    ErpIntegration
}

/// <summary>One provider's migration identity for each production context.</summary>
public sealed class DatabaseMigrationProfile
{
    private DatabaseMigrationProfile(string? migrationsAssembly, string? historyTable, string? historySchema, DatabaseContextKind migrationOwner)
    {
        MigrationsAssembly = migrationsAssembly;
        HistoryTable = historyTable;
        HistorySchema = historySchema;
        MigrationOwner = migrationOwner;
    }

    public string? MigrationsAssembly { get; }
    public string? HistoryTable { get; }
    public string? HistorySchema { get; }
    public DatabaseContextKind MigrationOwner { get; }

    public static DatabaseMigrationProfile For(DatabaseOptions database, DatabaseContextKind context)
    {
        ArgumentNullException.ThrowIfNull(database);
        var postgreSqlHistory = context switch
        {
            DatabaseContextKind.Core => "__EFMigrationsHistory",
            DatabaseContextKind.Space => "__EFMigrationsHistory_Space",
            DatabaseContextKind.IdentityPriority => "__EFMigrationsHistory_IdentityPriority",
            DatabaseContextKind.ErpIntegration => "__EFMigrationsHistory_ErpIntegration",
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };

        return database.Provider == DatabaseProvider.PostgreSql
            ? new("CP6.Persistence.PostgreSql", postgreSqlHistory, "public", context)
            : new(null, context == DatabaseContextKind.Space ? postgreSqlHistory : null, null,
                context == DatabaseContextKind.Space ? DatabaseContextKind.Space : DatabaseContextKind.Core);
    }
}
