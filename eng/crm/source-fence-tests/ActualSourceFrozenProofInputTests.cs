using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceFrozenProofInputTests
{
    private const string Secret = "invalid_connection;Password=source_proof_secret_9138;";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static ActualSourceFreezeRequest Request() => new(Guid.NewGuid(),
        new("not_contacted", "not_contacted", "not_contacted", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 9, 14, 0, 0, 0), "private_login", "0x1234"),
        new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64));

    [Theory]
    [InlineData("null-request", "C04A_REQUEST_MISMATCH")]
    [InlineData("changed-request", "C04A_REQUEST_MISMATCH")]
    [InlineData("bad-request-hash", "C04A_REQUEST_MISMATCH")]
    [InlineData("wrong-anchor", "C04A_SOURCE_TARGET_SET_MISMATCH")]
    [InlineData("uppercase-anchor", "C04A_SOURCE_TARGET_SET_MISMATCH")]
    [InlineData("empty-challenge", "C04A_SOURCE_PROOF_CHALLENGE_INVALID")]
    [InlineData("invalid-identity", "C04A_SOURCE_FROZEN_PROOF_INVALID")]
    public async Task Invalid_request_anchor_or_challenge_is_rejected_before_SQL_option_parsing(string scenario, string code)
    {
        var request = Request();
        var expected = request.Digest();
        var anchor = request.TargetClosedEvidenceSha256;
        var challenge = Guid.NewGuid();
        switch (scenario)
        {
            case "null-request": request = null!; break;
            case "changed-request": request = request with { RunId = Guid.NewGuid() }; break;
            case "bad-request-hash": expected = "bad"; break;
            case "wrong-anchor": anchor = new string('f', 64); break;
            case "uppercase-anchor": anchor = anchor.ToUpperInvariant(); break;
            case "empty-challenge": challenge = Guid.Empty; break;
            case "invalid-identity":
                request = request with { SourceIdentity = request.SourceIdentity with { DatabaseGuid = Guid.Empty } };
                expected = request.Digest();
                break;
        }
        var freezer = new ActualSourceFreezer(new(Secret, "not_contacted", Guid.NewGuid(), "not_contacted"));
        var error = await Assert.ThrowsAsync<SourceFenceException>(() => freezer.VerifyFrozenForTargetAsync(request, expected, anchor, challenge));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain("private_login", error.ToString());
    }

    [Theory]
    [InlineData("C04A_SQL_CONNECTION")]
    [InlineData("C04A_REQUEST_PATH")]
    [InlineData("C04A_REQUEST_FILE_SHA256")]
    [InlineData("C04A_TARGET_SET_ANCHOR_SHA256")]
    [InlineData("C04A_PROOF_CHALLENGE")]
    public async Task Cli_requires_each_bound_input_without_old_generic_identity_options(string missing)
    {
        var environment = Inputs("not_a_bound_path", new string('a', 64), new string('b', 64));
        environment.Remove(missing);
        await AssertCliFailureAsync(environment, "C04A_INVALID_OPTIONS");
    }

    [Theory]
    [InlineData("wrong-anchor", "C04A_SOURCE_TARGET_SET_MISMATCH")]
    [InlineData("empty-challenge", "C04A_SOURCE_PROOF_CHALLENGE_INVALID")]
    [InlineData("malformed-challenge", "C04A_INVALID_OPTIONS")]
    [InlineData("changed-file", "C04A_REQUEST_FILE_MISMATCH")]
    [InlineData("unknown-request-property", "C04A_REQUEST_INVALID_JSON")]
    public async Task Cli_uses_only_bound_request_identity_and_sanitizes_refusal_before_SQL(string scenario, string code)
    {
        var request = Request();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (scenario == "unknown-request-property")
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Insert(1, "\"ConnectionString\":\"private_sentinel\","));
        var path = Path.Combine(Path.GetTempPath(), "C04A_Source_Proof_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllBytesAsync(path, scenario == "changed-file" ? [.. bytes, (byte)' '] : bytes);
            var environment = Inputs(path, Hash(bytes), request.TargetClosedEvidenceSha256);
            if (scenario == "wrong-anchor") environment["C04A_TARGET_SET_ANCHOR_SHA256"] = new string('f', 64);
            if (scenario == "empty-challenge") environment["C04A_PROOF_CHALLENGE"] = Guid.Empty.ToString("D");
            if (scenario == "malformed-challenge") environment["C04A_PROOF_CHALLENGE"] = Secret;
            // These stale generic values and the container binding file must not
            // replace the identity already bound in the original freeze request.
            environment["C04A_EXPECTED_DATABASE_GUID"] = "malformed-unused-guid";
            environment["C04A_CONTAINER_BINDING_PATH"] = "missing-unused-container.json";
            await AssertCliFailureAsync(environment, code);
        }
        finally { File.Delete(path); }
    }

    private static Dictionary<string, string> Inputs(string path, string fileHash, string anchor) => new()
    {
        ["C04A_SQL_CONNECTION"] = Secret,
        ["C04A_REQUEST_PATH"] = path,
        ["C04A_REQUEST_FILE_SHA256"] = fileHash,
        ["C04A_TARGET_SET_ANCHOR_SHA256"] = anchor,
        ["C04A_PROOF_CHALLENGE"] = Guid.NewGuid().ToString("D")
    };

    private static async Task AssertCliFailureAsync(IReadOnlyDictionary<string, string> environment, string code)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("C04A_PUBLISHED_CLI_PATH") ?? typeof(SourceFence).Assembly.Location);
        start.ArgumentList.Add("prove-target-enable-actual");
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("C04A_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        Assert.Equal(2, process.ExitCode);
        Assert.Empty(await output);
        Assert.Equal(JsonSerializer.Serialize(new { error = code }), (await error).Trim());
        Assert.DoesNotContain(Secret, (await output) + (await error));
        Assert.DoesNotContain("private_login", (await output) + (await error));
        Assert.DoesNotContain("private_sentinel", (await output) + (await error));
    }
}
