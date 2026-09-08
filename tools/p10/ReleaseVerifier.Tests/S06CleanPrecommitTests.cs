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
