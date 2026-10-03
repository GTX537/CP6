using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Space.IntegrationTests;

/// <summary>SQL generation only; these facts do not open a database connection.</summary>
public sealed class SpaceMigrationSqlGenerationTests
{
    [Theory]
    [InlineData(DatabaseContextKind.Core)]
    [InlineData(DatabaseContextKind.Space)]
    [InlineData(DatabaseContextKind.IdentityPriority)]
    [InlineData(DatabaseContextKind.ErpIntegration)]
    public void All_PostgreSql_profiles_register_the_common_migration_generator(DatabaseContextKind kind)
    {
        using var context = Create(DatabaseProvider.PostgreSql, kind);
        Assert.IsType<PostgreSqlPrerequisiteMigrationsSqlGenerator>(context.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void SqlServer_keeps_its_native_migration_generator()
    {
        using var context = Create(DatabaseProvider.SqlServer, DatabaseContextKind.Space);
        Assert.Equal("SqlServerMigrationsSqlGenerator", context.GetService<IMigrationsSqlGenerator>().GetType().Name);
    }

    [Fact]
    public void Unrelated_operations_keep_the_original_PostgreSql_SQL()
    {
        using var context = Create(DatabaseProvider.PostgreSql, DatabaseContextKind.Space);
        using var original = new DbContext(new DbContextOptionsBuilder<DbContext>()
            .UseNpgsql("Host=localhost;Database=sql_generation_only;Username=unused").Options);
        MigrationOperation[] operations =
        [
            new EnsureSchemaOperation { Name = "unrelated_schema" },
            new SqlOperation { Sql = "SELECT 'unmodified raw statement';" }
        ];
        Assert.Equal(Generate(original, operations), Generate(context, operations));
        Assert.DoesNotContain("cp6_prerequisites_v1", Generate(context, operations), StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_Core_creates_become_one_guarded_install_without_mutating_source_operations()
    {
        using var context = Create(DatabaseProvider.PostgreSql, DatabaseContextKind.Core);
        var collation = new AlterDatabaseOperation();
        collation.AddAnnotation(PostgreSqlMigrationPrerequisitesV1.CollationAnnotation,
            PostgreSqlMigrationPrerequisitesV1.CollationDefinition);
        var functions = new SqlOperation { Sql = PostgreSqlMigrationPrerequisitesV1.FunctionsSql };
        MigrationOperation[] operations =
        [
            new EnsureSchemaOperation { Name = "crm_identity" }, collation, functions,
            new SqlOperation { Sql = "SELECT 'retained Core operation';" }
        ];
        var sql = Generate(context, operations);
        Assert.Single(Regex.Matches(sql, Regex.Escape("DO $cp6_prerequisites_v1$")));
        Assert.Single(Regex.Matches(sql, Regex.Escape("CREATE FUNCTION \"public\".cp6_text_range_v1")));
        Assert.Single(Regex.Matches(sql, Regex.Escape("CREATE FUNCTION \"public\".cp6_cp936_length_v1")));
        Assert.Contains("SELECT 'retained Core operation';", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR REPLACE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Same(collation, operations[1]);
        Assert.Same(functions, operations[2]);
        Assert.Equal(PostgreSqlMigrationPrerequisitesV1.CollationDefinition,
            collation[PostgreSqlMigrationPrerequisitesV1.CollationAnnotation]);
        Assert.Equal(PostgreSqlManagedTextV1.CreateFunctions("public", "cp6_ci_as_provider_v1"), functions.Sql);
    }

    [Fact]
    public void Space_table_prerequisites_precede_the_first_dependent_column()
    {
        using var context = Create(DatabaseProvider.PostgreSql, DatabaseContextKind.Space);
        var table = new CreateTableOperation { Name = "Space_PrerequisiteSqlProbe" };
        table.Columns.Add(new AddColumnOperation
        {
            Name = "Value", Table = table.Name, ClrType = typeof(string), ColumnType = "bpchar",
            Collation = "cp6_ci_as_provider_v1", IsNullable = false
        });
        var sql = Generate(context, [table]);
        Assert.Contains("CREATE TABLE \"Space_PrerequisiteSqlProbe\"", sql, StringComparison.Ordinal);
        Assert.Contains("DO $cp6_prerequisites_v1$", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("DO $cp6_prerequisites_v1$", StringComparison.Ordinal)
            < sql.IndexOf("CREATE TABLE \"Space_PrerequisiteSqlProbe\"", StringComparison.Ordinal));
        Assert.Single(table.Columns);
    }

    private static DbContext Create(DatabaseProvider provider, DatabaseContextKind kind)
    {
        var database = new DatabaseOptions(provider);
        var profile = DatabaseMigrationProfile.For(database, kind);
        var connection = provider == DatabaseProvider.PostgreSql
            ? "Host=localhost;Database=sql_generation_only;Username=unused"
            : "Server=localhost;Database=sql_generation_only;Integrated Security=true;TrustServerCertificate=true";
        return new DbContext(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<DbContext>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }

    private static string Generate(DbContext context, IReadOnlyList<MigrationOperation> operations) =>
        string.Concat(context.GetService<IMigrationsSqlGenerator>().Generate(operations).Select(command => command.CommandText));
}
