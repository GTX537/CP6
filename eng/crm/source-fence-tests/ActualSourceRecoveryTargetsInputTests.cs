using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceRecoveryTargetsInputTests
{
    private const string Format = "CP6.C04A.RecoveryTargets.v1";
    private const string Secret = "Server=localhost;Database=private_target;User ID=private_user;Password=secret_sentinel_5721;";
    private sealed record Reference(Guid OrganizationId, string ReceiptSha256, string ConnectionEnvironmentVariable);
    private static Reference[] References() =>
    [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), new string('a', 64), "C04A_RECOVERY_TARGET_A_" + Guid.NewGuid().ToString("N").ToUpperInvariant()),
        new(Guid.Parse("20000000-0000-0000-0000-000000000002"), new string('b', 64), "C04A_RECOVERY_TARGET_B_" + Guid.NewGuid().ToString("N").ToUpperInvariant())
    ];
    private static string Document(Reference[] references) => JsonSerializer.Serialize(new { Format, Targets = references });
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "C04A_Recovery_Targets_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public async Task Exact_file_resolves_private_environment_connections_without_serializing_them()
    {
        var references = References();
        var path = NewPath();
        try
        {
            foreach (var reference in references) Environment.SetEnvironmentVariable(reference.ConnectionEnvironmentVariable, Secret);
            var bytes = Encoding.UTF8.GetBytes(Document(references));
            await File.WriteAllBytesAsync(path, bytes);
            var targets = await ActualSourceFreezer.ReadRecoveryTargetsAsync(path, Hash(bytes));
            Assert.Equal(references.Select(reference => reference.OrganizationId), targets.Select(target => target.OrganizationId));
            Assert.Equal(references.Select(reference => reference.ReceiptSha256), targets.Select(target => target.TargetRollbackReceiptSha256));
            Assert.All(targets, target => Assert.Equal(Secret, target.ConnectionString));
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(targets));
            Assert.DoesNotContain("ConnectionString", JsonSerializer.Serialize(targets));
            Assert.All(targets, target => Assert.DoesNotContain(Secret, target.ToString()));
        }
        finally
        {
            foreach (var reference in references) Environment.SetEnvironmentVariable(reference.ConnectionEnvironmentVariable, null);
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("unknown-root", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("duplicate-root", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("missing-format", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("wrong-format", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("wrong-case", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("unknown-target", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("duplicate-property", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("nested-connection", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("null-target", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("null-document", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("wrong-targets-shape", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("empty", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("single", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("too-many", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("unsorted", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("duplicate-organization", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("duplicate-receipt", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("duplicate-variable", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("uppercase-receipt", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("literal-connection", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("lowercase-variable", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("empty-variable-suffix", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("long-variable-suffix", "C04A_RECOVERY_TARGETS_INVALID")]
    [InlineData("missing-connection", "C04A_RECOVERY_TARGET_CONNECTION_REQUIRED")]
    [InlineData("changed-bytes", "C04A_REQUEST_FILE_MISMATCH")]
    [InlineData("too-large", "C04A_REQUEST_TOO_LARGE")]
    public async Task Bound_file_refuses_ambiguous_shapes_or_partial_target_sets_before_environment_resolution(string scenario, string code)
    {
        var references = References();
        var json = Malformed(scenario, references);
        var bytes = Encoding.UTF8.GetBytes(json);
        var path = NewPath();
        try
        {
            await File.WriteAllBytesAsync(path, scenario == "changed-bytes" ? Encoding.UTF8.GetBytes(json + " ") : bytes);
            var failure = await Assert.ThrowsAsync<SourceFenceException>(() => ActualSourceFreezer.ReadRecoveryTargetsAsync(path, Hash(bytes)));
            Assert.Equal(code, failure.Code);
            Assert.DoesNotContain(Secret, failure.ToString());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("reopen-targets-actual", "unknown-target", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("recovery-status-targets-actual", "duplicate-property", "C04A_REQUEST_INVALID_JSON")]
    [InlineData("reopen-targets-actual", "changed-bytes", "C04A_REQUEST_FILE_MISMATCH")]
    [InlineData("recovery-status-targets-actual", "missing-connection", "C04A_RECOVERY_TARGET_CONNECTION_REQUIRED")]
    public async Task Cli_rejects_bound_target_inputs_before_parsing_SQL_connections_and_sanitizes_output(string operation, string scenario, string code)
    {
        var requestPath = NewPath();
        var targetsPath = NewPath();
        try
        {
            var request = new ActualSourceRecoveryRequest(Guid.NewGuid(), new string('a', 64), new string('b', 64), new string('c', 64));
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            var targetBytes = Encoding.UTF8.GetBytes(Malformed(scenario, References()));
            await File.WriteAllBytesAsync(requestPath, requestBytes);
            await File.WriteAllBytesAsync(targetsPath, scenario == "changed-bytes" ? [.. targetBytes, (byte)' '] : targetBytes);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Environment.GetEnvironmentVariable("C04A_PUBLISHED_CLI_PATH") ?? typeof(SourceFence).Assembly.Location);
            start.ArgumentList.Add(operation);
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("C04A_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
            start.Environment["C04A_SQL_CONNECTION"] = "invalid_connection;" + Secret;
            start.Environment["C04A_EXPECTED_DATABASE"] = "not_contacted";
            start.Environment["C04A_EXPECTED_DATABASE_GUID"] = Guid.NewGuid().ToString("D");
            start.Environment["C04A_EXPECTED_SERVER_NAME"] = "not_contacted";
            start.Environment["C04A_RECOVERY_REQUEST_PATH"] = requestPath;
            start.Environment["C04A_RECOVERY_REQUEST_FILE_SHA256"] = Hash(requestBytes);
            start.Environment["C04A_RECOVERY_TARGETS_PATH"] = targetsPath;
            start.Environment["C04A_RECOVERY_TARGETS_FILE_SHA256"] = Hash(targetBytes);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await output);
            Assert.Equal(JsonSerializer.Serialize(new { error = code }), (await error).Trim());
            Assert.DoesNotContain(Secret, (await output) + (await error));
        }
        finally { File.Delete(requestPath); File.Delete(targetsPath); }
    }

    private static string Malformed(string scenario, Reference[] references)
    {
        var json = Document(references);
        return scenario switch
        {
            "unknown-root" => json.Insert(1, "\"Unknown\":true,"),
            "duplicate-root" => json.Insert(1, "\"Format\":\"" + Format + "\","),
            "missing-format" => JsonSerializer.Serialize(new { Targets = references }),
            "wrong-format" => json.Replace(Format, "unknown", StringComparison.Ordinal),
            "wrong-case" => json.Replace("OrganizationId", "organizationId", StringComparison.Ordinal),
            "unknown-target" => json.Replace("\"OrganizationId\":", "\"ConnectionString\":\"secret_sentinel_5721\",\"OrganizationId\":", StringComparison.Ordinal),
            "duplicate-property" => json.Replace("\"OrganizationId\":", "\"OrganizationId\":\"" + references[0].OrganizationId + "\",\"OrganizationId\":", StringComparison.Ordinal),
            "nested-connection" => json.Replace("\"" + references[0].ConnectionEnvironmentVariable + "\"", "{\"nested\":true}", StringComparison.Ordinal),
            "null-target" => JsonSerializer.Serialize(new { Format, Targets = new Reference?[] { references[0], null } }),
            "null-document" => "null",
            "wrong-targets-shape" => JsonSerializer.Serialize(new { Format, Targets = new { references } }),
            "empty" => Document([]),
            "single" => Document([references[0]]),
            "too-many" => Document(Enumerable.Repeat(references[0], 17).ToArray()),
            "unsorted" => Document([references[1], references[0]]),
            "duplicate-organization" => Document([references[0], references[1] with { OrganizationId = references[0].OrganizationId }]),
            "duplicate-receipt" => Document([references[0], references[1] with { ReceiptSha256 = references[0].ReceiptSha256 }]),
            "duplicate-variable" => Document([references[0], references[1] with { ConnectionEnvironmentVariable = references[0].ConnectionEnvironmentVariable }]),
            "uppercase-receipt" => Document([references[0] with { ReceiptSha256 = new string('A', 64) }, references[1]]),
            "literal-connection" => Document([references[0] with { ConnectionEnvironmentVariable = Secret }, references[1]]),
            "lowercase-variable" => Document([references[0] with { ConnectionEnvironmentVariable = references[0].ConnectionEnvironmentVariable.ToLowerInvariant() }, references[1]]),
            "empty-variable-suffix" => Document([references[0] with { ConnectionEnvironmentVariable = "C04A_RECOVERY_TARGET_" }, references[1]]),
            "long-variable-suffix" => Document([references[0] with { ConnectionEnvironmentVariable = "C04A_RECOVERY_TARGET_" + new string('A', 65) }, references[1]]),
            "too-large" => json + new string(' ', 16385),
            _ => json
        };
    }
}
