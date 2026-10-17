using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Persistence.PostgreSql.Migrations.Core;
using CP6.Persistence.PostgreSql.Migrations.IdentityPriority;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Frozen baseline objects for the bounded profile, not an EF migration. No Core or Identity migration
/// history is stamped: all remaining baseline objects still require their own explicit adoption.
/// </summary>
public static class ServiceRevocationBaselineV1
{
    public static string GenerateSql() => GenerateSql(false);

    internal static string GenerateSql(bool serviceTokens)
    {
        var core = new PostgreSqlCoreBaselineV1();
        var queue = new PostgreSqlIdentityPriorityBaselineV1();
        bool CoreTable(string? schema, string table) => schema == "crm_identity" && table is "ServiceToken" or "Snapshot"
            || schema is null && (table == "CrmIdentityTenantGenerations" || serviceTokens && table == "Sys_Tenants");
        static bool QueueTable(string? schema, string table) => schema == "crm_identity_priority" && table == "Cp6_OutboxMessage";
        static IEnumerable<MigrationOperation> Select(Migration migration, Func<string?, string, bool> include) =>
            migration.UpOperations.Where(op => op switch
            {
                CreateTableOperation table => include(table.Schema, table.Name),
                CreateIndexOperation index => include(index.Schema, index.Table),
                _ => false
            });
        var selected = Select(core, CoreTable).Concat(Select(queue, QueueTable)).ToArray();
        if (selected.OfType<CreateTableOperation>().Count() != (serviceTokens ? 5 : 4))
            throw new InvalidOperationException("Service revocation baseline changed.");
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        builder.EnsureSchema("crm_identity"); builder.EnsureSchema("crm_identity_priority");
        builder.Operations.AddRange(selected);
        PostgreSqlTokenV1.Install(builder, "cp6_storage_core", [new("crm_identity", "ServiceToken", "RowVersion"), new("crm_identity", "Snapshot", "RowVersion")]);
        PostgreSqlTokenV1.Install(builder, "cp6_storage_identity", [new("crm_identity_priority", "Cp6_OutboxMessage", "RowVersion")]);
        builder.Sql(IdentityGenerationPostgreSqlV1.Sql);
        using var context = new CP6Context(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),
            new(DatabaseProvider.PostgreSql), "Host=127.0.0.1;Database=script_only;Username=script_only;Search Path=public").Options);
        return string.Join("\n", context.GetService<IMigrationsSqlGenerator>().Generate(builder.Operations, core.TargetModel,
            MigrationsSqlGenerationOptions.NoTransactions).Select(x => x.CommandText));
    }
}
