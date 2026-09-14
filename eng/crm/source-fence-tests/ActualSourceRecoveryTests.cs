using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Crm.Cutover;
using CP6.Crm.SourceFence;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceRecoveryTests
{
    private static ActualSourceRecoveryRequest Request() => new(Guid.NewGuid(), new string('a', 64), new string('b', 64), new string('c', 64));

    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-file-hash")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("format")]
    [InlineData("empty-run")]
    public async Task Recovery_request_binds_exact_bytes_and_rejects_ambiguous_documents(string scenario)
    {
        var request = Request();
        var json = JsonSerializer.Serialize(request);
        json = scenario switch
        {
            "duplicate" => json.Insert(1, "\"RunId\":\"" + request.RunId + "\","),
            "unknown" => json.Insert(1, "\"Unexpected\":true,"),
            "format" => json.Replace(request.Format, "wrong", StringComparison.Ordinal),
            "empty-run" => JsonSerializer.Serialize(request with { RunId = Guid.Empty }),
            _ => json
        };
        var path = Path.Combine(Path.GetTempPath(), "C04A_Recovery_Request_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await File.WriteAllBytesAsync(path, bytes);
            var hash = scenario == "wrong-file-hash" ? new string('d', 64) : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (scenario == "valid") Assert.Equal(request, await ActualSourceFreezer.ReadRecoveryRequestAsync(path, hash));
            else await Assert.ThrowsAsync<SourceFenceException>(() => ActualSourceFreezer.ReadRecoveryRequestAsync(path, hash));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"Permit\":null}")]
    [InlineData("null")]
    [InlineData("{\"unknown\":true}")]
    public void Invalid_terminal_receipts_have_only_protocol_errors(string value)
        => Assert.Throws<TargetRollbackException>(() => TargetRollbackProtocol.ParseReceipt(value));

    [Fact]
    public async Task Missing_live_terminal_target_proof_leaves_actual_foundation_source_frozen()
    {
        await using var database = await SqlFixture.CreateAsync(inspection: true);
        var server = await database.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));");
        var options = new ActualSourceInspectionOptions(database.ConnectionString, database.Name, database.Options.ExpectedDatabaseGuid, server);
        var observation = await new ActualSourceInspector(options).InspectAsync();
        var freezer = new ActualSourceFreezer(options);
        var freeze = new ActualSourceFreezeRequest(Guid.NewGuid(), observation.Identity, observation.ScopeSha256,
            new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64));
        await freezer.FreezeAsync(freeze, freeze.Digest());
        var request = Request() with { SourceFreezeRequestSha256 = freeze.Digest() };
        var error = await Assert.ThrowsAsync<SourceFenceException>(() => freezer.ReopenAsync(request, request.Digest(), database.ConnectionString));
        Assert.Equal("C04A_TARGET_ROLLBACK_MISSING", error.Code);
        Assert.Equal("Frozen", (await freezer.StatusAsync(freeze.Digest())).Fence.State);
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM crm_source_control.Audit;"));
    }
}
