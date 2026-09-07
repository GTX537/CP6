using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class ProgramProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cp6-p10-inspection-test-" + Guid.NewGuid().ToString("N"));

    public ProgramProcessTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Missing_arguments_return_usage_without_acceptance()
    {
        var result = await Run();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("usage:", result.Error);
    }

    [Fact]
    public async Task Valid_trust_inspection_is_explicitly_not_candidate_acceptance()
    {
        var input = TrustBytes();
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore,
            Cp6DeterministicJson.Sha256Hex(input), Write("trust.json", input));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("candidateAccepted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("deployable").ValueKind);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(input), document.RootElement.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Noncanonical_trust_fails_even_when_its_raw_hash_matches()
    {
        var input = TrustBytes().Concat(new byte[] { (byte)'\n' }).ToArray();
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore,
            Cp6DeterministicJson.Sha256Hex(input), Write("noncanonical.json", input));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("non-canonical-json", result.Error.Trim());
    }

    [Fact]
    public async Task Structurally_valid_System_locator_is_refused_by_the_Platform_adapter()
    {
        var input = Encoding.UTF8.GetBytes("""
            {"$schemaId":"https://schemas.cp6.dev/release/candidate-locator.v1","createdAtUtc":"2026-09-01T00:00:00.000Z","releaseTag":"v0.10.1-test.1","signerKeyId":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","subject":{"byteLength":1,"key":"objects/sha256/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/candidate-result.v2.json","mediaType":"application/vnd.cp6.candidate-result.v2+json","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","storageAuthority":"cp6-release-r2-v1"},"subjectKind":"SystemCandidateResult","trustPolicyVersion":1}
            """);
        Assert.Equal("SystemCandidateResult", Cp6ReleaseValidator.ValidateCandidateLocator(input).SubjectKind);
        var result = await Run("inspect", Cp6ReleaseContractIds.CandidateLocator,
            Cp6DeterministicJson.Sha256Hex(input), Write("system-locator.fixture.json", input));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("inspection-lane", result.Error.Trim());
    }

    [Fact]
    public async Task Canonicalize_creates_once_and_never_overwrites_existing_bytes()
    {
        var input = Write("input.json", Encoding.UTF8.GetBytes("{ \"z\": 1, \"a\": 2 }"));
        var output = Path.Combine(_directory, "output.json");
        var first = await Run("canonicalize", input, output);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal("{\"a\":2,\"z\":1}", File.ReadAllText(output));
        var original = File.ReadAllBytes(output);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(original), first.Output.Trim());
        File.WriteAllText(input, "{\"different\":true}");
        var second = await Run("canonicalize", input, output);
        Assert.Equal(1, second.ExitCode);
        Assert.Equal("inspection-io", second.Error.Trim());
        Assert.Empty(second.Output);
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Missing_file_error_never_discloses_the_private_path()
    {
        var path = Path.Combine(_directory, "private-marker-do-not-disclose.json");
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore, new string('a', 64), path);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("inspection-io", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024 + 1, false)]
    public void Read_bound_enforces_exact_boundary(int size, bool allowed)
    {
        var path = Write("size.bin", new byte[size]);
        if (allowed) Assert.Equal(size, ContractInspection.ReadBounded(path).Length);
        else Assert.Equal("input-size", Assert.Throws<Cp6ReleaseContractException>(() => ContractInspection.ReadBounded(path)).Code);
    }

    private static byte[] TrustBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "CP6.P10.ReleaseVerifier.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "CP6.P10.ReleaseVerifier.Tests.deps.json"),
            typeof(ContractInspection).Assembly.Location
        }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start inspection process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public void Dispose()
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var actual = Path.GetFullPath(_directory);
        if (Path.GetDirectoryName(actual) != expectedParent || !Path.GetFileName(actual).StartsWith("cp6-p10-inspection-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup escaped its private temporary directory.");
        Directory.Delete(actual, recursive: true);
    }
}
