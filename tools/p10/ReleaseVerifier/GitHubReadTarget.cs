using System.Globalization;

namespace CP6.P10.ReleaseVerifier;

// No caller-selected URL, HTTP method, response link, pagination URL or repository owner.
internal sealed class GitHubReadTarget
{
    private GitHubReadTarget(string path) => Path = path;
    internal string Path { get; }

    internal static GitHubReadTarget Run(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt));
    internal static GitHubReadTarget Jobs(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt) + "/jobs?per_page=100&page=1");
    internal static GitHubReadTarget MainBranch(string repository) => new(RepositoryPath(repository) + "/branches/main");
    internal static GitHubReadTarget Compare(string repository, string source, string observedMain)
    {
        RequireSha(source);
        RequireSha(observedMain);
        return new(RepositoryPath(repository) + "/compare/" + source + "..." + observedMain + "?per_page=1&page=2");
    }

    internal static GitHubReadTarget Workflow(string repository, string path, string source)
    {
        RequireSha(source);
        var allowed = repository switch
        {
            "GTX537/CP6" => path is S06ReleaseIdentity.ValidationPath or S06ReleaseIdentity.PublicationPath,
            "GTX537/CP6.Platform" => path == ".github/workflows/p10-formal-packages.yml",
            "GTX537/CP6.CRM" => path == ".github/workflows/crm-validation.yml",
            _ => false
        };
        if (!allowed) throw GitHubWirePolicy.Error("github-workflow");
        return new(RepositoryPath(repository) + "/contents/" + path + "?ref=" + source);
    }

    internal static GitHubReadTarget Archive(PinnedReleaseDocument document) =>
        new(RepositoryPath(PinnedReleaseDocument.Repository) + "/contents/" + document.Path + "?ref=" + PinnedReleaseDocument.CommitSha);

    internal static GitHubReadTarget CrmPullRequest(CrmPullRequestSelection selected) =>
        new(RepositoryPath(CrmPullRequestSelection.Repository) + "/pulls/" + selected.Number.ToString(CultureInfo.InvariantCulture));

    internal static GitHubReadTarget CrmConsumerCommit(bool checkout) =>
        new(RepositoryPath(CrmPullRequestSelection.Repository) + "/git/commits/" +
            (checkout ? CrmConsumerRecordChecks.CheckoutSha : S06ReleaseIdentity.CrmSource));

    internal static GitHubReadTarget Artifact(long artifactId)
    {
        if (artifactId <= 0) throw GitHubWirePolicy.Error("artifact-selection");
        return new("/repos/GTX537/CP6/actions/artifacts/" + artifactId.ToString(CultureInfo.InvariantCulture));
    }

    internal static GitHubReadTarget ArtifactZip(long artifactId) => new(Artifact(artifactId).Path + "/zip");

    internal static void RequireSha(string value)
    {
        if (value is null || value.Length != 40 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw GitHubWirePolicy.Error("github-sha");
    }

    private static string RunPath(string repository, long runId, long attempt)
    {
        if (runId <= 0 || attempt <= 0 || attempt > int.MaxValue) throw GitHubWirePolicy.Error("github-run");
        return RepositoryPath(repository) + "/actions/runs/" + runId.ToString(CultureInfo.InvariantCulture) +
            "/attempts/" + attempt.ToString(CultureInfo.InvariantCulture);
    }

    private static string RepositoryPath(string repository)
    {
        if (repository is not ("GTX537/CP6" or "GTX537/CP6.Platform" or "GTX537/CP6.CRM"))
            throw GitHubWirePolicy.Error("github-repository");
        return "/repos/" + repository;
    }
}
