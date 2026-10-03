using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace CP6.Core.Persistence;

// Npgsql 8's documented generator constructor requires this provider-owned options service.
#pragma warning disable EF1001
public sealed class PostgreSqlPrerequisiteMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions options) : NpgsqlMigrationsSqlGenerator(dependencies, options)
#pragma warning restore EF1001
{
    public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations,
        IModel? model = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        if (!operations.Any(RequiresPrerequisites))
            return base.Generate(operations, model, options);

        var prepared = new List<MigrationOperation>
        {
            new SqlOperation { Sql = PostgreSqlMigrationPrerequisitesV1.InstallSql }
        };
        foreach (var operation in operations)
        {
            // Only the exact original Core v1 creates are substituted. Other operations and annotations are untouched.
            if (IsFrozenCollationCreate(operation) || IsFrozenFunctionCreate(operation)) continue;
            prepared.Add(operation);
        }
        return base.Generate(prepared, model, options);
    }

    private static bool RequiresPrerequisites(MigrationOperation operation) => operation switch
    {
        CreateTableOperation table => table.Columns.Any(column => column.Collation == PostgreSqlMigrationPrerequisitesV1.Collation)
            || table.CheckConstraints.Any(check => UsesTextFunctions(check.Sql)),
        ColumnOperation column => column.Collation == PostgreSqlMigrationPrerequisitesV1.Collation,
        AddCheckConstraintOperation check => UsesTextFunctions(check.Sql),
        _ => IsFrozenCollationCreate(operation) || IsFrozenFunctionCreate(operation)
    };

    private static bool UsesTextFunctions(string sql) => sql.Contains("public.cp6_cp936_length_v1(", StringComparison.Ordinal)
        || sql.Contains("public.cp6_text_range_v1(", StringComparison.Ordinal);

    private static bool IsFrozenCollationCreate(MigrationOperation operation) => operation is AlterDatabaseOperation database
        && database.Collation is null && database.OldDatabase.Collation is null
        && !database.OldDatabase.GetAnnotations().Any() && database.GetAnnotations().Count() == 1
        && Equals(database[PostgreSqlMigrationPrerequisitesV1.CollationAnnotation], PostgreSqlMigrationPrerequisitesV1.CollationDefinition);

    private static bool IsFrozenFunctionCreate(MigrationOperation operation) => operation is SqlOperation sql
        && !sql.SuppressTransaction && !sql.GetAnnotations().Any()
        && sql.Sql == PostgreSqlMigrationPrerequisitesV1.FunctionsSql;
}
