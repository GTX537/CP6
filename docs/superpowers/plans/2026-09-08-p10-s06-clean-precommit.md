# P10 S06 clean precommit process implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Require a real separate read-only verifier process bound to the current immutable intent before the publisher can commit a Locator.

**Architecture:** The publisher-side adapter launches the same compiled executable through its actual dotnet host with a cleared environment containing only explicit readers and selected current-workflow identity. It waits for successful exit and exact bounded canonical confirmation output; no serialized success flag, supplied executable or process mock can replace execution.

**Tech Stack:** .NET 8.0.424 Process APIs, fixed existing readonly CLI, canonical JSON and xUnit pure profile tests.

---

## Scope and self-review

- Parent input is the private-constructor authenticated current-run intent, not caller-owned local Locator bytes.
- The child independently re-downloads the immutable intent by artifact ID and repeats real signature/R2/package/OCI/workflow checks using confirm-platform-intent.
- Do not pass publisher, signing, private CRM, NuGet restore, proxy or arbitrary ambient environment inputs to the child.
- Reader token permissions still rely on the protected workflow's read-only scope; environment clearing is not a server-permission attestation.
- Add locatorSha256 and intentArtifactId to confirmation output to bind the exact intended Locator, not merely the same candidate subject hash.
- Launch the actual current dotnet host and assembly only; no external override/factory. Standard output/error each bounded to 64 KiB, 25-minute cancellation deadline, kill/wait on failure.
- Use a new private temporary home/work directory and remove only that validated process-owned directory afterward; stored content can only be public evidence/certificate caches, not credentials.
- Pure launch/IPC vectors return no trusted verification capability. Full positive execution remains pending real protected S06 publication.
- No R2 write in this module, production deployment or changes to old R2 gates.

## Files

- Create: `tools/p10/ReleaseVerifier/S06CleanProcessProfile.cs`
- Create: `tools/p10/ReleaseVerifier/S06CleanPrecommit.cs`
- Modify: `tools/p10/ReleaseVerifier/ReadOnlyVerificationCommand.cs`
- Test: `tools/p10/ReleaseVerifier.Tests/S06CleanPrecommitTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-clean-precommit.md`

### Task 1: Tests and RED

- [ ] Add tests and throwing profile/process scaffolds with the signatures below.
- [ ] Run focused tests; require missing-implementation failures after compilation, no skips.

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Pure launch/IPC vectors only. A valid summary vector is not the result of S06CleanPrecommit.VerifyAsync.
public sealed class S06CleanPrecommitTests
{
    private static DateTimeOffset Time => new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static string LocatorSha => new('b', 64);
    private static string CandidateSha => new('c', 64);

    [Fact]
    public void Process_uses_only_fixed_command_arguments_and_explicit_readers()
    {
        var workflow = Workflow();
        workflow["P10_R2_PUBLISH_SECRET_ACCESS_KEY"] = "private-marker";
        workflow["P10_LOCATOR_COSIGN_PRIVATE_KEY"] = "private-marker";
        var start = S06CleanProcessProfile.Start(Host(), Assembly(), Path.GetTempPath(), workflow, Readers(), "v0.10.1-p10.1", 123);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.True(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError);
        Assert.Equal(new[] { Assembly(), "confirm-platform-intent", "v0.10.1-p10.1", "123" }, start.ArgumentList);
        Assert.DoesNotContain(start.Environment.Keys, n => ReadOnlyVerificationCommand.ForbiddenVariables.Contains(n));
        Assert.DoesNotContain("PATH", start.Environment.Keys);
        Assert.DoesNotContain("private-marker", start.Environment.Values);
        Assert.All(ReadOnlyVerificationCommand.RequiredVariables, n => Assert.Equal(Readers()[n], start.Environment[n]));
    }

    [Theory]
    [InlineData("missing-reader")]
    [InlineData("extra-reader")]
    [InlineData("control-reader")]
    [InlineData("wrong-job")]
    [InlineData("relative-host")]
    [InlineData("wrong-host")]
    [InlineData("wrong-assembly")]
    public void Process_profile_rejects_ambient_or_ambiguous_execution_inputs(string mutation)
    {
        var workflow = Workflow();
        var readers = Readers();
        var host = Host();
        var assembly = Assembly();
        if (mutation == "missing-reader") readers.Remove("P10_FEED_READ_TOKEN");
        if (mutation == "extra-reader") readers.Add("P10_OCI_COSIGN_PRIVATE_KEY", "private-marker");
        if (mutation == "control-reader") readers["P10_FEED_READ_TOKEN"] = "bad\n";
        if (mutation == "wrong-job") workflow["GITHUB_JOB"] = "validate";
        if (mutation == "relative-host") host = "dotnet";
        if (mutation == "wrong-host") host = Path.Combine(Path.GetTempPath(), "other.exe");
        if (mutation == "wrong-assembly") assembly = Path.Combine(Path.GetTempPath(), "other.dll");
        Assert.Throws<Cp6ReleaseContractException>(() =>
            S06CleanProcessProfile.Start(host, assembly, Path.GetTempPath(), workflow, readers, "v0.10.1-p10.1", 123));
    }

    [Fact]
    public void Exact_precommit_summary_is_a_valid_IPC_message_not_candidate_acceptance() => Check(Summary());

    [Theory]
    [InlineData("state")]
    [InlineData("releaseTag")]
    [InlineData("sha256")]
    [InlineData("locatorSha256")]
    [InlineData("intentArtifactId")]
    [InlineData("publicationRunId")]
    [InlineData("validationRunId")]
    [InlineData("candidateAccepted")]
    [InlineData("deployable")]
    [InlineData("publicationWorkflowCompleted")]
    [InlineData("verifiedAtUtc")]
    [InlineData("extra")]
    [InlineData("missing")]
    public void Summary_must_bind_the_exact_intent_and_never_claim_finished_publication(string mutation)
    {
        var root = JsonNode.Parse(Summary())!.AsObject();
        if (mutation is "state" or "releaseTag" or "sha256" or "locatorSha256") root[mutation] = "wrong";
        if (mutation is "intentArtifactId" or "publicationRunId" or "validationRunId") root[mutation] = 0;
        if (mutation is "candidateAccepted" or "deployable" or "publicationWorkflowCompleted") root[mutation] = true;
        if (mutation == "verifiedAtUtc") root[mutation] = "2026-09-01T12:00:02.000Z";
        if (mutation == "extra") root["extra"] = true;
        if (mutation == "missing") root.Remove("locatorSha256");
        Assert.Throws<Cp6ReleaseContractException>(() => Check(
            Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root))));
    }

    [Fact]
    public void Noncanonical_child_output_is_rejected() =>
        Assert.Throws<Cp6ReleaseContractException>(() => Check(Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(Summary()))));

    [Fact]
    public async Task Precancelled_precommit_starts_no_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06CleanPrecommit.VerifyAsync(null!, "", "", "", "", "", cancellation.Token));
    }

    private static void Check(byte[] bytes) => S06CleanProcessProfile.RequireSummary(bytes.Append((byte)'\n').ToArray(),
        "v0.10.1-p10.1", 123, LocatorSha, CandidateSha, 456, Time, Time.AddSeconds(1));

    private static byte[] Summary() => S06ArtifactAssembly.Canonical(new
    {
        state = "PreCommitVerified",
        releaseTag = "v0.10.1-p10.1",
        sha256 = CandidateSha,
        locatorSha256 = LocatorSha,
        intentArtifactId = 123,
        candidateAccepted = false,
        deployable = false,
        publicationWorkflowCompleted = false,
        validationRunId = 321,
        publicationRunId = 456,
        verifiedAtUtc = "2026-09-01T12:00:00.500Z"
    });

    private static string Host() => Path.Combine(Path.GetTempPath(), OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    private static string Assembly() => Path.Combine(Path.GetTempPath(), "CP6.P10.ReleaseVerifier.dll");

    private static Dictionary<string, string> Readers() => ReadOnlyVerificationCommand.RequiredVariables
        .ToDictionary(n => n, _ => "safe-unit-marker", StringComparer.Ordinal);

    private static Dictionary<string, string?> Workflow() => new(StringComparer.Ordinal)
    {
        ["GITHUB_ACTIONS"] = "true",
        ["GITHUB_REPOSITORY"] = "GTX537/CP6",
        ["GITHUB_REPOSITORY_ID"] = "1214929352",
        ["GITHUB_REF"] = "refs/heads/main",
        ["GITHUB_EVENT_NAME"] = "workflow_dispatch",
        ["GITHUB_SHA"] = new string('a', 40),
        ["GITHUB_WORKFLOW_REF"] = "GTX537/CP6/" + S06ReleaseIdentity.PublicationPath + "@refs/heads/main",
        ["GITHUB_WORKFLOW_SHA"] = new string('a', 40),
        ["GITHUB_RUN_ID"] = "456",
        ["GITHUB_RUN_ATTEMPT"] = "1",
        ["GITHUB_JOB"] = "publish",
        ["GITHUB_SERVER_URL"] = "https://github.com",
        ["GITHUB_API_URL"] = "https://api.github.com",
        ["RUNNER_ENVIRONMENT"] = "github-hosted",
        ["RUNNER_OS"] = "Linux",
        ["RUNNER_ARCH"] = "X64"
    };
}
```

### Task 2: Profile, execution and exact confirmation binding

- [ ] Implement the pure launch/IPC profile.

```csharp
using System.Diagnostics;
using System.Globalization;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Pure process/summary profile. It neither launches a process nor returns a verification capability.
internal static class S06CleanProcessProfile
{
    internal static ProcessStartInfo Start(string hostPath, string assemblyPath, string directory,
        IReadOnlyDictionary<string, string?> workflowEnvironment, IReadOnlyDictionary<string, string> readEnvironment,
        string releaseTag, long artifactId)
    {
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(artifactId > 0 && Path.IsPathFullyQualified(hostPath) &&
            Path.GetFileNameWithoutExtension(hostPath) == "dotnet" && Path.IsPathFullyQualified(assemblyPath) &&
            Path.GetFileName(assemblyPath) == "CP6.P10.ReleaseVerifier.dll" && Path.IsPathFullyQualified(directory),
            "clean-process-path");
        _ = S06CurrentWorkflowChecks.ReadEnvironment(workflowEnvironment, S06ReleaseIdentity.PublicationPath);
        Require(readEnvironment.Keys.Order(StringComparer.Ordinal).SequenceEqual(
            ReadOnlyVerificationCommand.RequiredVariables.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            readEnvironment.Values.All(v => v is { Length: > 0 and <= 4096 } && !v.Any(char.IsControl)), "clean-process-environment");
        var start = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
        };
        start.Environment.Clear();
        foreach (var name in S06CurrentWorkflowChecks.EnvironmentNames) start.Environment[name] = workflowEnvironment[name]!;
        foreach (var pair in readEnvironment) start.Environment[pair.Key] = pair.Value;
        foreach (var name in new[] { "SystemRoot", "WINDIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        foreach (var name in new[] { "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR" }) start.Environment[name] = directory;
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var argument in new[] { assemblyPath, "confirm-platform-intent", releaseTag,
            artifactId.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void RequireSummary(ReadOnlyMemory<byte> output, string releaseTag, long artifactId,
        string locatorSha256, string candidateSha256, long publicationRunId, DateTimeOffset started, DateTimeOffset completed)
    {
        Require(output.Length is > 1 and <= 65536 && output.Span[^1] == (byte)'\n', "clean-process-output");
        var length = output.Length - 1;
        if (output.Span[length - 1] == (byte)'\r') length--;
        var bytes = output[..length].ToArray();
        Require(bytes.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(bytes)), "clean-process-output");
        try
        {
            var root = GitHubApiJson.Parse(bytes);
            Exact(root, "state", "releaseTag", "sha256", "locatorSha256", "intentArtifactId", "candidateAccepted", "deployable",
                "publicationWorkflowCompleted", "validationRunId", "publicationRunId", "verifiedAtUtc");
            Require(Text(root, "state") == "PreCommitVerified" && Text(root, "releaseTag") == releaseTag &&
                Text(root, "sha256") == candidateSha256 && Text(root, "locatorSha256") == locatorSha256 &&
                root.GetProperty("intentArtifactId").GetInt64() == artifactId &&
                root.GetProperty("publicationRunId").GetInt64() == publicationRunId &&
                root.GetProperty("validationRunId").GetInt64() > 0 &&
                root.GetProperty("validationRunId").GetInt64() != publicationRunId &&
                root.GetProperty("candidateAccepted").ValueKind == System.Text.Json.JsonValueKind.False &&
                root.GetProperty("deployable").ValueKind == System.Text.Json.JsonValueKind.False &&
                root.GetProperty("publicationWorkflowCompleted").ValueKind == System.Text.Json.JsonValueKind.False, "clean-process-binding");
            var verified = Time(root, "verifiedAtUtc");
            Require(started.Offset == TimeSpan.Zero && completed.Offset == TimeSpan.Zero && started <= completed &&
                verified >= DateTimeOffset.FromUnixTimeMilliseconds(started.ToUnixTimeMilliseconds()) &&
                verified <= completed, "clean-process-time");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("clean-process-output"); }
    }
}
```

- [ ] Implement the actual new-process verification.

```csharp
using System.Diagnostics;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Every commit must await this actual new read-only process against the immutable current-run intent.
// No serialized success flag, process factory or caller-selected executable is accepted here.
internal static class S06CleanPrecommit
{
    internal static async Task VerifyAsync(S06PublicationIntent intent, string readAccessKeyId, string readSecret,
        string cosignPath, string githubReadToken, string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workflow = S06CurrentWorkflowChecks.EnvironmentNames.ToDictionary(n => n,
            Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        var readers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P10_COSIGN_PATH"] = cosignPath,
            ["P10_R2_CONSUMER_ACCESS_KEY_ID"] = readAccessKeyId,
            ["P10_R2_CONSUMER_SECRET_ACCESS_KEY"] = readSecret,
            ["P10_GITHUB_READ_TOKEN"] = githubReadToken,
            ["P10_FEED_READ_TOKEN"] = feedReadToken
        };
        string? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("cp6-p10-clean-precommit-").FullName;
            var start = S06CleanProcessProfile.Start(Environment.ProcessPath ?? "",
                typeof(S06CleanPrecommit).Assembly.Location, directory, workflow, readers,
                intent.Locator.ReleaseTag, intent.Metadata.Id);
            var started = DateTimeOffset.UtcNow;
            using var process = Process.Start(start) ?? throw Error("clean-process-start");
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(25));
            try
            {
                var output = ReadAsync(process.StandardOutput.BaseStream, process, deadline.Token);
                var error = ReadAsync(process.StandardError.BaseStream, process, deadline.Token);
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error);
                Require(process.ExitCode == 0 && (await error).Length == 0, "clean-process-failed");
                S06CleanProcessProfile.RequireSummary(await output, intent.Locator.ReleaseTag, intent.Metadata.Id,
                    intent.Locator.Sha256, intent.Locator.Subject.Sha256, intent.Publication.Workflow.RunId,
                    started, DateTimeOffset.UtcNow);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("clean-process-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("clean-process-failed"); }
        finally
        {
            if (directory is not null) Cleanup(directory);
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, Process process, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > 65536)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw Error("clean-process-output");
            }
            output.Write(buffer, 0, count);
        }
    }

    private static void Cleanup(string directory)
    {
        try
        {
            var actual = Path.GetFullPath(directory);
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            Require(Path.GetDirectoryName(actual) == parent &&
                Path.GetFileName(actual).StartsWith("cp6-p10-clean-precommit-", StringComparison.Ordinal) &&
                (File.GetAttributes(actual) & FileAttributes.ReparsePoint) == 0, "clean-process-cleanup");
            // Only this process-created directory; any caches contain public certificates/evidence, never credentials.
            Directory.Delete(actual, recursive: true);
        }
        catch (Exception) { throw Error("clean-process-cleanup"); }
    }
}
```

- [ ] Update the readonly CLI confirmation envelope exactly as below.

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
                locatorSha256 = fetched.Locator.Sha256,
                intentArtifactId = args[0] == "confirm-platform-intent" ? (long?)artifactId : null,
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

- [ ] Run focused tests; require all passing.

### Task 3: Verify and commit

- [ ] Run full Release tests and formatting; require no failures/skips.
- [ ] Verify all four plan code blocks, review exact five-file diff and scan for secret/debug residue.
- [ ] Stage only these files and commit `feat(p10): require isolated read-only precommit verification`.
- [ ] Execute the real child against the protected S06 intent before accepting final publication.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06CleanPrecommitTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-clean-precommit.md tools/p10/ReleaseVerifier/S06CleanProcessProfile.cs tools/p10/ReleaseVerifier/S06CleanPrecommit.cs tools/p10/ReleaseVerifier/ReadOnlyVerificationCommand.cs tools/p10/ReleaseVerifier.Tests/S06CleanPrecommitTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): require isolated read-only precommit verification"
```

## Execution evidence

2026-09-08: all 24 new cases first failed against throwing scaffolds, then passed against the implementation. The full verifier suite passed 1,464/1,464 with zero skipped tests. Format verification identified only multi-property lines in the new summary test vector; after splitting those lines in both this plan and the test, format verification and all 24 focused cases passed. The plan/code agreement and scoped diff were reviewed. Actual protected-run child-process verification remains required; these unit vectors are not candidate acceptance.
