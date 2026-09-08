# P10 S06 validation collector CLI implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Wire protected two-phase validation collection into the executable with explicit reader-only credentials and no fake completion flags.

**Architecture:** Exact prepare/finalize command shapes call the real collector. Preparation alone accepts the private CRM reader; both phases reject signing and R2 publisher secrets. Output contains safe producer/hash/count summaries and never local paths or candidate acceptance.

**Tech Stack:** .NET 8.0.424, existing collector/local-file adapters, canonical JSON and xUnit subprocess tests.

---

## Scope and self-review

- `prepare-validation NEW_STAGE` reads P10_GITHUB_READ_TOKEN, P10_FEED_READ_TOKEN and P10_CRM_READ_TOKEN; all three are explicit and nonempty.
- `finalize-validation STAGE IMAGE_INPUTS NEW_ARTIFACT` needs public GitHub/feed readers and P10_COSIGN_PATH, and refuses private CRM credentials.
- Both commands require the real hosted current validation job through the collector, regardless of whether credentials are present.
- No remote-write API, signing/private-key processing, skip flag, source override or final workflow-success claim.
- Existing inspection and normal candidate verification commands remain unchanged.
- Child process tests now also scrub the selected GitHub/runner identity fields so a CI test cannot inadvertently impersonate its own live workflow.
- Positive hosted workflow acceptance remains open; local rejection tests cannot satisfy S06.

## Files

- Create: `tools/p10/ReleaseVerifier/ValidationCommand.cs`
- Modify: `tools/p10/ReleaseVerifier/Program.cs`
- Modify/Test: `tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-validation-cli.md`

### Task 1: Tests and RED

- [x] Apply the full test file below, add throwing collector-command scaffolds and wire Program.
- [x] Run focused tests; require every new case to fail from the scaffold while existing cases remain green.

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

    [Theory]
    [InlineData("prepare")]
    [InlineData("finalize")]
    public async Task Collector_commands_require_read_credentials_before_workflow_or_file_access(string phase)
    {
        var args = phase == "prepare" ? new[] { "prepare-validation", "/never-created" } :
            new[] { "finalize-validation", "/never-read", "/never-read", "/never-created" };
        var result = await Run(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("validation-credential", result.Error.Trim());
        Assert.DoesNotContain("/never", result.Error);
    }

    [Theory]
    [InlineData("P10_GITHUB_READ_TOKEN")]
    [InlineData("P10_FEED_READ_TOKEN")]
    [InlineData("P10_CRM_READ_TOKEN")]
    public async Task Preparation_requires_each_explicit_reader(string missing)
    {
        var values = new Dictionary<string, string?>
        {
            ["P10_GITHUB_READ_TOKEN"] = "safe-marker",
            ["P10_FEED_READ_TOKEN"] = "safe-marker",
            ["P10_CRM_READ_TOKEN"] = "safe-marker"
        };
        values[missing] = null;
        var result = await RunWithEnvironment(values, "prepare-validation", "/never-created");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("validation-credential", result.Error.Trim());
    }

    [Fact]
    public async Task Private_CRM_read_is_allowed_only_for_preparation_not_as_current_workflow_proof()
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?>
        {
            ["P10_GITHUB_READ_TOKEN"] = "safe-marker",
            ["P10_FEED_READ_TOKEN"] = "safe-marker",
            ["P10_CRM_READ_TOKEN"] = "safe-marker"
        }, "prepare-validation", "/never-created");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("s06-current-environment", result.Error.Trim());
    }

    [Theory]
    [InlineData("P10_LOCATOR_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_OCI_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_R2_PUBLISH_SECRET_ACCESS_KEY")]
    public async Task Preparation_cannot_run_with_signing_or_publication_secrets(string variable)
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?> { [variable] = "private-marker" },
            "prepare-validation", "/never-created");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("validation-secret-scope", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Fact]
    public async Task Finalization_refuses_private_CRM_credentials()
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?> { ["P10_CRM_READ_TOKEN"] = "private-marker" },
            "finalize-validation", "/never-read", "/never-read", "/never-created");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("validation-secret-scope", result.Error.Trim());
    }

    [Fact]
    public async Task Collector_commands_accept_no_extra_boolean_or_file_override()
    {
        var result = await Run("finalize-validation", "/never-read", "/never-read", "/never-created", "skip-verification");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("usage:", result.Error);
        Assert.DoesNotContain("skip-verification", result.Error);
    }

    [Fact]
    public async Task Precancelled_collector_command_does_not_create_a_stage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ValidationCommand.ExecuteAsync(["prepare-validation", "/never-created"], cancellation.Token));
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
        foreach (var name in S06CurrentWorkflowChecks.EnvironmentNames) start.Environment.Remove(name);
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

### Task 2: Program and collector command

- [x] Apply the exact Program routing.

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
    if (ValidationCommand.Matches(args))
    {
        var bytes = await ValidationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    Console.Error.WriteLine("usage: canonicalize INPUT NEW_OUTPUT | inspect SCHEMA_ID EXPECTED_SHA256 INPUT | " +
        "verify-platform TAG | confirm-platform-intent TAG ARTIFACT_ID | confirm-platform-published TAG | " +
        "prepare-validation NEW_STAGE | finalize-validation STAGE IMAGE_INPUTS NEW_ARTIFACT");
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

- [x] Replace the command scaffold.

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Hosted collector commands write only fresh local public handoff directories.
// Signing/build/push remain explicit protected workflow steps; these commands have no remote-write API.
internal static class ValidationCommand
{
    internal static bool Matches(string[] arguments) => arguments is ["prepare-validation", _] or
        ["finalize-validation", _, _, _];

    internal static async Task<byte[]> ExecuteAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        Require(Matches(args), "validation-command");
        var prepare = args[0] == "prepare-validation";
        var forbidden = ReadOnlyVerificationCommand.ForbiddenVariables
            .Where(n => !prepare || n != "P10_CRM_READ_TOKEN");
        Require(forbidden.All(n => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))), "validation-secret-scope");
        var github = Required("P10_GITHUB_READ_TOKEN");
        var feed = Required("P10_FEED_READ_TOKEN");
        if (prepare)
        {
            var crm = Required("P10_CRM_READ_TOKEN");
            var workflow = await S06ValidationCollector.PrepareAsync(args[1], github, crm, feed, cancellationToken);
            return S06ArtifactAssembly.Canonical(new
            {
                state = "ValidationPreparationCreated", producer = Workflow(workflow),
                fileCount = S06ValidationInputs.PreparationNames.Count, candidateAccepted = false, deployable = false
            });
        }
        var cosign = new CosignBlobVerifier(Required("P10_COSIGN_PATH"));
        var artifact = await S06ValidationCollector.FinalizeAsync(args[1], args[2], args[3], cosign, github, feed, cancellationToken);
        return S06ArtifactAssembly.Canonical(new
        {
            state = "ValidationArtifactCreated", producer = Workflow(artifact.Producer),
            indexSha256 = Cp6DeterministicJson.Sha256Hex(artifact.CopyIndexBytes()),
            fileCount = artifact.CopyFiles().Count, candidateAccepted = false, deployable = false,
            validationWorkflowCompleted = false
        });
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl), "validation-credential");
        return value!;
    }
}
```

- [x] Rerun focused tests and require all passing.

### Task 3: Verify and commit

- [x] Run full Release tests and formatting; require no failures/skips.
- [x] Check plan/source agreement, exact four-file diff and secret/debug hygiene.
- [ ] Stage only these files and commit `feat(p10): expose hosted validation collection commands`.
- [ ] Exercise both commands in the real protected S06 validation workflow before final S06 acceptance.

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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-validation-cli.md tools/p10/ReleaseVerifier/ValidationCommand.cs tools/p10/ReleaseVerifier/Program.cs tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): expose hosted validation collection commands"
```

## Verification outcome (2026-09-08)

- RED: 12 new cases failed from the command scaffold; 37 existing cases stayed green; no skips.
- Focused GREEN: all 49 process cases passed.
- Full Release suite: 1,440 passed, 0 failed, 0 skipped in 47 seconds; formatting exited 0.
- Hosted positive preparation/finalization and real native workflow acceptance remain pending.
