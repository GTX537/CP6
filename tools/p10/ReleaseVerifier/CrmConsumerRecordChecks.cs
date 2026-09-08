using System.Globalization;
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmConsumerRecordChecks
{
    internal const string CheckoutSha = "5d307d520a3bd7d5628d65cd98500869c9e7b101";
    internal const string TreeSha = "d66ba8fd85e8d82856aa525cdf0b2560ce47b78b";

    internal static DateTimeOffset Record(string name, JsonElement value, CrmForwardBindingObservation consumer)
    {
        var (pullRequest, runner, os, lockHash) = name switch
        {
            "crm-pr-linux" => (true, "ubuntu-latest", "Ubuntu 24.04.4 LTS", LinuxLock),
            "crm-pr-windows" => (true, "windows-latest", "Microsoft Windows 10.0.26100", WindowsLock),
            "crm-main-linux" => (false, "ubuntu-latest", "Ubuntu 24.04.4 LTS", LinuxLock),
            "crm-main-windows" => (false, "windows-latest", "Microsoft Windows 10.0.26100", WindowsLock),
            _ => throw GitHubWirePolicy.Error("github-crm-record-name")
        };
        Require(consumer.Selection.Number == 46, "github-crm-selection");
        var run = pullRequest ? consumer.PullRequest : consumer.Main;
        var producer = Property(value, "producer");
        Require(Text(value, "schemaId") == "cp6.crm.p10-s05-consumer-evidence.v1" &&
            Text(producer, "repository") == CrmPullRequestSelection.Repository &&
            Text(producer, "commitSha") == (pullRequest ? CheckoutSha : S06ReleaseIdentity.CrmSource) &&
            Text(producer, "workflowPath") == run.Workflow.WorkflowPath &&
            Text(producer, "workflowFileSha") == run.Workflow.WorkflowFileSha &&
            Number(producer, "runId") == run.Workflow.RunId && Number(producer, "runAttempt") == run.Workflow.RunAttempt,
            "github-crm-record-producer");
        Require(Text(value, "invocationKind") == "GitHubActions" && Text(value, "runnerOs") == os &&
            Text(value, "dotnetSdk") == "8.0.424" && Text(value, "lockSha256") == lockHash, "github-crm-record-runtime");
        Require(Text(value, "platformSourceSha") == S06ReleaseIdentity.Source &&
            Text(value, "publicationRecordSha256") == S06ReleaseIdentity.PublicationHash &&
            Text(value, "packageVersion") == S06ReleaseIdentity.Version, "github-crm-record-package");
        var packages = Property(value, "packageSubjects");
        Require(packages.ValueKind == JsonValueKind.Array && packages.GetArrayLength() == S06ReleaseIdentity.PackageHashes.Count,
            "github-crm-record-package");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            var id = Text(package, "packageId");
            Require(seen.Add(id) && S06ReleaseIdentity.PackageHashes.TryGetValue(id, out var hash) &&
                Text(package, "version") == S06ReleaseIdentity.Version && Text(package, "sha256") == hash, "github-crm-record-package");
        }
        Require(Text(value, "trustPolicySha256") == S06ReleaseIdentity.NuGetTrustHash &&
            Text(value, "signerFingerprint") == S06ReleaseIdentity.Signer &&
            Text(value, "spkiKeyId") == "sha256:27ecc2239a1b3c2368610d3602aadc5260b44e26baffe896b9a2449662c696d6" &&
            Property(value, "publicCaTrusted").ValueKind == JsonValueKind.False &&
            Property(value, "internallyTrusted").ValueKind == JsonValueKind.True &&
            Property(value, "deployable").ValueKind == JsonValueKind.False &&
            Text(value, "timestampPolicy") == "Rfc3161Required", "github-crm-record-trust");
        Require(Text(value, "registryRestore") == "Success" && Text(value, "lockedRestore") == "Success" &&
            Number(value, "projectReferences") == 0 && Number(value, "testCount") == 16 && Number(value, "passed") == 16 &&
            Number(value, "failed") == 0 && Number(value, "skipped") == 0 && Text(value, "testConclusion") == "success",
            "github-crm-record-result");
        Require(DateTimeOffset.TryParseExact(Text(value, "createdAtUtc"), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var created), "github-proof-time");
        var jobs = run.Jobs.Where(j => j.Name == "platform-p10-formal-" + runner).ToArray();
        Require(jobs.Length == 1 && jobs[0].StartedAtUtc <= created && created <= jobs[0].CompletedAtUtc, "github-proof-time");
        return created;
    }

    internal static void Commit(JsonElement value, bool checkout)
    {
        Require(Text(value, "sha") == (checkout ? CheckoutSha : S06ReleaseIdentity.CrmSource) &&
            Text(Property(value, "tree"), "sha") == TreeSha, "github-crm-commit");
        var parents = Property(value, "parents");
        var selected = CrmPullRequestSelection.Get(46);
        Require(parents.ValueKind == JsonValueKind.Array && parents.GetArrayLength() == 2 &&
            Text(parents[0], "sha") == selected.BaseSha &&
            Text(parents[1], "sha") == selected.PullRequestWorkflow.CommitSha, "github-crm-commit");
    }

    private const string LinuxLock = "6ab560211c3bca108b1f981f35f1160d90d692fd88ee39c3ebfe3c66084fcd88";
    private const string WindowsLock = "dca0cbc0e84527934bb08e74291b0fcc067a7446cbbcb530d04ad8d8152187de";
}
