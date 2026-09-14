using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class LocalSqlContainerTests
{
    private static LocalSqlContainerBinding Example() => new("dockerDesktopLinuxEngine", "example-engine", new string('a', 64),
        "sha256:" + new string('b', 64), "/example-sql", "example-sql", "2026-09-14T12:00:00.000000000Z", 1433, 1433,
        new string('c', 64), new string('d', 64), new string('e', 64), "default");

    [Fact]
    public void Native_identity_and_freeze_request_keep_their_original_canonical_bytes()
    {
        var identity = new ActualSourceIdentity("server", "machine", "database", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 9, 14, 12, 0, 0), "owner", "ABCD");
        var original = new { identity.ServerName, identity.MachineName, identity.DatabaseName, identity.BrokerGuid,
            identity.DatabaseGuid, identity.FamilyGuid, identity.RecoveryForkGuid, identity.CreatedAtServerLocal, identity.OriginalLogin, identity.OriginalLoginSid };
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(identity));
        var request = new ActualSourceFreezeRequest(Guid.NewGuid(), identity, new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64));
        var oldRequest = JsonSerializer.Serialize(new { request.RunId, SourceIdentity = original, request.ExpectedScopeSha256,
            request.ApprovalEvidenceSha256, request.WriterControlEvidenceSha256, request.RecoveryPlanEvidenceSha256, request.TargetClosedEvidenceSha256, request.Format });
        Assert.Equal(oldRequest, JsonSerializer.Serialize(request));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldRequest))).ToLowerInvariant(), request.Digest());
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-hash")]
    [InlineData("duplicate")]
    [InlineData("format")]
    [InlineData("missing-format")]
    [InlineData("unknown")]
    [InlineData("remote-engine")]
    [InlineData("short-id")]
    public async Task Container_binding_files_are_byte_bound_and_reject_ambiguous_or_remote_inputs(string scenario)
    {
        var binding = Example();
        var value = JsonSerializer.Serialize(binding);
        value = scenario switch
        {
            "duplicate" => value.Insert(1, "\"ContainerId\":\"" + binding.ContainerId + "\","),
            "format" => value.Replace(binding.Format, "unknown", StringComparison.Ordinal),
            "missing-format" => value.Replace(",\"Format\":\"" + binding.Format + "\"", "", StringComparison.Ordinal),
            "unknown" => value.Insert(1, "\"Unreviewed\":true,"),
            "remote-engine" => JsonSerializer.Serialize(binding with { EnginePipeName = "tcp://remote:2375" }),
            "short-id" => JsonSerializer.Serialize(binding with { ContainerId = binding.ContainerId[..12] }),
            _ => value
        };
        var path = Path.Combine(Path.GetTempPath(), "C04A_ContainerBinding_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, value);
            var hash = scenario == "wrong-hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
            if (scenario == "valid") Assert.Equal(binding, await LocalSqlContainerInspector.ReadBindingAsync(path, hash));
            else await Assert.ThrowsAsync<SourceFenceException>(() => LocalSqlContainerInspector.ReadBindingAsync(path, hash));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("localhost,1433")]
    [InlineData("203.0.113.9,1433")]
    [InlineData("127.0.0.1,1434")]
    [InlineData("lpc:localhost\\KOUSQLSERVER")]
    public async Task Different_SQL_endpoints_are_refused_before_connecting(string endpoint)
    {
        var connection = new SqlConnectionStringBuilder { DataSource = endpoint, InitialCatalog = "ExampleOnly", UserID = "fixture", Password = "not-a-real-password" };
        var inspector = new ActualSourceInspector(new(connection.ConnectionString, "ExampleOnly", Guid.NewGuid(), "fixture", LocalContainer: Example()));
        var error = await Assert.ThrowsAsync<SourceFenceException>(() => inspector.InspectAsync());
        Assert.Equal("C04A_CONTAINER_SQL_ENDPOINT", error.Code);
    }

    [Fact]
    public async Task Nested_container_format_cannot_be_ignored_inside_a_freeze_request()
    {
        var identity = new ActualSourceIdentity("server", "machine", "database", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow, "owner", "ABCD", Example());
        var request = new ActualSourceFreezeRequest(Guid.NewGuid(), identity, new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64));
        var path = Path.Combine(Path.GetTempPath(), "C04A_ContainerFreeze_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request).Replace(Example().Format, "unknown", StringComparison.Ordinal));
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
            var error = await Assert.ThrowsAsync<SourceFenceException>(() => ActualSourceFreezer.ReadRequestAsync(path, hash));
            Assert.Equal("C04A_CONTAINER_FILE_INVALID", error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Live_local_container_source_uses_foundation_schema_and_rejects_binding_drift_without_touching_actual_database()
    {
        var path = Environment.GetEnvironmentVariable("C04A_TEST_CONTAINER_BINDING_PATH") ?? throw new InvalidOperationException("Explicit local container binding is required.");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        var binding = await LocalSqlContainerInspector.ReadBindingAsync(path, hash);
        await LocalSqlContainerInspector.VerifyAsync(binding);
        await using var database = await SqlFixture.CreateAsync(inspection: true, localContainer: binding);
        var server = await database.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var options = new ActualSourceInspectionOptions(database.ConnectionString, database.Name, database.Options.ExpectedDatabaseGuid, server, LocalContainer: binding);
        var report = await new ActualSourceInspector(options).InspectAsync();
        Assert.Equal(binding, report.Identity.LocalContainer);
        Assert.Equal(binding.HostName, report.Identity.MachineName);
        Assert.All(report.SourceRows.Values, count => Assert.Equal(0, count));
        Assert.False(report.CompleteWriteFenceVerified);
        var nativeError = await Assert.ThrowsAsync<SourceFenceException>(() => new ActualSourceInspector(options with { LocalContainer = null }).InspectAsync());
        Assert.Equal("C04A_LOCAL_SOURCE_REQUIRED", nativeError.Code);
        var stale = options with { LocalContainer = binding with { StartedAtUtc = "2020-01-01T00:00:00Z" } };
        var changed = await Assert.ThrowsAsync<SourceFenceException>(() => new ActualSourceInspector(stale).InspectAsync());
        Assert.Equal("C04A_CONTAINER_BINDING_CHANGED", changed.Code);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        var request = new ActualSourceFreezeRequest(Guid.NewGuid(), report.Identity, report.ScopeSha256, new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64));
        var freezer = new ActualSourceFreezer(options);
        var frozen = await freezer.FreezeAsync(request, request.Digest());
        Assert.Equal("Frozen", frozen.Fence.State);
        Assert.Equal(frozen.RequestSha256, (await freezer.StatusAsync(request.Digest())).RequestSha256);
        var write = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(SqlFixture.InsertAccount));
        Assert.Equal(51041, write.Number);
        await Assert.ThrowsAsync<SourceFenceException>(() => new ActualSourceFreezer(stale).StatusAsync(request.Digest()));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM crm_source_control.Audit;"));
        await database.ExecuteAsync("UPDATE dbo.ErpSentinel SET Value=11 WHERE Id=1;");
        Assert.Equal(11, await database.ScalarAsync<int>("SELECT Value FROM dbo.ErpSentinel WHERE Id=1;"));
    }
}
