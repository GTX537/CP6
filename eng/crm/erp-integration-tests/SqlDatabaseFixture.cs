using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlDatabaseCollection : ICollectionFixture<SqlDatabaseFixture>
{
    public const string Name = "C03 real SQL";
}

/// <summary>Explicit ERP configuration uses the owned relational fixture; otherwise preserves the legacy SQL database lifecycle.</summary>
public sealed class SqlDatabaseFixture : IAsyncLifetime, IErpScenarioDatabase
{
    private readonly ErpRelationalFixture? relational = Environment.GetEnvironmentVariables().Keys.Cast<string>()
        .Any(name => name.StartsWith("CP6_ERP_TEST_", StringComparison.OrdinalIgnoreCase)) ? new() : null;
    private readonly string database = "CP6C03Test_" + Guid.NewGuid().ToString("N");
    private string? masterConnection;
    private string? connection;
    private bool created;

    public DatabaseOptions Database => relational?.Database ?? new(DatabaseProvider.SqlServer);
    public bool IsPostgreSql => Database.Provider == DatabaseProvider.PostgreSql;
    public string SetupSummary => relational?.SetupSummary ?? "Provider=SqlServer; legacy isolated SQL database lifecycle.";

    public void ConfigureBusinessOptions(DbContextOptionsBuilder options)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Core);
        DatabaseContextOptions.Configure(options, Database,
            relational?.ConnectionString ?? connection ?? throw new InvalidOperationException("C03_DATABASE_NOT_INITIALIZED"),
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
    }

    public async Task InitializeAsync()
    {
        if (relational is not null)
        {
            await relational.InitializeAsync();
            return;
        }
        var supplied = Environment.GetEnvironmentVariable("CP6_C03_TEST_SQL");
        if (string.IsNullOrWhiteSpace(supplied))
            throw new InvalidOperationException("C03_REAL_SQL_REQUIRED: Set CP6_C03_TEST_SQL to an explicit loopback SQL Server master connection. This suite never skips unavailable SQL.");
        SqlConnectionStringBuilder sql;
        try { sql = new(supplied); }
        catch (ArgumentException) { throw new InvalidOperationException("C03_SQL_CONNECTION_INVALID"); }
        RequireLoopback(sql.DataSource);
        if (!string.IsNullOrEmpty(sql.AttachDBFilename)) throw new InvalidOperationException("C03_SQL_ATTACH_FORBIDDEN");
        sql.InitialCatalog = "master";
        sql.Pooling = false;
        sql.MultipleActiveResultSets = false;
        sql.ConnectTimeout = 15;
        masterConnection = sql.ConnectionString;
        try
        {
            await using (var master = new SqlConnection(masterConnection))
            {
                await master.OpenAsync();
                await using var create = master.CreateCommand();
                RequireOwnedName();
                // CREATE fails on an existing name; no existing database can become owned by this fixture.
                create.CommandText = $"CREATE DATABASE [{database}]";
                create.CommandTimeout = 90;
                await create.ExecuteNonQueryAsync();
                created = true;
            }
            sql.InitialCatalog = database;
            connection = sql.ConnectionString;
            await using var db = CreateBusinessContext(Guid.NewGuid());
            await db.Database.MigrateAsync();
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            await using var queue = CreateDbContext();
            // This also proves queue tables are delivered by real CP6 migrations.
            Assert.Equal(0, await queue.Inbox.CountAsync());
        }
        catch (SqlException ex)
        {
            await DisposeAsync();
            // SQL errors can echo connection/server details. Report only the SQL number.
            throw new InvalidOperationException($"C03_REAL_SQL_INITIALIZATION_FAILED_SQL_{ex.Number}");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public CP6Context CreateBusinessContext(Guid tenant) => relational?.CreateBusinessContext(tenant) ?? new(
        new DbContextOptionsBuilder<CP6Context>().UseSqlServer(
            connection ?? throw new InvalidOperationException("C03_SQL_NOT_INITIALIZED"), o => o.CommandTimeout(180)).Options,
        new TenantContext { CurrentTenantId = tenant });

    public ErpIntegrationContext CreateDbContext() => relational?.CreateDbContext() ?? new(
        new DbContextOptionsBuilder<ErpIntegrationContext>().UseSqlServer(
            connection ?? throw new InvalidOperationException("C03_SQL_NOT_INITIALIZED"), o => o.CommandTimeout(60)).Options);

    public async Task DisposeAsync()
    {
        if (relational is not null)
        {
            await relational.DisposeAsync();
            return;
        }
        if (!created) return;
        RequireOwnedName();
        var sql = new SqlConnectionStringBuilder(masterConnection!);
        RequireLoopback(sql.DataSource);
        if (sql.InitialCatalog != "master") throw new InvalidOperationException("C03_CLEANUP_REQUIRES_MASTER");
        await using var master = new SqlConnection(sql.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandTimeout = 90;
        drop.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
        await drop.ExecuteNonQueryAsync();
        await using var verify = master.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database";
        verify.Parameters.AddWithValue("@database", database);
        Assert.Equal(0, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        created = false;
    }

    private void RequireOwnedName()
    {
        if (!Regex.IsMatch(database, "^CP6C03Test_[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("C03_SQL_OWNERSHIP_INVALID");
    }

    private static void RequireLoopback(string dataSource)
    {
        var source = dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? dataSource[4..] : dataSource;
        var host = source.Split('\\', ',')[0];
        if (!new[] { "localhost", "127.0.0.1", "[::1]", "::1" }.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("C03_REQUIRES_EXPLICIT_LOOPBACK_SQL");
    }
}
