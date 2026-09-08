using System.Text.Json;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Only selected safe facts are serialized. Raw private API responses, runner metadata and logs are excluded.
internal static class CrmPublicObservations
{
    // Independently read from the pinned workflow Git blob at the S05 archive commit.
    private const string WorkflowSha256 = "43864e5b88b516bf23d8395ace8f8e1646ca1efa70829e093b7bd7b833f490ef";
    private const int WorkflowByteLength = 14913;

    internal static JsonElement Delivery(CrmForwardBindingObservation observed) => JsonSerializer.SerializeToElement(new
    {
        number = observed.Selection.Number,
        mergedAtUtc = FormatTime(observed.MergedAtUtc),
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        pullRequest = Run(observed.PullRequest),
        main = Run(observed.Main)
    });

    internal static CrmForwardBindingObservation ReadDelivery(JsonElement value, int number, DateTimeOffset cutoff)
    {
        Exact(value, "number", "mergedAtUtc", "observedAtUtc", "pullRequest", "main");
        var selected = CrmPullRequestSelection.Get(number);
        Require(value.GetProperty("number").GetInt32() == number, "s06-crm-delivery");
        var merged = Time(value, "mergedAtUtc");
        var observed = Time(value, "observedAtUtc");
        var pr = ReadRun(value.GetProperty("pullRequest"), selected.PullRequestWorkflow, "pull_request", cutoff);
        var main = ReadRun(value.GetProperty("main"), selected.MainWorkflow, "push", cutoff);
        CrmForwardBindingChecks.ForwardOrder(pr.CompletedAtUtc, merged, main.StartedAtUtc, main.CompletedAtUtc, cutoff);
        Require(pr.ObservedAtUtc <= main.ObservedAtUtc && main.ObservedAtUtc <= observed && observed <= cutoff,
            "s06-crm-time");
        return new(selected, merged, pr, main, observed);
    }

    internal static JsonElement Source(GitHubSourceObservation observed) => JsonSerializer.SerializeToElement(new
    {
        repository = observed.Repository,
        sourceGitSha = observed.SourceGitSha,
        observedMainSha = observed.ObservedMainSha,
        mainProtectedAtObservation = observed.MainProtectedAtObservation,
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        relationship = "SourceReachableFromMain"
    });

    internal static GitHubSourceObservation ReadSource(JsonElement value, DateTimeOffset cutoff)
    {
        Exact(value, "repository", "sourceGitSha", "observedMainSha", "mainProtectedAtObservation", "observedAtUtc", "relationship");
        var main = Text(value, "observedMainSha");
        GitHubReadTarget.RequireSha(main);
        var protection = value.GetProperty("mainProtectedAtObservation");
        Require(Text(value, "repository") == CrmPullRequestSelection.Repository &&
            Text(value, "sourceGitSha") == PinnedReleaseDocument.CommitSha &&
            Text(value, "relationship") == "SourceReachableFromMain" &&
            protection.ValueKind is JsonValueKind.True or JsonValueKind.False, "s06-crm-source");
        var observed = Time(value, "observedAtUtc");
        Require(observed <= cutoff, "s06-crm-time");
        // CRM protection is recorded honestly; the collector does not claim public Platform's policy applies to CRM.
        return new(CrmPullRequestSelection.Repository, PinnedReleaseDocument.CommitSha, main, protection.GetBoolean(), observed);
    }

    private static JsonElement Run(GitHubWorkflowObservation observed) => JsonSerializer.SerializeToElement(new
    {
        workflow = Identity(observed.Workflow),
        @event = observed.Event,
        status = "completed",
        conclusion = "success",
        startedAtUtc = FormatTime(observed.StartedAtUtc),
        completedAtUtc = FormatTime(observed.CompletedAtUtc),
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        file = new { sha256 = observed.File.Sha256, byteLength = observed.File.ByteLength },
        jobs = observed.Jobs.OrderBy(j => j.Name, StringComparer.Ordinal).Select(j => new
        {
            id = j.Id,
            name = j.Name,
            conclusion = "success",
            startedAtUtc = FormatTime(j.StartedAtUtc),
            completedAtUtc = FormatTime(j.CompletedAtUtc)
        }).ToArray()
    });

    private static GitHubWorkflowObservation ReadRun(JsonElement value, GitHubWorkflowIdentity expected,
        string expectedEvent, DateTimeOffset cutoff)
    {
        Exact(value, "workflow", "event", "status", "conclusion", "startedAtUtc", "completedAtUtc", "observedAtUtc", "file", "jobs");
        Require(Equal(value.GetProperty("workflow"), Identity(expected)) && Text(value, "event") == expectedEvent &&
            Text(value, "status") == "completed" && Text(value, "conclusion") == "success", "s06-crm-run");
        var started = Time(value, "startedAtUtc");
        var completed = Time(value, "completedAtUtc");
        var observed = Time(value, "observedAtUtc");
        Require(started <= completed && completed <= observed && observed <= cutoff, "s06-crm-time");
        var file = value.GetProperty("file");
        Exact(file, "sha256", "byteLength");
        Require(Text(file, "sha256") == WorkflowSha256 && file.GetProperty("byteLength").GetInt32() == WorkflowByteLength,
            "s06-crm-workflow-file");
        var jobs = value.GetProperty("jobs");
        var names = CrmPullRequestSelection.RequiredJobs.Order(StringComparer.Ordinal).ToArray();
        Require(jobs.ValueKind == JsonValueKind.Array && jobs.GetArrayLength() == names.Length, "s06-crm-jobs");
        var ids = new HashSet<long>();
        var results = new List<GitHubJobObservation>();
        for (var index = 0; index < names.Length; index++)
        {
            var job = jobs[index];
            Exact(job, "id", "name", "conclusion", "startedAtUtc", "completedAtUtc");
            var id = job.GetProperty("id").GetInt64();
            Require(id > 0 && ids.Add(id) && Text(job, "name") == names[index] && Text(job, "conclusion") == "success",
                "s06-crm-jobs");
            var jobStarted = Time(job, "startedAtUtc");
            var jobCompleted = Time(job, "completedAtUtc");
            Require(started <= jobStarted && jobStarted <= jobCompleted && jobCompleted <= completed, "s06-crm-time");
            results.Add(new(id, names[index], jobStarted, jobCompleted));
        }
        return new(expected, expectedEvent, started, completed, new(WorkflowSha256, WorkflowByteLength),
            results.AsReadOnly(), observed);
    }

    private static JsonElement Identity(GitHubWorkflowIdentity workflow) => JsonSerializer.SerializeToElement(new
    {
        repository = workflow.Repository,
        workflowPath = workflow.WorkflowPath,
        workflowFileSha = workflow.WorkflowFileSha,
        runId = workflow.RunId,
        runAttempt = workflow.RunAttempt,
        commitSha = workflow.CommitSha,
        environment = "none"
    });

    private static bool Equal(JsonElement left, JsonElement right) =>
        CP6.Platform.Release.Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(left)).AsSpan().SequenceEqual(
            CP6.Platform.Release.Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(right)));
}
