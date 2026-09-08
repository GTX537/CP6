using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// A fixed artifact selector, not a successful/completed workflow capability.
internal sealed class WorkflowArtifactSelection
{
    internal const long RepositoryId = 1214929352;
    internal const int MaximumArchiveBytes = 64 * 1024 * 1024;

    private WorkflowArtifactSelection(GitHubWorkflowIdentity workflow, long artifactId, string purpose)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && artifactId > 0, "artifact-selection");
        Workflow = workflow;
        ArtifactId = artifactId;
        Name = "p10-s06-" + purpose + "-" + workflow.CommitSha + "-" +
            workflow.RunId.ToString(CultureInfo.InvariantCulture) + "-" + workflow.RunAttempt.ToString(CultureInfo.InvariantCulture);
    }

    internal GitHubWorkflowIdentity Workflow { get; }
    internal long ArtifactId { get; }
    internal string Name { get; }

    internal static WorkflowArtifactSelection Validation(GitHubWorkflowIdentity workflow, long artifactId)
    {
        Require(workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath, "artifact-selection");
        return new(workflow, artifactId, "validation");
    }

    internal static WorkflowArtifactSelection PublicationIntent(GitHubWorkflowIdentity workflow, long artifactId)
    {
        Require(workflow.WorkflowPath == S06ReleaseIdentity.PublicationPath, "artifact-selection");
        return new(workflow, artifactId, "intent");
    }

    internal WorkflowArtifactMetadata ReadMetadata(JsonElement value, DateTimeOffset earliest, DateTimeOffset latest)
    {
        RequireCutoff(earliest);
        RequireCutoff(latest);
        Require(earliest <= latest && latest <= DateTimeOffset.UtcNow, "artifact-time");
        Require(Number(value, "id") == ArtifactId && Text(value, "name") == Name &&
            Property(value, "expired").ValueKind == JsonValueKind.False, "artifact-identity");
        var api = "https://api.github.com" + GitHubReadTarget.Artifact(ArtifactId).Path;
        Require(Text(value, "url") == api && Text(value, "archive_download_url") == api + "/zip", "artifact-identity");
        var run = Property(value, "workflow_run");
        Require(Number(run, "id") == Workflow.RunId && Text(run, "head_sha") == Workflow.CommitSha &&
            Text(run, "head_branch") == "main" && Number(run, "repository_id") == RepositoryId &&
            Number(run, "head_repository_id") == RepositoryId, "artifact-workflow");
        var digest = Text(value, "digest");
        OciWirePolicy.RequireDigest(digest);
        var length = Number(value, "size_in_bytes");
        Require(length is > 0 and <= MaximumArchiveBytes, "artifact-size");
        var created = Time(value, "created_at");
        var updated = Time(value, "updated_at");
        var expires = Time(value, "expires_at");
        Require(earliest <= created && created <= updated && updated <= latest &&
            expires > DateTimeOffset.UtcNow, "artifact-time");
        return new(ArtifactId, Name, digest, (int)length, created, expires);
    }
}

internal sealed record WorkflowArtifactMetadata(long Id, string Name, string Digest, int ByteLength,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
