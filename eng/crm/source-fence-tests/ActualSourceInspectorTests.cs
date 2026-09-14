using System.Text.Json;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceInspectorTests
{
    [Fact]
    public async Task Actual_inspection_is_read_only_and_binds_schema_permissions_and_programs()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var options = new ActualSourceInspectionOptions(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server);
        var inspector = new ActualSourceInspector(options);
        var before = await inspector.InspectAsync();
        Assert.Equal(20, before.SourceRows.Count);
        Assert.All(before.SourceRows.Values, value => Assert.Equal(0, value));
        Assert.True(before.ObserverIsSysadmin);
        Assert.False(before.CompleteWriteFenceVerified);
        Assert.False(before.MutationAuthorized);
        Assert.Contains(before.DatabasePrincipals, user => user.Name == "FenceWriter");
        Assert.Equal(before.ScopeSha256, (await inspector.InspectAsync()).ScopeSha256);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        await Error(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_DATABASE_IDENTITY");

        await db.ExecuteAsync("GRANT REFERENCES ON dbo.Crm_Account TO FenceWriter;");
        var permissions = await inspector.InspectAsync();
        Assert.NotEqual(before.SecuritySha256, permissions.SecuritySha256);
        Assert.Equal(before.SchemaSha256, permissions.SchemaSha256);
        await Error(() => new ActualSourceInspector(options with { ExpectedScopeSha256 = before.ScopeSha256 }).InspectAsync(), "C04A_SCOPE_CHANGED");
        await db.ExecuteAsync("ALTER TABLE dbo.Crm_Account ADD InspectionProbe int NULL;");
        var schema = await inspector.InspectAsync();
        Assert.NotEqual(permissions.SchemaSha256, schema.SchemaSha256);
        await db.ExecuteAsync("CREATE PROCEDURE dbo.InspectionProbe AS SELECT N'private-module-marker';");
        var program = await inspector.InspectAsync();
        Assert.NotEqual(schema.ProgramsSha256, program.ProgramsSha256);
        Assert.DoesNotContain("private-module-marker", JsonSerializer.Serialize(program));
        await db.ExecuteAsync(SqlFixture.InsertAccount);
        var populated = await inspector.InspectAsync();
        Assert.Equal(1, populated.SourceRows["Crm_Account"]);
        Assert.Contains("C04A_SOURCE_NONEMPTY", populated.Blockers);
        Assert.NotEqual(program.ScopeSha256, populated.ScopeSha256);
    }

    [Fact]
    public async Task Lesser_login_cannot_misreport_filtered_catalogs_as_empty()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var login = "C04A_Inspection_Test_" + Guid.NewGuid().ToString("N");
        var password = "aA1!" + Guid.NewGuid().ToString("N");
        await db.ExecuteAsync($"CREATE LOGIN [{login}] WITH PASSWORD=N'{password}';");
        try
        {
            await db.ExecuteAsync($"CREATE USER [{login}] FOR LOGIN [{login}];");
            var builder = new SqlConnectionStringBuilder(db.ConnectionString) { IntegratedSecurity = false, UserID = login, Password = password, Pooling = false };
            await Error(() => new ActualSourceInspector(new(builder.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server)).InspectAsync(), "C04A_INSPECTION_VISIBILITY");
        }
        finally
        {
            await db.ExecuteAsync($"IF USER_ID(N'{login}') IS NOT NULL DROP USER [{login}]; DROP LOGIN [{login}];");
        }
    }

    [Fact]
    public async Task Lock_timeout_and_cancellation_leave_actual_source_untouched()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var inspector = new ActualSourceInspector(new(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server, LockTimeoutMilliseconds: 150));
        await using (var connection = await db.OpenAsync())
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Crm_Account WITH (TABLOCKX,HOLDLOCK);", connection, transaction);
            await command.ExecuteScalarAsync();
            await Error(() => inspector.InspectAsync(), "C04A_LOCK_TIMEOUT");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Error(() => inspector.InspectAsync(cancellation.Token), "C04A_CANCELLED");
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Crm_Account;"));
    }

    [Fact]
    public async Task Encrypted_modules_are_reported_as_unclassified_not_verified()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        await db.ExecuteAsync("CREATE PROCEDURE dbo.OpaqueProbe WITH ENCRYPTION AS SELECT 1;");
        var report = await new ActualSourceInspector(new(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server)).InspectAsync();
        Assert.Equal(1, report.OpaqueModuleCount);
        Assert.Contains("C04A_OPAQUE_MODULES", report.Blockers);
        Assert.False(report.WriterInventoryComplete);
    }

    [Fact]
    public async Task Release_CLI_inspects_actual_name_and_rejects_changed_scope_without_secret_output()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        async Task<(int Code, string Output, string Error)> Invoke(string? hash)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(SourceFence).Assembly.Location);
            start.ArgumentList.Add("inspect-actual");
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("C04A_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
            start.Environment["C04A_SQL_CONNECTION"] = db.ConnectionString;
            start.Environment["C04A_EXPECTED_DATABASE"] = db.Name;
            start.Environment["C04A_EXPECTED_DATABASE_GUID"] = db.Options.ExpectedDatabaseGuid.ToString();
            start.Environment["C04A_EXPECTED_SERVER_NAME"] = server;
            if (hash != null) start.Environment["C04A_EXPECTED_SCOPE_SHA256"] = hash;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await output, await error);
        }
        var inspected = await Invoke(null);
        Assert.Equal(0, inspected.Code);
        Assert.Empty(inspected.Error);
        using var json = JsonDocument.Parse(inspected.Output);
        Assert.False(json.RootElement.GetProperty("mutationAuthorized").GetBoolean());
        var scope = json.RootElement.GetProperty("scopeSha256").GetString()!;
        Assert.Equal(0, (await Invoke(scope)).Code);
        var refused = await Invoke(new string('0', 64));
        Assert.Equal(2, refused.Code);
        Assert.Empty(refused.Output);
        Assert.Equal("{\"error\":\"C04A_SCOPE_CHANGED\"}", refused.Error.Trim());
        Assert.DoesNotContain(db.ConnectionString, inspected.Output + refused.Error);
    }

    [Fact]
    public async Task Changed_view_binding_changes_scope_while_observation_times_do_not()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var inspector = new ActualSourceInspector(new(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server));
        await db.ExecuteAsync("CREATE VIEW dbo.SourceView AS SELECT Id FROM dbo.Crm_Account;");
        var before = await inspector.InspectAsync();
        var repeated = await inspector.InspectAsync();
        Assert.Equal(before.ScopeSha256, repeated.ScopeSha256);
        Assert.True(before.ObservationStartedUtc <= before.ObservedAtUtc);
        await db.ExecuteAsync("ALTER VIEW dbo.SourceView AS SELECT Id FROM dbo.Crm_Contact;");
        var after = await inspector.InspectAsync();
        Assert.NotEqual(before.ProgramsSha256, after.ProgramsSha256);
        Assert.NotEqual(before.ScopeSha256, after.ScopeSha256);
        Assert.Equal(before.SchemaSha256, after.SchemaSha256);
    }

    [Fact]
    public async Task Actual_inspection_rejects_wrong_instance_and_database_identity()
    {
        await using var db = await SqlFixture.CreateAsync();
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var options = new ActualSourceInspectionOptions(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server);
        await Error(() => new ActualSourceInspector(options with { ExpectedServerName = server + "_wrong" }).InspectAsync(), "C04A_DATABASE_IDENTITY");
        await Error(() => new ActualSourceInspector(options with { ExpectedDatabaseGuid = Guid.NewGuid() }).InspectAsync(), "C04A_DATABASE_IDENTITY");
        await db.ExecuteAsync("CREATE TABLE dbo.Crm_Unexpected (Id int NOT NULL);");
        await Error(() => new ActualSourceInspector(options).InspectAsync(), "C04A_SOURCE_INVENTORY");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    [Theory]
    [InlineData("Server=remote.invalid;Database=CP6DB", "CP6DB", "C04A_LOCAL_SOURCE_REQUIRED")]
    [InlineData("Server=localhost;Database=master", "master", "C04A_DATABASE_IDENTITY")]
    [InlineData("Server=localhost;Database=wrong", "CP6DB", "C04A_DATABASE_IDENTITY")]
    [InlineData("Server=localhost;Database=CP6DB;Application Intent=ReadOnly", "CP6DB", "C04A_DATABASE_IDENTITY")]
    public async Task Unsafe_connection_options_fail_before_connection(string connection, string name, string code)
    {
        await Error(() => new ActualSourceInspector(new(connection, name, Guid.NewGuid(), "local-instance")).InspectAsync(), code);
    }

    private static async Task Error(Func<Task> action, string code) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<SourceFenceException>(action)).Code);
}
