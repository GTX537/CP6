# P10 S06 Publication CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user already selected no delegation.

**Goal:** Expose the three actual conditional-publication phases with exact arguments, explicit credentials and no acceptance or skip-verification switch.

**Architecture:** PublicationCommand dispatches only prepare-publication, store-publication-bundle and commit-publication to the implemented S06Publisher. Signing and CRM private-read credentials are forbidden in these processes. Preparation needs the fixed publishing and GitHub/feed/cosign inputs; bundle and commit additionally require explicit R2 readers. Program preserves existing command behavior and emits only canonical non-acceptance phase summaries.

**Tech Stack:** .NET 8, xUnit process tests, existing CP6.Platform.Release [0.10.1].

---

## Scope and self-review

All run/attempt/artifact selectors are canonical positive integers; attempts are bounded by int.MaxValue. A tag passes the existing Platform discovery policy before any environment or I/O work. No local file replaces an immutable intent artifact and no boolean replaces the actual new read-only child. Commands reject signing/CRM credentials before reading any allowed secret. Values are bounded and control-character-free and are never echoed. Existing ProgramProcessTests erase inherited P10/signing/current-workflow variables in the test child only; these tests never invoke an actual publisher.

The six-field summary is a phase result: state, releaseTag, conditionalCreate, candidateAccepted=false, deployable=false and publicationWorkflowCompleted=false. PublishedUnconfirmed applies even to a verified identical 412: final postcheck and completed publication remain separate.

## Files

- Create tools/p10/ReleaseVerifier/PublicationCommand.cs.
- Modify tools/p10/ReleaseVerifier/Program.cs.
- Extend tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs without changing its existing cases.

## Task 1: Red process tests

- [ ] Add the full test file below, plus a PublicationCommand scaffold whose ExecuteAsync throws and RequiredVariables has the listed five names. Keep Program unchanged for the red run.

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

    [Theory]
    [InlineData("prepare")]
    [InlineData("bundle")]
    [InlineData("commit")]
    public async Task Publication_commands_require_credentials_before_any_IO(string phase)
    {
        var result = await Run(PublicationArgs(phase));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("publication-credential", result.Error.Trim());
    }

    [Theory]
    [InlineData("prepare", 2, "0")]
    [InlineData("prepare", 2, "01")]
    [InlineData("prepare", 3, "2147483648")]
    [InlineData("prepare", 4, "-1")]
    [InlineData("commit", 2, "+1")]
    [InlineData("commit", 2, "1 ")]
    [InlineData("commit", 2, "9223372036854775808")]
    [InlineData("commit", 2, "/private/locator.json")]
    public async Task Publisher_selection_is_canonical_bounded_and_not_a_local_file(string phase, int index, string value)
    {
        var args = PublicationArgs(phase);
        args[index] = value;
        var result = await Run(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("publication-selection", result.Error.Trim());
        Assert.DoesNotContain("/private", result.Error);
    }

    [Theory]
    [InlineData("P10_LOCATOR_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_LOCATOR_COSIGN_PASSWORD")]
    [InlineData("P10_OCI_COSIGN_PRIVATE_KEY")]
    [InlineData("P10_OCI_COSIGN_PASSWORD")]
    [InlineData("COSIGN_PASSWORD")]
    [InlineData("P10_CRM_ACTIONS_READ_TOKEN")]
    [InlineData("P10_CRM_READ_TOKEN")]
    public async Task Publisher_commands_cannot_receive_signing_or_private_CRM_credentials(string variable)
    {
        var result = await RunWithEnvironment(new Dictionary<string, string?> { [variable] = "private-marker" },
            PublicationArgs("commit"));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("publication-secret-scope", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData("P10_COSIGN_PATH")]
    [InlineData("P10_GITHUB_READ_TOKEN")]
    [InlineData("P10_FEED_READ_TOKEN")]
    [InlineData("P10_R2_PUBLISH_ACCESS_KEY_ID")]
    [InlineData("P10_R2_PUBLISH_SECRET_ACCESS_KEY")]
    public async Task Every_publisher_input_is_required_without_disclosing_it(string missing)
    {
        var values = PublicationCommand.RequiredVariables.ToDictionary(n => n, _ => (string?)"private-marker", StringComparer.Ordinal);
        values[missing] = null;
        var result = await RunWithEnvironment(values, PublicationArgs("prepare"));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("publication-credential", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData("P10_R2_CONSUMER_ACCESS_KEY_ID")]
    [InlineData("P10_R2_CONSUMER_SECRET_ACCESS_KEY")]
    public async Task Commit_requires_separate_explicit_reader_inputs(string missing)
    {
        var values = PublicationCommand.RequiredVariables.ToDictionary(n => n, _ => (string?)"private-marker", StringComparer.Ordinal);
        values["P10_R2_CONSUMER_ACCESS_KEY_ID"] = "private-marker";
        values["P10_R2_CONSUMER_SECRET_ACCESS_KEY"] = "private-marker";
        values[missing] = null;
        var result = await RunWithEnvironment(values, PublicationArgs("commit"));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("publication-credential", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Fact]
    public async Task Supplying_credentials_cannot_replace_the_actual_current_publication_workflow()
    {
        var values = PublicationCommand.RequiredVariables.ToDictionary(n => n, _ => (string?)"private-marker", StringComparer.Ordinal);
        var result = await RunWithEnvironment(values, PublicationArgs("prepare"));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("s06-current-environment", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Fact]
    public async Task Commit_has_no_skip_verification_argument()
    {
        var result = await Run(PublicationArgs("commit").Append("--clean-success=true").ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("usage:", result.Error);
        Assert.DoesNotContain("--clean-success=true", result.Error);
    }

    [Fact]
    public async Task Precancelled_publication_command_performs_no_IO()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PublicationCommand.ExecuteAsync(PublicationArgs("commit"), cancellation.Token));
    }

    private static string[] PublicationArgs(string phase) => phase switch
    {
        "prepare" => ["prepare-publication", "v0.10.1-p10.1", "1", "1", "2", "/never-created"],
        "bundle" => ["store-publication-bundle", "v0.10.1-p10.1", "/never-read", "/never-created"],
        _ => ["commit-publication", "v0.10.1-p10.1", "3"]
    };

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

- [ ] Run the process suite and verify new recognized-command expectations fail because Program still returns usage. Existing tests and the extra-argument rejection may already pass; the precancellation case must fail against the throwing scaffold.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProgramProcessTests
```

## Task 2: Implement command and wire Program

- [ ] Replace the new scaffold with this complete implementation:

```csharp
using System.Globalization;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Secret-bearing publisher CLI. It never signs, accepts a verification boolean, or claims candidate acceptance.
internal static class PublicationCommand
{
    internal static IReadOnlyList<string> RequiredVariables { get; } = Array.AsReadOnly(new[]
    {
        "P10_COSIGN_PATH", "P10_GITHUB_READ_TOKEN", "P10_FEED_READ_TOKEN",
        "P10_R2_PUBLISH_ACCESS_KEY_ID", "P10_R2_PUBLISH_SECRET_ACCESS_KEY"
    });

    internal static bool Matches(string[] arguments) => arguments is ["prepare-publication", _, _, _, _, _] or
        ["store-publication-bundle", _, _, _] or ["commit-publication", _, _];

    internal static async Task<byte[]> ExecuteAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        Require(Matches(args), "publication-command");
        var tag = args[1];
        _ = R2ObjectTarget.Discovery(tag, R2DiscoveryPart.Locator);
        long run = 0, attempt = 0, artifact = 0;
        if (args[0] == "prepare-publication")
        {
            run = Positive(args[2], long.MaxValue);
            attempt = Positive(args[3], int.MaxValue);
            artifact = Positive(args[4], long.MaxValue);
        }
        if (args[0] == "commit-publication") artifact = Positive(args[2], long.MaxValue);
        var forbidden = ReadOnlyVerificationCommand.ForbiddenVariables.Where(n =>
            n is not ("P10_R2_PUBLISH_ACCESS_KEY_ID" or "P10_R2_PUBLISH_SECRET_ACCESS_KEY"));
        Require(forbidden.All(n => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))), "publication-secret-scope");
        var values = RequiredVariables.ToDictionary(n => n, Required, StringComparer.Ordinal);
        var cosignPath = values["P10_COSIGN_PATH"];
        var github = values["P10_GITHUB_READ_TOKEN"];
        var feed = values["P10_FEED_READ_TOKEN"];
        var publishId = values["P10_R2_PUBLISH_ACCESS_KEY_ID"];
        var publishSecret = values["P10_R2_PUBLISH_SECRET_ACCESS_KEY"];
        string state;
        string? conditionalCreate = null;
        if (args[0] == "prepare-publication")
        {
            await S06Publisher.PrepareAsync(tag, run, attempt, artifact, args[5],
                new CosignBlobVerifier(cosignPath), github, feed, publishId, publishSecret, cancellationToken);
            state = "PreparedForLocatorSigning";
        }
        else
        {
            var readId = Required("P10_R2_CONSUMER_ACCESS_KEY_ID");
            var readSecret = Required("P10_R2_CONSUMER_SECRET_ACCESS_KEY");
            if (args[0] == "store-publication-bundle")
            {
                await S06Publisher.StoreBundleAsync(tag, args[2], args[3], new CosignBlobVerifier(cosignPath),
                    github, feed, readId, readSecret, publishId, publishSecret, cancellationToken);
                state = "StoredBundleForIntent";
            }
            else
            {
                conditionalCreate = (await S06Publisher.CommitAsync(tag, artifact, cosignPath, github, feed,
                    readId, readSecret, publishId, publishSecret, cancellationToken)).ToString();
                state = "PublishedUnconfirmed";
            }
        }
        return S06ArtifactAssembly.Canonical(new
        {
            state,
            releaseTag = tag,
            conditionalCreate,
            candidateAccepted = false,
            deployable = false,
            publicationWorkflowCompleted = false
        });
    }

    private static long Positive(string text, long maximum)
    {
        Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value > 0 && value <= maximum && value.ToString(CultureInfo.InvariantCulture) == text, "publication-selection");
        return value;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl), "publication-credential");
        return value!;
    }
}
```

- [ ] Replace Program with:

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
    if (PublicationCommand.Matches(args))
    {
        var bytes = await PublicationCommand.ExecuteAsync(args);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    Console.Error.WriteLine("usage: canonicalize INPUT NEW_OUTPUT | inspect SCHEMA_ID EXPECTED_SHA256 INPUT | " +
        "verify-platform TAG | confirm-platform-intent TAG ARTIFACT_ID | confirm-platform-published TAG | " +
        "prepare-validation NEW_STAGE | finalize-validation STAGE IMAGE_INPUTS NEW_ARTIFACT | " +
        "prepare-publication TAG VALIDATION_RUN VALIDATION_ATTEMPT VALIDATION_ARTIFACT NEW_STAGE | " +
        "store-publication-bundle TAG SIGNED_STAGE NEW_INTENT | commit-publication TAG INTENT_ARTIFACT");
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

## Task 3: Verification and scoped commit

- [ ] From tools/p10 use the task's configured .NET 8 host and actual official cosign, formal signed-package readback and GitHub/feed read credentials; do not print values or skip real tests:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProgramProcessTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

Expected: all cases pass, zero skips, formatting exits 0. Check three C# blocks match actual files; review the four-file diff for sensitive values, private paths, unsupported flags, broad changes and false acceptance.

- [ ] Record actual results then commit only:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-publication-cli.md tools/p10/ReleaseVerifier/PublicationCommand.cs tools/p10/ReleaseVerifier/Program.cs tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): expose protected publication phases"
```

Actual hosted end-to-end publication, postconfirmation and final P10 closeout are still required.

## Execution evidence

2026-09-08: the red process run failed 27 new expectations (unrecognized commands or the throwing cancellation scaffold); 50 existing/extra-argument cases passed. After wiring, all 77 process cases passed. Full verification passed 1,518/1,518 with zero skips in 78 seconds; format verification exited 0. Three plan/code blocks match, and the scoped CLI diff was reviewed. No external S06 publication was attempted.
