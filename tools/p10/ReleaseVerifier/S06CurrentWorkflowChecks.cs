using System.Globalization;
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Selected-field checks only. Environment values and parser outputs alone do not create a live context.
internal static class S06CurrentWorkflowChecks
{
    internal static IReadOnlyList<string> EnvironmentNames { get; } = Array.AsReadOnly(new[]
    {
        "GITHUB_ACTIONS", "GITHUB_REPOSITORY", "GITHUB_REPOSITORY_ID", "GITHUB_REF", "GITHUB_EVENT_NAME",
        "GITHUB_SHA", "GITHUB_WORKFLOW_REF", "GITHUB_WORKFLOW_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT",
        "GITHUB_JOB", "GITHUB_SERVER_URL", "GITHUB_API_URL", "RUNNER_ENVIRONMENT", "RUNNER_OS", "RUNNER_ARCH"
    });

    internal static string JobName(string path) => path switch
    {
        S06ReleaseIdentity.ValidationPath => S06WorkflowProfile.ValidationJobs.Single(),
        S06ReleaseIdentity.PublicationPath => S06WorkflowProfile.PublicationJobs.Single(),
        _ => throw GitHubWirePolicy.Error("s06-current-profile")
    };

    internal static S06CurrentSelection ReadEnvironment(IReadOnlyDictionary<string, string?> values, string path)
    {
        var job = JobName(path);
        string Value(string name) => values.TryGetValue(name, out var value) && value is { Length: > 0 and <= 256 } ?
            value : throw GitHubWirePolicy.Error("s06-current-environment");
        Require(Value("GITHUB_ACTIONS") == "true" && Value("GITHUB_REPOSITORY") == "GTX537/CP6" &&
            Value("GITHUB_REPOSITORY_ID") == WorkflowArtifactSelection.RepositoryId.ToString(CultureInfo.InvariantCulture) &&
            Value("GITHUB_REF") == "refs/heads/main" && Value("GITHUB_EVENT_NAME") == "workflow_dispatch" &&
            Value("GITHUB_WORKFLOW_REF") == "GTX537/CP6/" + path + "@refs/heads/main" &&
            Value("GITHUB_JOB") == job && Value("GITHUB_SERVER_URL") == "https://github.com" &&
            Value("GITHUB_API_URL") == "https://api.github.com" && Value("RUNNER_ENVIRONMENT") == "github-hosted" &&
            Value("RUNNER_OS") == "Linux" && Value("RUNNER_ARCH") == "X64", "s06-current-environment");
        var source = Value("GITHUB_SHA");
        GitHubReadTarget.RequireSha(source);
        Require(Value("GITHUB_WORKFLOW_SHA") == source, "s06-current-environment");
        var runId = Positive(Value("GITHUB_RUN_ID"));
        var attempt = Positive(Value("GITHUB_RUN_ATTEMPT"));
        Require(attempt <= int.MaxValue, "s06-current-environment");
        return new(path, job, source, runId, attempt);
    }

    internal static S06CurrentTiming ReadTiming(JsonElement run, JsonElement jobs,
        GitHubWorkflowIdentity expected, DateTimeOffset observedAtUtc, JsonElement? firstAttempt = null)
    {
        expected.RequireValid();
        var name = JobName(expected.WorkflowPath);
        RequireCutoff(observedAtUtc);
        Require(expected.Repository == "GTX537/CP6" && Number(run, "id") == expected.RunId &&
            Number(run, "run_attempt") == expected.RunAttempt && Text(run, "head_sha") == expected.CommitSha &&
            Text(run, "head_branch") == "main" && Text(run, "path") == expected.WorkflowPath &&
            Text(run, "event") == "workflow_dispatch" && Text(Property(run, "repository"), "full_name") == expected.Repository &&
            Text(Property(run, "head_repository"), "full_name") == expected.Repository, "s06-current-run");
        Require(Text(run, "status") == "in_progress" && Property(run, "conclusion").ValueKind == JsonValueKind.Null,
            "s06-current-status");
        var (started, _) = GitHubRunChronology.Read(run, expected, observedAtUtc, firstAttempt, "s06-current-time");
        var array = Property(jobs, "jobs");
        Require(Number(jobs, "total_count") == 1 && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 1,
            "s06-current-jobs");
        var job = array[0];
        Require(Number(job, "id") > 0 && Text(job, "name") == name && Number(job, "run_id") == expected.RunId &&
            Number(job, "run_attempt") == expected.RunAttempt && Text(job, "head_sha") == expected.CommitSha &&
            Text(job, "head_branch") == "main", "s06-current-job");
        Require(Text(job, "status") == "in_progress" && Property(job, "conclusion").ValueKind == JsonValueKind.Null &&
            Property(job, "completed_at").ValueKind == JsonValueKind.Null, "s06-current-status");
        var jobStarted = Time(job, "started_at");
        Require(started <= jobStarted && jobStarted <= observedAtUtc, "s06-current-time");
        return new(started, jobStarted);
    }

    private static long Positive(string value)
    {
        Require(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0 &&
            result.ToString(CultureInfo.InvariantCulture) == value, "s06-current-environment");
        return result;
    }
}

internal sealed record S06CurrentSelection(string WorkflowPath, string JobName, string CommitSha, long RunId, long RunAttempt);
internal sealed record S06CurrentTiming(DateTimeOffset RunStartedAtUtc, DateTimeOffset JobStartedAtUtc);
