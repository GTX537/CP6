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
