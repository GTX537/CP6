using System.Globalization;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// Pure selected-field checks. The live source binds these inputs to fixed HTTPS requests.
internal static class GitHubEvidenceChecks
{
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw GitHubWirePolicy.Error(code);
    }

    internal static JsonElement Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
            throw GitHubWirePolicy.Error("github-proof-shape");
        return property;
    }

    internal static string Text(JsonElement value, string name)
    {
        var property = Property(value, name);
        if (property.ValueKind != JsonValueKind.String) throw GitHubWirePolicy.Error("github-proof-shape");
        return property.GetString()!;
    }

    internal static long Number(JsonElement value, string name)
    {
        var property = Property(value, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var result) || result < 0)
            throw GitHubWirePolicy.Error("github-proof-shape");
        return result;
    }

    internal static DateTimeOffset Time(JsonElement value, string name)
    {
        if (!DateTimeOffset.TryParseExact(Text(value, name), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) || result < DateTimeOffset.UnixEpoch)
            throw GitHubWirePolicy.Error("github-proof-time");
        return result;
    }

    internal static GitHubMainObservation MainBranch(JsonElement main)
    {
        Require(Text(main, "name") == "main", "github-source-branch");
        var sha = Text(Property(main, "commit"), "sha");
        GitHubReadTarget.RequireSha(sha);
        var protection = Property(main, "protected");
        Require(protection.ValueKind is JsonValueKind.True or JsonValueKind.False, "github-source-protection");
        return new(sha, protection.GetBoolean());
    }

    internal static void Reachability(string repository, string source, GitHubMainObservation main, JsonElement comparison)
    {
        _ = GitHubReadTarget.Compare(repository, source, main.Sha);
        if (repository is "GTX537/CP6" or "GTX537/CP6.Platform")
            Require(main.Protected, "github-source-protection");
        var status = Text(comparison, "status");
        var ahead = Number(comparison, "ahead_by");
        Require(Text(Property(comparison, "base_commit"), "sha") == source &&
            Text(Property(comparison, "merge_base_commit"), "sha") == source &&
            Number(comparison, "behind_by") == 0 && Number(comparison, "total_commits") == ahead &&
            (source == main.Sha ? status == "identical" && ahead == 0 : status == "ahead" && ahead > 0),
            "github-source-reachability");
    }

    internal static void RequireCutoff(DateTimeOffset cutoff)
    {
        Require(cutoff.Offset == TimeSpan.Zero && cutoff >= DateTimeOffset.UnixEpoch, "github-proof-time");
    }
}

internal sealed record GitHubMainObservation(string Sha, bool Protected);
internal sealed record GitHubSourceObservation(string Repository, string SourceGitSha, string ObservedMainSha,
    bool MainProtectedAtObservation, DateTimeOffset ObservedAtUtc);
internal sealed record GitHubWorkflowIdentity(string Repository, string WorkflowPath, string WorkflowFileSha,
    long RunId, long RunAttempt, string CommitSha)
{
    internal void RequireValid()
    {
        _ = GitHubReadTarget.Run(Repository, RunId, RunAttempt);
        _ = GitHubReadTarget.Workflow(Repository, WorkflowPath, CommitSha);
        GitHubReadTarget.RequireSha(WorkflowFileSha);
    }
}

internal sealed record GitHubWorkflowFileObservation(string Sha256, int ByteLength);
internal sealed record GitHubJobObservation(long Id, string Name, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc);
internal sealed record GitHubWorkflowObservation(GitHubWorkflowIdentity Workflow, string Event,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, GitHubWorkflowFileObservation File,
    IReadOnlyList<GitHubJobObservation> Jobs, DateTimeOffset ObservedAtUtc);
