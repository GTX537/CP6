using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal sealed class RuntimeFixture
{
    public RuntimeFixture(DatabaseProvider provider)
    {
        Database = new(provider);
        Owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER") ?? "";
        ProbeAssert.Require(Regex.IsMatch(Owner, "\\A[0-9a-f]{32}\\z"), "A task ownership receipt is required.");
        ConnectionString = Environment.GetEnvironmentVariable(IsPostgreSql ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER") ?? "";
        ProbeAssert.Require(!string.IsNullOrWhiteSpace(ConnectionString), "A test connection is required.");
        if (IsPostgreSql)
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            ProbeAssert.Require(builder.Host is "localhost" or "127.0.0.1" or "::1", "Literal loopback required.");
            ProbeAssert.Require(Regex.IsMatch(builder.Database ?? "", "\\ACP6Compat_WP3_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP3 database name required.");
            builder.IncludeErrorDetail = false;
            builder.ApplicationName = "CP6Compat.WP3.RuntimeProbe";
            ConnectionString = builder.ConnectionString;
        }
        else
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            var source = builder.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
                ? builder.DataSource[4..] : builder.DataSource;
            var host = source.Split('\\', ',')[0];
            ProbeAssert.Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)",
                "A literal loopback SQL Server host is required.");
            ProbeAssert.Require(Regex.IsMatch(builder.InitialCatalog, "\\ACP6Compat_WP3_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP3 database name required.");
        }
    }
    public DatabaseOptions Database { get; }
    public bool IsPostgreSql => Database.Provider == DatabaseProvider.PostgreSql;
    public string ConnectionString { get; }
    private string Owner { get; }
    public DbConnection Connection() => new DatabaseConnectionFactory(Database).Create(ConnectionString);
    public CP6Context Core(Guid? tenant = null)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), Database, ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        return new(options, new TenantContext { CurrentTenantId = tenant ?? TenantContext.DefaultTenant });
    }
    public async Task<string> VerifyOwnerAsync()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        var actual = await connection.QuerySingleOrDefaultAsync<string>(IsPostgreSql
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP3')");
        ProbeAssert.Require(actual == (IsPostgreSql ? "DB-COMPAT-01-WP3:" + Owner : Owner), "Actual database ownership must match before writes.");
        return connection.ServerVersion;
    }
    public async Task<string> InitializeCoreAsync()
    {
        await using var core = Core();
        var available = core.Database.GetMigrations().ToArray();
        var before = (await core.Database.GetAppliedMigrationsAsync()).ToArray();
        ProbeAssert.Require(before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length)), "Unknown or mixed migration history rejected.");
        await core.Database.MigrateAsync();
        return await VerifyCoreHistoryAsync();
    }
    public async Task<string> VerifyCoreHistoryAsync()
    {
        await using var core = Core();
        var available = core.Database.GetMigrations().ToArray();
        var applied = (await core.Database.GetAppliedMigrationsAsync()).ToArray();
        ProbeAssert.Require(available.Length == (IsPostgreSql ? 1 : 140) && available.SequenceEqual(applied) && !(await core.Database.GetPendingMigrationsAsync()).Any(), "Exact supported Core history required.");
        return $"Exact Core history={applied.Length}, pending=0; no full catalog or business acceptance implied.";
    }
    public string SequenceTable => IsPostgreSql ? "public.\"T_DocSequence\"" : "dbo.T_DocSequence";
    public string Column(string column) => IsPostgreSql ? '"' + column + '"' : '[' + column + ']';
    public async Task ClearOrderAsync()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        await connection.ExecuteAsync($"DELETE FROM {SequenceTable} WHERE {Column("FuncCode")}=@code", new { code = "ORD" });
        ProbeAssert.Require(await ReadOrderAsync() is null, "Order fixture cleanup must leave the counter absent.");
    }
    public async Task<int?> ReadOrderAsync()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<int?>($"SELECT {Column("LastSeq")} FROM {SequenceTable} WHERE {Column("FuncCode")}=@code", new { code = "ORD" });
    }
}
internal static class ProbeAssert
{
    public static void Require(bool condition, string message) { if (!condition) throw new ProbeAssertionException(message); }
}
internal sealed class ProbeAssertionException(string message) : Exception(message);
