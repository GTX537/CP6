using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePostgreSqlPrerequisiteTests(ITestOutputHelper output)
{
    [PostgreSqlPrerequisiteFact]
    public async Task Space_first_then_Core_reuses_prerequisites()
    {
        var database = await OpenFreshAsync();
        var execution = new Execution(Guid.NewGuid(), Guid.NewGuid());
        await using var space = database.CreateContext(execution, new Clock());
        await database.MigrateAsync(space);
        await AssertSpaceOnlyAsync(database);
        await AssertFunctionBehaviorAsync(database);
        var original = await ReadPrerequisitesAsync(database);

        var model = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
        space.Models.Add(model);
        await space.SaveChangesAsync();
        var token = model.RowVersion.ToArray();
        Assert.Equal(8, token.Length);

        await using var core = CreateCore(database, execution.TenantId);
        await MigrateProfileAsync(core, "Core-after-Space");
        Assert.Equal(original, await ReadPrerequisitesAsync(database));
        await AssertRepeatAsync(database, core, space, original);
        await using var verify = database.CreateContext(execution, new Clock());
        var persisted = await verify.Models.AsNoTracking().SingleAsync(row => row.Id == model.Id);
        Assert.Equal(model.SiteId, persisted.SiteId);
        Assert.Equal(token, persisted.RowVersion);

        // Each existing function is deliberately altered only inside a rolled-back native transaction.
        // The production installer must reject it, and must not replace it to make the test pass.
        foreach (var signature in new[] { "public.cp6_text_range_v1(text,text)", "public.cp6_cp936_length_v1(text)" })
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using (var alter = new NpgsqlCommand($"ALTER FUNCTION {signature} VOLATILE", connection, transaction))
                    await alter.ExecuteNonQueryAsync();
                await using var check = new NpgsqlCommand(PostgreSqlMigrationPrerequisitesV1.InstallSql, connection, transaction);
                var error = await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteNonQueryAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.Contains("function definition mismatch", error.MessageText, StringComparison.Ordinal);
                output.WriteLine($"DefinitionGuard={signature}; SQLSTATE={error.SqlState}; ExpectedRejection=true.");
            }
            finally
            {
                await transaction.RollbackAsync();
            }
            Assert.Equal(original, await ReadPrerequisitesAsync(database));
        }
    }

    [PostgreSqlPrerequisiteFact]
    public async Task Core_first_then_Space_reuses_prerequisites()
    {
        var database = await OpenFreshAsync();
        var execution = new Execution(Guid.NewGuid(), Guid.NewGuid());
        await using var core = CreateCore(database, execution.TenantId);
        await MigrateProfileAsync(core, "Core-first");
        var original = await ReadPrerequisitesAsync(database);
        await using var space = database.CreateContext(execution, new Clock());
        await MigrateProfileAsync(space, "Space-after-Core");
        Assert.Equal(original, await ReadPrerequisitesAsync(database));
        await AssertFunctionBehaviorAsync(database);
        await AssertRepeatAsync(database, core, space, original);
    }

    [PostgreSqlPrerequisiteFact]
    public async Task Generated_Space_script_runs_twice_without_Core()
    {
        var database = await OpenFreshAsync();
        // This is the actual production design-time factory, with the same provider registration as runtime Migrate.
        await using var space = new SpaceContextDesignFactory().CreateDbContext(
            ["--Database:Provider", "PostgreSql", "--ConnectionStrings:DefaultConnection", database.ConnectionString]);
        var migrator = space.Database.GetService<IMigrator>();
        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var transactionalProbe = migrator.GenerateScript(options:
            MigrationsSqlGenerationOptions.Idempotent | MigrationsSqlGenerationOptions.NoTransactions);
        output.WriteLine($"DesignTimeScriptSHA256={Hash(script)}; NegativeScriptSHA256={Hash(transactionalProbe)}.");
        Assert.Contains("DO $cp6_prerequisites_v1$", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("DO $cp6_prerequisites_v1$", StringComparison.Ordinal)
            < script.IndexOf("\"Space_AiTenantPolicy\"", StringComparison.Ordinal));

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                await using (var wrong = new NpgsqlCommand(
                    "CREATE COLLATION public.cp6_ci_as_provider_v1 (provider=icu,locale='und-u-ks-level2',deterministic=true)", connection, transaction))
                    await wrong.ExecuteNonQueryAsync();
                await using var invalid = new NpgsqlCommand(transactionalProbe, connection, transaction) { CommandTimeout = 180 };
                var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
                Assert.Equal("P0001", error.SqlState);
                Assert.Contains("collation definition mismatch", error.MessageText, StringComparison.Ordinal);
                output.WriteLine($"ScriptCollationGuardSQLSTATE={error.SqlState}; ExpectedRejection=true.");
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }
        Assert.Null(await connection.ExecuteScalarAsync<string>(
            "SELECT collname FROM pg_catalog.pg_collation c JOIN pg_catalog.pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public' AND c.collname='cp6_ci_as_provider_v1'"));
        Assert.Empty(await space.Database.GetAppliedMigrationsAsync());

        await ExecuteScriptAsync(connection, script);
        await AssertExactHistoryAsync(space, "Space-script-first");
        await AssertSpaceOnlyAsync(database);
        var original = await ReadPrerequisitesAsync(database);
        await ExecuteScriptAsync(connection, script);
        await AssertExactHistoryAsync(space, "Space-script-repeat");
        Assert.Equal(original, await ReadPrerequisitesAsync(database));
        await AssertSpaceOnlyAsync(database);
        await AssertFunctionBehaviorAsync(database);
        output.WriteLine("GeneratedSpaceScriptExecutions=2; CoreProfile=not-applied.");
    }

    private async Task<SpaceMigrationTestDatabase> OpenFreshAsync()
    {
        var database = await SpaceMigrationTestDatabase.OpenIfSelectedAsync(output, allowPostgreSql: true)
            ?? throw new InvalidOperationException("The prerequisite cases require an explicitly selected runner-owned PostgreSQL database.");
        Assert.Equal(DatabaseProvider.PostgreSql, database.Database.Provider);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Empty(await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema')"));
        output.WriteLine("Prerequisite case: verified WP5 owner; initially empty catalog; database creation/deletion remain runner-owned.");
        return database;
    }

    private CP6Context CreateCore(SpaceMigrationTestDatabase database, Guid tenantId)
    {
        var profile = DatabaseMigrationProfile.For(database.Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database.Database,
            database.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        options.AddInterceptors(new SpaceNativeFailureObserver(DatabaseProvider.PostgreSql, output.WriteLine, "prerequisite-core"));
        var context = new CP6Context(options.Options, new TenantContext { CurrentTenantId = tenantId });
        context.Database.SetCommandTimeout(180);
        return context;
    }

    private async Task MigrateProfileAsync(DbContext context, string stage)
    {
        var expected = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(expected);
        Assert.True(before.Length <= expected.Length && before.SequenceEqual(expected.Take(before.Length)));
        output.WriteLine($"Stage={stage}; BeforeApplied={string.Join(',', before)}.");
        await context.Database.MigrateAsync();
        await AssertExactHistoryAsync(context, stage);
    }

    private async Task AssertExactHistoryAsync(DbContext context, string stage)
    {
        var expected = context.Database.GetMigrations().ToArray();
        var actual = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        output.WriteLine($"Stage={stage}; ActualApplied={string.Join(',', actual)}; Pending=0.");
    }

    private async Task AssertRepeatAsync(SpaceMigrationTestDatabase database, CP6Context core, SpaceContext space, string[] original)
    {
        await MigrateProfileAsync(core, "Core-repeat");
        await MigrateProfileAsync(space, "Space-repeat");
        Assert.Equal(original, await ReadPrerequisitesAsync(database));
        output.WriteLine("PrerequisiteOidsAndDefinitions=unchanged; RepeatedProfiles=Core,Space.");
    }

    private static async Task AssertSpaceOnlyAsync(SpaceMigrationTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var tables = (await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname='public' ORDER BY tablename")).ToArray();
        Assert.Contains("Space_Model", tables);
        Assert.Contains(SpaceContext.MigrationsHistoryTable, tables);
        Assert.DoesNotContain("__EFMigrationsHistory", tables);
        Assert.DoesNotContain("Space_Site", tables);
        Assert.All(tables, table => Assert.True(table == SpaceContext.MigrationsHistoryTable || table.StartsWith("Space_", StringComparison.Ordinal)));
    }

    private async Task<string[]> ReadPrerequisitesAsync(SpaceMigrationTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var definitions = (await connection.QueryAsync<string>("""
            SELECT 'collation|' || to_jsonb(c)::text AS definition
            FROM pg_catalog.pg_collation c JOIN pg_catalog.pg_namespace n ON n.oid=c.collnamespace
            WHERE n.nspname='public' AND c.collname='cp6_ci_as_provider_v1'
            UNION ALL
            SELECT 'function|' || to_jsonb(p)::text FROM pg_catalog.pg_proc p
            WHERE p.oid IN (to_regprocedure('public.cp6_text_range_v1(text,text)'),to_regprocedure('public.cp6_cp936_length_v1(text)'))
            ORDER BY definition
            """)).ToArray();
        Assert.Equal(3, definitions.Length);
        output.WriteLine($"PrerequisiteCatalogSHA256={Hash(string.Join('\n', definitions))}; Objects=3.");
        return definitions;
    }

    private static async Task AssertFunctionBehaviorAsync(SpaceMigrationTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT public.cp6_cp936_length_v1('€'), public.cp6_cp936_length_v1('中文'),
              public.cp6_text_range_v1('ABCDEF','hex'), 'a' COLLATE public.cp6_ci_as_provider_v1 = 'A'
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(4, reader.GetInt32(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
    }

    private static async Task ExecuteScriptAsync(NpgsqlConnection connection, string script)
    {
        await using var command = new NpgsqlCommand(script, connection) { CommandTimeout = 180 };
        await command.ExecuteNonQueryAsync();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Execution(Guid TenantId, Guid ActorId) : ISpaceExecutionContext;
    private sealed class Clock : ISpaceClock { public DateTime UtcNow => DateTime.UtcNow; }

    private sealed class PostgreSqlPrerequisiteFactAttribute : FactAttribute
    {
        public PostgreSqlPrerequisiteFactAttribute()
        {
            if (SpaceMigrationTestDatabase.IsSelected)
                _ = SpaceMigrationTestDatabase.ValidateSelectedInputs();
            else
                Skip = "These cases require dedicated owned PostgreSQL migration inputs.";
        }
    }
}
