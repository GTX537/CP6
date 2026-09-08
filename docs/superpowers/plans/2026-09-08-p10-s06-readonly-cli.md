# P10 S06 read-only verification CLI implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Expose complete candidate verification and clean current-publication pre/post-commit confirmation as distinct read-only commands.

**Architecture:** The existing executable dispatches exact command shapes into a read-only adapter that takes credentials only from named environment variables. Normal acceptance always uses completed publisher verification; pre/post-commit paths use actual current publication identity and explicitly return no final candidate acceptance.

**Tech Stack:** .NET 8.0.424, existing verified candidate/intent components, pinned cosign and R2/GitHub/feed transports, xUnit subprocess tests.

---

## Scope and self-review

- `verify-platform TAG`: authenticated authoritative discovery and completed publisher/validation; success means `VerifiedNonDeployable`, not audit-ledger `Frozen`.
- `confirm-platform-intent TAG ARTIFACT_ID`: actual current-run immutable artifact, intended signed Locator and full remote proof.
- `confirm-platform-published TAG`: actual authoritative discovery and complete current-run confirmation.
- Confirmation output always has `candidateAccepted=false`, `deployable=false`, `publicationWorkflowCompleted=false`; a running publisher cannot claim its final success.
- Commands reject signing/publisher/private-audit environment inputs; server-side token read permissions are separately enforced by workflow resource configuration.
- Credentials are never command arguments, output, or exception details. Missing inputs have no ambient fallback.
- Preserve existing canonicalization/inspection behavior. Child tests scrub inherited P10 credentials and inject only non-secret markers into their own processes.
- No local file override, trust/endpoint override, remote writes, deployment or existing R2 gate changes.
- Positive command success remains pending real protected S06 OCI/R2 evidence; no fake acceptance output is introduced.

## Files

- Create: `tools/p10/ReleaseVerifier/ReadOnlyVerificationCommand.cs`
- Modify: `tools/p10/ReleaseVerifier/Program.cs`
- Modify/Test: `tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-readonly-cli.md`

### Task 1: Tests and RED

- [x] Replace the process test file with the complete version below.
- [x] Add throwing command scaffolds and wire the executable using the complete Program below.
- [x] Run focused tests. Existing inspection tests must remain green; every new behavioral case must fail from the scaffold instead of silently returning usage.

```csharp
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

    [Theory]
    [InlineData("verify-platform")]
    [InlineData("confirm-platform-published")]
    public async Task Verification_without_read_credentials_fails_without_acceptance(string command)
    {
        var result = await Run(command, "v0.10.1-p10.1");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("verification-credential", result.Error.Trim());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("1 ")]
    [InlineData("9223372036854775808")]
    [InlineData("private-marker-do-not-disclose")]
    public async Task Intent_artifact_IDs_are_canonical_positive_integers(string id)
    {
        var result = await Run("confirm-platform-intent", "v0.10.1-p10.1", id);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("verification-artifact-id", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData("verify-platform", "../v0.10.1", "release-tag")]
    [InlineData("confirm-platform-published", "v0.10.1\n", "r2-discovery-tag")]
    public async Task Readonly_verification_rejects_unsafe_discovery_tags(string command, string tag, string code)
    {
        var result = await Run(command, tag);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(code, result.Error.Trim());
        Assert.DoesNotContain(tag, result.Error);
    }

    [Fact]
    public async Task Intent_confirmation_accepts_no_local_file_substitute()
    {
        var result = await Run("confirm-platform-intent", "v0.10.1-p10.1", "/private/locator.json", "/private/bundle.json");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("usage:", result.Error);
        Assert.DoesNotContain("/private", result.Error);
    }

    [Theory]
    [InlineData("P10_LOCATOR_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_LOCATOR_COSIGN_PASSWORD")]
    [InlineData("P10_OCI_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_OCI_COSIGN_PASSWORD")]
    [InlineData("P10_R2_PUBLISH_ACCESS_KEY_ID")]
    [InlineData("P10_R2_PUBLISH_SECRET_ACCESS_KEY")]
    [InlineData("COSIGN_PASSWORD")]
    [InlineData("P10_CRM_ACTIONS_READ_TOKEN")]
    [InlineData("P10_CRM_READ_TOKEN")]
    public async Task Readonly_commands_refuse_signing_publisher_or_private_audit_credentials(string variable)
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?>
        {
            [variable] = "private-marker-do-not-disclose"
        }, "verify-platform", "v0.10.1-p10.1");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("verification-secret-scope", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData("P10_COSIGN_PATH")]
    [InlineData("P10_R2_CONSUMER_ACCESS_KEY_ID")]
    [InlineData("P10_R2_CONSUMER_SECRET_ACCESS_KEY")]
    [InlineData("P10_GITHUB_READ_TOKEN")]
    [InlineData("P10_FEED_READ_TOKEN")]
    public async Task Every_read_input_is_required_before_any_external_work(string missing)
    {
        var values = ReadOnlyVerificationCommand.RequiredVariables.ToDictionary(n => n,
            _ => (string?)"safe-unit-marker", StringComparer.Ordinal);
        values[missing] = null;
        var result = await RunWithEnvironment(values, "verify-platform", "v0.10.1-p10.1");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("verification-credential", result.Error.Trim());
    }

    [Fact]
    public async Task Read_credential_control_characters_never_reach_requests_or_diagnostics()
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?>
        {
            ["P10_COSIGN_PATH"] = "private-marker\n"
        }, "verify-platform", "v0.10.1-p10.1");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("verification-credential", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Fact]
    public async Task Precancelled_readonly_command_does_not_read_environment_or_network()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReadOnlyVerificationCommand.ExecuteAsync(["verify-platform", "v0.10.1-p10.1"], cancellation.Token));
    }

    private static byte[] TrustBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments) =>
        RunWithEnvironment(new Dictionary<string, string?>(), arguments);

    private static async Task<(int ExitCode, string Output, string Error)> RunWithEnvironment(
        IReadOnlyDictionary<string, string?> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var name in start.Environment.Keys.Where(n =>
            n.StartsWith("P10_", StringComparison.Ordinal) || n == "COSIGN_PASSWORD").ToArray())
            start.Environment.Remove(name);
        foreach (var pair in environment)
        {
            if (pair.Value is null) start.Environment.Remove(pair.Key);
            else start.Environment[pair.Key] = pair.Value;
        }
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
```

### Task 2: Minimal CLI integration

- [x] Apply the exact executable routing below.

```csharp
using System.Text.Json;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

try
{
    if (args is ["canonicalize", var inputPath, var outputPath])
    {
        var bytes = Cp6DeterministicJson.Canonicalize(ContractInspection.ReadBounded(inputPath));
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        Console.WriteLine(Cp6DeterministicJson.Sha256Hex(bytes));
        return 0;
    }
    if (args is ["inspect", var schemaId, var expectedHash, var path])
    {
        var result = ContractInspection.Inspect(ContractInspection.ReadBounded(path), schemaId, expectedHash);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(Cp6DeterministicJson.Canonicalize(bytes)));
        return 0;
    }
    if (ReadOnlyVerificationCommand.Matches(args))
    {
        var bytes = await ReadOnlyVerificationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    Console.Error.WriteLine("usage: canonicalize INPUT NEW_OUTPUT | inspect SCHEMA_ID EXPECTED_SHA256 INPUT | " +
        "verify-platform TAG | confirm-platform-intent TAG ARTIFACT_ID | confirm-platform-published TAG");
    return 2;
}
catch (Cp6ReleaseContractException error)
{
    Console.Error.WriteLine(error.Code);
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("inspection-io");
    return 1;
}
```

- [x] Replace the command scaffold with the implementation below.

```csharp
using System.Globalization;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Read-only CLI orchestration. Token permissions are additionally constrained by the protected workflow;
// absence of write-secret inputs alone is not a claim about a token's server-side permission scope.
internal static class ReadOnlyVerificationCommand
{
    internal static IReadOnlyList<string> RequiredVariables { get; } = Array.AsReadOnly(new[]
    {
        "P10_COSIGN_PATH", "P10_R2_CONSUMER_ACCESS_KEY_ID", "P10_R2_CONSUMER_SECRET_ACCESS_KEY",
        "P10_GITHUB_READ_TOKEN", "P10_FEED_READ_TOKEN"
    });

    internal static IReadOnlyList<string> ForbiddenVariables { get; } = Array.AsReadOnly(new[]
    {
        "P10_LOCATOR_COSIGN_PRIVATE_KEY", "P10_LOCATOR_COSIGN_PASSWORD",
        "P10_OCI_COSIGN_PRIVATE_KEY", "P10_OCI_COSIGN_PASSWORD",
        "P10_R2_PUBLISH_ACCESS_KEY_ID", "P10_R2_PUBLISH_SECRET_ACCESS_KEY",
        "COSIGN_PASSWORD", "P10_CRM_ACTIONS_READ_TOKEN", "P10_CRM_READ_TOKEN"
    });

    internal static bool Matches(string[] arguments) => arguments is ["verify-platform", _] or
        ["confirm-platform-published", _] or ["confirm-platform-intent", _, _];

    internal static async Task<byte[]> ExecuteAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        Require(Matches(args), "verification-command");
        var tag = args[1];
        _ = R2ObjectTarget.Discovery(tag, R2DiscoveryPart.Locator);
        long artifactId = 0;
        if (args[0] == "confirm-platform-intent")
            Require(long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out artifactId) &&
                artifactId > 0 && artifactId.ToString(CultureInfo.InvariantCulture) == args[2], "verification-artifact-id");
        Require(ForbiddenVariables.All(n => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))),
            "verification-secret-scope");
        var values = RequiredVariables.ToDictionary(n => n, Required, StringComparer.Ordinal);
        var cosign = new CosignBlobVerifier(values["P10_COSIGN_PATH"]);
        var id = values["P10_R2_CONSUMER_ACCESS_KEY_ID"];
        var secret = values["P10_R2_CONSUMER_SECRET_ACCESS_KEY"];
        var github = values["P10_GITHUB_READ_TOKEN"];
        var feed = values["P10_FEED_READ_TOKEN"];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        try
        {
            if (args[0] == "verify-platform")
            {
                var verified = await VerifiedPlatformCandidate.VerifyAsync(tag, id, secret, cosign, github, feed, deadline.Token);
                return S06ArtifactAssembly.Canonical(new
                {
                    state = verified.State, releaseTag = verified.ReleaseTag, sha256 = verified.Sha256,
                    candidateAccepted = verified.CandidateAccepted, deployable = verified.Deployable,
                    validationRunId = verified.ValidationRunId, publicationRunId = verified.PublicationRunId,
                    verifiedAtUtc = FormatTime(verified.VerifiedAtUtc)
                });
            }
            FetchedCandidateGraph fetched;
            if (args[0] == "confirm-platform-intent")
            {
                var intent = await S06PublicationIntent.ReadAsync(tag, artifactId, cosign, github, deadline.Token);
                fetched = await PlatformCandidateSource.IntendedAsync(tag, intent.CopyLocatorBytes(), intent.CopyBundleBytes(),
                    id, secret, cosign, deadline.Token);
            }
            else fetched = await PlatformCandidateSource.DiscoverAsync(tag, id, secret, cosign, deadline.Token);
            var confirmation = await S06PublicationConfirmation.VerifyAsync(fetched, cosign, github, feed, deadline.Token);
            return S06ArtifactAssembly.Canonical(new
            {
                state = args[0] == "confirm-platform-intent" ? "PreCommitVerified" : "PostCommitConfirmed",
                releaseTag = confirmation.ReleaseTag, sha256 = confirmation.CandidateSha256,
                candidateAccepted = false, deployable = false,
                publicationWorkflowCompleted = confirmation.PublicationWorkflowCompleted,
                validationRunId = confirmation.Validation.Workflow.RunId,
                publicationRunId = confirmation.Publication.Workflow.RunId,
                verifiedAtUtc = FormatTime(DateTimeOffset.UtcNow)
            });
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("verification-timeout");
        }
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl), "verification-credential");
        return value!;
    }
}
```

- [x] Run focused tests, then full Release suite and formatting.

### Task 3: Review and commit

- [x] Verify all three code blocks match the files and inspect the exact four-file diff.
- [x] Scan for secret/debug residue and verify no unrelated files are staged.
- [ ] Commit `feat(p10): expose separate read-only candidate verification commands`.
- [ ] Exercise all three positive command paths through actual protected S06 runs before final acceptance.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProgramProcessTests
  if ($LASTEXITCODE -ne 0) { throw 'Focused verification failed.' }
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw 'Full verification failed.' }
  & $env:DOTNET_HOST_PATH format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
} finally {
  Remove-Item Env:P10_GITHUB_READ_TOKEN -ErrorAction SilentlyContinue
  Remove-Item Env:P10_FEED_READ_TOKEN -ErrorAction SilentlyContinue
}
```

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-readonly-cli.md tools/p10/ReleaseVerifier/ReadOnlyVerificationCommand.cs tools/p10/ReleaseVerifier/Program.cs tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): expose separate read-only candidate verification commands"
```

## Verification outcome (2026-09-08)

- RED: 28 newly added cases failed from the command scaffold; all 9 existing inspection cases remained green. No skips.
- Focused GREEN: 37 passed, 0 failed, 0 skipped.
- Full Release suite: 1,378 passed, 0 failed, 0 skipped in 47 seconds; formatting exited 0.
- All positive remote command paths still require the actual protected S06 workflow/OCI/R2 run. No fake acceptance or external write was performed.
