using CP6.Core.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Crm.SourceFence.Tests;

// Real SQL Server, linked foundation migration. Fresh fixture data is synthetic;
// these tests do not substitute for restored-source or production acceptance.
internal sealed class SqlFixture : IAsyncDisposable
{
    internal static readonly string[] Tables =
    [
        "Crm_Account", "Crm_Activity", "Crm_Collaborator", "Crm_Contact", "Crm_ErpLink",
        "Crm_IntakeConfig", "Crm_IntakeMember", "Crm_Lead", "Crm_MediaAsset", "Crm_MergeRecord",
        "Crm_Opportunity", "Crm_PageRevision", "Crm_PageTranslation", "Crm_PublicForm",
        "Crm_PublicRoute", "Crm_PublicSubmission", "Crm_Site", "Crm_SitePage", "Crm_SourceTouch", "Crm_StageHistory"
    ];
    private readonly string admin;
    private readonly string prefix;
    internal string Name { get; }
    internal string ConnectionString { get; private set; } = "";
    internal SourceFenceOptions Options { get; private set; } = null!;
    internal SourceFence Fence => new(Options);
    private bool created;

    private SqlFixture(string admin, bool inspection)
    {
        this.admin = admin;
        prefix = inspection ? "CP6_C04A_Inspection_Test_" : "CP6_C04A_Rehearsal_Test_";
        Name = prefix + Guid.NewGuid().ToString("N");
    }

    internal static async Task<SqlFixture> CreateAsync(bool inspection = false)
    {
        var admin = Environment.GetEnvironmentVariable("C04A_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(admin))
            throw new InvalidOperationException("C04A_TEST_SQL_CONNECTION is required; SQL tests must not skip.");
        var builder = new SqlConnectionStringBuilder(admin);
        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test administrator connection must explicitly select master.");
        var fixture = new SqlFixture(admin, inspection);
        await using var connection = new SqlConnection(admin);
        await connection.OpenAsync();
        using var identity = new SqlCommand("SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));", connection);
        if (!string.Equals((string?)await identity.ExecuteScalarAsync(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test SQL Server must be on the local machine.");
        using var create = new SqlCommand($"CREATE DATABASE [{fixture.Name}];", connection);
        await create.ExecuteNonQueryAsync();
        fixture.created = true;
        builder.InitialCatalog = fixture.Name;
        // Deliberately aborted EXECUTE AS probes must never return impersonated
        // sessions to a pool before their REVERT statement could execute.
        builder.Pooling = false;
        fixture.ConnectionString = builder.ConnectionString;
        try
        {
            using var context = new DbContext(new DbContextOptionsBuilder().UseSqlServer(fixture.ConnectionString).Options);
            var generator = context.GetService<IMigrationsSqlGenerator>();
            foreach (var command in generator.Generate(new CrmFoundation().UpOperations))
                await fixture.ExecuteAsync(command.CommandText);
            await fixture.ExecuteAsync("CREATE TABLE dbo.ErpSentinel (Id int NOT NULL PRIMARY KEY, Value int NOT NULL); INSERT dbo.ErpSentinel VALUES (1, 10); CREATE USER FenceWriter WITHOUT LOGIN; GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON SCHEMA::dbo TO FenceWriter;");
            var guid = await fixture.ScalarAsync<Guid>("SELECT service_broker_guid FROM sys.databases WHERE database_id=DB_ID();");
            fixture.Options = new(fixture.ConnectionString, fixture.Name, guid, 1500, 20);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await OpenAsync();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        return (T)(await command.ExecuteScalarAsync())!;
    }

    internal const string InsertAccount = "INSERT dbo.Crm_Account (Id,Name,NormalizedName,CreateDate,TenantId,IsDeleted) VALUES (NEWID(),N'fixture',N'FIXTURE',SYSUTCDATETIME(),NEWID(),0);";

    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        if (!Name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing cleanup of an unowned database.");
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(admin);
        await connection.OpenAsync();
        using var command = new SqlCommand($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];", connection);
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}
