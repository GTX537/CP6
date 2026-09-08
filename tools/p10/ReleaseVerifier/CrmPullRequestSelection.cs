namespace CP6.P10.ReleaseVerifier;

// The two reviewed S05 deliveries, not a caller-controlled PR/workflow selector.
internal sealed class CrmPullRequestSelection
{
    internal const string Repository = "GTX537/CP6.CRM";
    internal const string WorkflowPath = ".github/workflows/crm-validation.yml";
    internal const string WorkflowFileSha = "924014cb1231824a9b57ab82a6f9638f76329919";
    private CrmPullRequestSelection(int number, string headBranch, string headSha, string baseSha,
        string mergeSha, long pullRequestRunId, long mainRunId)
    {
        Number = number;
        HeadBranch = headBranch;
        BaseSha = baseSha;
        PullRequestWorkflow = new(Repository, WorkflowPath, WorkflowFileSha, pullRequestRunId, 1, headSha);
        MainWorkflow = new(Repository, WorkflowPath, WorkflowFileSha, mainRunId, 1, mergeSha);
    }

    internal int Number { get; }
    internal string HeadBranch { get; }
    internal string BaseSha { get; }
    internal GitHubWorkflowIdentity PullRequestWorkflow { get; }
    internal GitHubWorkflowIdentity MainWorkflow { get; }
    internal static IReadOnlyList<string> RequiredJobs { get; } = Array.AsReadOnly(new[]
    {
        "crm-validation", "platform-p06-sql-consumer", "platform-p10-formal-ubuntu-latest",
        "platform-p10-formal-windows-latest", "platform-p10-test-candidate"
    });

    private static readonly CrmPullRequestSelection Consumer = new(46, "codex/p10-s05-formal-package-consumer",
        "7b099200165e46f613a2689806f4a2a928425a8f", "4a340042f3596e82e7d358a39ad3106933c4395d",
        S06ReleaseIdentity.CrmSource, 34133944140, 34134695003);
    private static readonly CrmPullRequestSelection Archive = new(47, "codex/p10-s05-formal-consumer-evidence",
        "574c2d59b28141e7912573f53d9b99a6c902798e", S06ReleaseIdentity.CrmSource,
        PinnedReleaseDocument.CommitSha, 34137355910, 34138142163);

    internal static CrmPullRequestSelection Get(int number) => number switch
    {
        46 => Consumer,
        47 => Archive,
        _ => throw GitHubWirePolicy.Error("github-crm-selection")
    };
}
