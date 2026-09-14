using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceFreezerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static async Task<(ActualSourceFreezer Freezer, ActualSourceFreezeRequest Request, ActualSourceInspectionOptions Options)> Prepare(SqlFixture db)
    {
        var server = await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var options = new ActualSourceInspectionOptions(db.ConnectionString, db.Name, db.Options.ExpectedDatabaseGuid, server, LockTimeoutMilliseconds: 150);
        var report = await new ActualSourceInspector(options).InspectAsync();
        return (new(options), new(Guid.NewGuid(), report.Identity, report.ScopeSha256, Hash, Hash, Hash, Hash), options);
    }

    [Fact]
    public async Task Exact_request_freezes_all_tables_and_replays_without_duplicate_audit()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (freezer, request, _) = await Prepare(db);
        var result = await freezer.FreezeAsync(request, request.Digest());
        Assert.Equal("Frozen", result.Fence.State);
        Assert.Equal(1, result.Fence.Generation);
        Assert.Equal(request.RunId, result.Fence.RunId);
        Assert.Equal(request.Digest(), result.RequestSha256);
        Assert.Equal(request.ExpectedScopeSha256, result.BeforeScopeSha256);
        Assert.NotEqual(result.BeforeScopeSha256, result.FrozenScopeSha256);
        Assert.False(result.CompleteWriteFenceVerified);
        Assert.False(result.TargetClosedIndependentlyVerified);
        Assert.False(result.RecoveryIntegrationVerified);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await freezer.FreezeAsync(request, request.Digest())));
        var status = await freezer.StatusAsync(request.Digest());
        Assert.Equal(result.FrozenScopeSha256, status.FrozenScopeSha256);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM crm_source_control.Audit;"));
        foreach (var table in SqlFixture.Tables)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"DELETE dbo.[{table}] WHERE 1=0;"));
            Assert.Equal(51041, error.Number);
        }
        await db.ExecuteAsync("UPDATE dbo.ErpSentinel SET Value=11 WHERE Id=1;");
        Assert.Equal(11, await db.ScalarAsync<int>("SELECT Value FROM dbo.ErpSentinel;"));
        await Error(() => db.Fence.ReopenAsync(request.RunId, 1), "C04A_DATABASE_IDENTITY");
    }

    [Fact]
    public async Task Changed_request_or_identity_cannot_adopt_or_overwrite_a_freeze()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (freezer, request, _) = await Prepare(db);
        await Error(() => freezer.FreezeAsync(request, new string('b', 64)), "C04A_REQUEST_MISMATCH");
        var wrongIdentity = request with { SourceIdentity = request.SourceIdentity with { FamilyGuid = Guid.NewGuid() } };
        await Error(() => freezer.FreezeAsync(wrongIdentity, wrongIdentity.Digest()), "C04A_DATABASE_IDENTITY");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        await freezer.FreezeAsync(request, request.Digest());
        var changed = request with { WriterControlEvidenceSha256 = new string('b', 64) };
        await Error(() => freezer.FreezeAsync(changed, changed.Digest()), "C04A_REQUEST_MISMATCH");
        await Error(() => freezer.StatusAsync(changed.Digest()), "C04A_REQUEST_MISMATCH");
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM crm_source_control.Audit;"));
    }

    [Theory]
    [InlineData("GRANT REFERENCES ON dbo.Crm_Account TO FenceWriter;", "C04A_SCOPE_CHANGED")]
    [InlineData("ALTER TABLE dbo.Crm_Account ADD ScopeProbe int NULL;", "C04A_SCOPE_CHANGED")]
    [InlineData(SqlFixture.InsertAccount, "C04A_SCOPE_CHANGED")]
    public async Task Stale_review_cannot_install_any_control_objects(string change, string code)
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (freezer, request, _) = await Prepare(db);
        await db.ExecuteAsync(change);
        await Error(() => freezer.FreezeAsync(request, request.Digest()), code);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'CP6.C04A.ActualSourceFreeze.v1';"));
    }

    [Fact]
    public async Task Nonempty_review_and_lock_timeout_cannot_freeze()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        await db.ExecuteAsync(SqlFixture.InsertAccount);
        var (freezer, request, _) = await Prepare(db);
        await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_SOURCE_NONEMPTY");
        await db.ExecuteAsync("DELETE dbo.Crm_Account;");
        (freezer, request, _) = await Prepare(db);
        await using (var connection = await db.OpenAsync())
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Crm_Account WITH (TABLOCKX,HOLDLOCK);", connection, transaction);
            await command.ExecuteScalarAsync();
            await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_LOCK_TIMEOUT");
            await transaction.RollbackAsync();
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Error(() => freezer.FreezeAsync(request, request.Digest(), cancellation.Token), "C04A_CANCELLED");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    [Fact]
    public async Task Actual_binding_on_rehearsal_named_catalog_cannot_be_reopened_by_legacy_entry()
    {
        await using var db = await SqlFixture.CreateAsync();
        var (freezer, request, _) = await Prepare(db);
        await freezer.FreezeAsync(request, request.Digest());
        await Error(() => db.Fence.ReopenAsync(request.RunId, 1), "C04A_ACTUAL_CONTROL_REQUIRED");
        await Error(() => db.Fence.SealForwardOnlyAsync(request.RunId, 1), "C04A_ACTUAL_CONTROL_REQUIRED");
        Assert.Equal("Frozen", (await freezer.StatusAsync(request.Digest())).Fence.State);
    }

    [Fact]
    public async Task Missing_binding_or_changed_frozen_scope_is_not_a_valid_receipt()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (freezer, request, _) = await Prepare(db);
        await Error(() => freezer.StatusAsync(request.Digest()), "C04A_ACTUAL_BINDING_MISSING");
        await freezer.FreezeAsync(request, request.Digest());
        await db.ExecuteAsync("GRANT REFERENCES ON dbo.Crm_Account TO FenceWriter;");
        await Error(() => freezer.StatusAsync(request.Digest()), "C04A_SCOPE_CHANGED");
        await db.ExecuteAsync("REVOKE REFERENCES ON dbo.Crm_Account FROM FenceWriter;");
        await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'CP6.C04A.ActualSourceFreeze.v1';");
        await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_ACTUAL_BINDING_MISSING");
    }

    [Fact]
    public async Task Failure_while_persisting_receipt_rolls_back_every_fence_change()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        await db.ExecuteAsync("CREATE TRIGGER RejectActualReceipt ON DATABASE FOR CREATE_TRIGGER AS IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]','nvarchar(128)')=N'C04A_Fence_Crm_StageHistory' EXEC sys.sp_addextendedproperty @name=N'CP6.C04A.ActualSourceFreeze.v1',@value=N'fixture collision';");
        var (freezer, request, _) = await Prepare(db);
        await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_SQL_FAILURE");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'C04A[_]Fence[_]%';"));
        await db.ExecuteAsync(SqlFixture.InsertAccount);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Crm_Account;"));
    }

    [Fact]
    public async Task Ddl_hook_cannot_bless_unreviewed_permissions_into_the_frozen_scope()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        await db.ExecuteAsync("CREATE TRIGGER InjectUnreviewedGrant ON DATABASE FOR CREATE_SCHEMA AS GRANT REFERENCES ON dbo.Crm_Account TO FenceWriter;");
        var (freezer, request, _) = await Prepare(db);
        await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_INSPECTION_DRIFT");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.database_permissions WHERE permission_name=N'REFERENCES' AND grantee_principal_id=USER_ID(N'FenceWriter');"));
    }

    [Fact]
    public async Task Changed_nested_receipt_format_is_rejected_without_changing_frozen_state()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (freezer, request, _) = await Prepare(db);
        await freezer.FreezeAsync(request, request.Digest());
        await db.ExecuteAsync("DECLARE @value nvarchar(3750)=(SELECT CONVERT(nvarchar(3750),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6.C04A.ActualSourceFreeze.v1'); SET @value=REPLACE(@value,N'CP6.C04A.ActualSourceFreezeRequest.v1',N'CP6.C04A.ActualSourceFreezeRequest.invalid'); EXEC sys.sp_updateextendedproperty @name=N'CP6.C04A.ActualSourceFreeze.v1',@value=@value;");
        await Error(() => freezer.StatusAsync(request.Digest()), "C04A_ACTUAL_BINDING_DRIFT");
        await Error(() => freezer.FreezeAsync(request, request.Digest()), "C04A_ACTUAL_BINDING_DRIFT");
        Assert.Equal("Frozen", await db.ScalarAsync<string>("SELECT State FROM crm_source_control.Control;"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM crm_source_control.Audit;"));
        await db.ExecuteAsync("DECLARE @value nvarchar(3750)=(SELECT CONVERT(nvarchar(3750),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6.C04A.ActualSourceFreeze.v1'); SET @value=REPLACE(@value,N'CP6.C04A.ActualSourceFreezeRequest.invalid',N'CP6.C04A.ActualSourceFreezeRequest.v1'); EXEC sys.sp_updateextendedproperty @name=N'CP6.C04A.ActualSourceFreeze.v1',@value=@value;");
        Assert.Equal(1, (await freezer.FreezeAsync(request, request.Digest())).Fence.Generation);
        Assert.Equal(1, (await freezer.StatusAsync(request.Digest())).Fence.Generation);
    }

    [Fact]
    public async Task Cli_binds_exact_file_bytes_and_rejects_actual_reopen()
    {
        await using var db = await SqlFixture.CreateAsync(inspection: true);
        var (_, request, options) = await Prepare(db);
        var directory = Path.Combine(Path.GetTempPath(), "C04A_Actual_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "request.json");
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            async Task<(int Code, string Output, string Error)> Invoke(string operation, string digest)
            {
                var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add(Environment.GetEnvironmentVariable("C04A_PUBLISHED_CLI_PATH") ?? typeof(SourceFence).Assembly.Location);
                start.ArgumentList.Add(operation);
                foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("C04A_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
                start.Environment["C04A_SQL_CONNECTION"] = db.ConnectionString;
                start.Environment["C04A_EXPECTED_DATABASE"] = db.Name;
                start.Environment["C04A_EXPECTED_DATABASE_GUID"] = db.Options.ExpectedDatabaseGuid.ToString();
                start.Environment["C04A_EXPECTED_SERVER_NAME"] = options.ExpectedServerName;
                start.Environment["C04A_REQUEST_PATH"] = path;
                start.Environment["C04A_REQUEST_FILE_SHA256"] = digest;
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return (process.ExitCode, await output, await error);
            }
            var refused = await Invoke("freeze-actual", new string('b', 64));
            Assert.Equal(2, refused.Code);
            Assert.Contains("C04A_REQUEST_FILE_MISMATCH", refused.Error);
            var frozen = await Invoke("freeze-actual", hash);
            Assert.True(frozen.Code == 0, frozen.Error);
            Assert.Contains("\"state\":\"Frozen\"", frozen.Output);
            Assert.DoesNotContain(db.ConnectionString, frozen.Output + frozen.Error);
            var reopen = await Invoke("reopen-actual", hash);
            Assert.Equal(2, reopen.Code);
            Assert.Contains("C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED", reopen.Error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task Error(Func<Task> action, string code) => Assert.Equal(code, (await Assert.ThrowsAsync<SourceFenceException>(action)).Code);
}
