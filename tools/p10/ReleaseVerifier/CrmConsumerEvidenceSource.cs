using System.Collections.Frozen;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmConsumerEvidenceSource
{
    internal static async Task<CrmConsumerEvidenceObservation> ReadAsync(DateTimeOffset completedBeforeUtc, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var documents = new Dictionary<string, DownloadedReleaseDocument>(StringComparer.Ordinal);
        foreach (var name in new[] { "crm-index", "crm-pr-linux", "crm-pr-windows", "crm-main-linux", "crm-main-windows" })
            documents.Add(name, await ReleaseArchiveSource.ReadAsync(name, token, deadline.Token));
        var consumer = await CrmForwardBindingSource.ReadAsync(46, completedBeforeUtc, token, deadline.Token);
        var archive = await CrmForwardBindingSource.ReadAsync(47, completedBeforeUtc, token, deadline.Token);
        CrmConsumerIndexChecks.Index(GitHubApiJson.Parse(documents["crm-index"].CopyBytes()), consumer, archive);
        foreach (var document in documents.Where(d => d.Key != "crm-index"))
            _ = CrmConsumerRecordChecks.Record(document.Key, GitHubApiJson.Parse(document.Value.CopyBytes()), consumer);
        using var client = new GitHubReadClient(token);
        foreach (var checkout in new[] { false, true })
            CrmConsumerRecordChecks.Commit(await client.ReadAsync(GitHubReadTarget.CrmConsumerCommit(checkout), deadline.Token), checkout);
        var reachability = await GitHubEvidenceSource.ReadSourceAsync(
            CrmPullRequestSelection.Repository, PinnedReleaseDocument.CommitSha, token, deadline.Token);
        return new(consumer, archive, reachability, documents.ToFrozenDictionary(StringComparer.Ordinal), DateTimeOffset.UtcNow);
    }
}

internal sealed record CrmConsumerEvidenceObservation(CrmForwardBindingObservation Consumer, CrmForwardBindingObservation Archive,
    GitHubSourceObservation ArchiveSource, IReadOnlyDictionary<string, DownloadedReleaseDocument> Documents, DateTimeOffset ObservedAtUtc);
