using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Public Platform source observations only; no private CRM token or raw GitHub response is embedded.
internal static class SourceReferenceEvidence
{
    internal static async Task<byte[]> CreateAsync(GitHubWorkflowIdentity producer, string githubReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var observed = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6.Platform",
            S06ReleaseIdentity.Source, githubReadToken, cancellationToken);
        var details = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            repository = observed.Repository,
            sourceGitSha = observed.SourceGitSha,
            observedMainSha = observed.ObservedMainSha,
            mainProtectedAtObservation = observed.MainProtectedAtObservation,
            observedAtUtc = FormatTime(observed.ObservedAtUtc),
            relationship = "SourceReachableFromMain"
        });
        var bytes = Create("SourceReference", producer, observed.ObservedAtUtc, details);
        _ = Read(bytes, producer, DateTimeOffset.UtcNow);
        return bytes;
    }

    internal static GitHubSourceObservation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff)
    {
        var statement = S06InToto.Read(bytes, "SourceReference", producer, cutoff);
        try
        {
            var details = statement.Details;
            Exact(details, "repository", "sourceGitSha", "observedMainSha", "mainProtectedAtObservation", "observedAtUtc", "relationship");
            var main = Text(details, "observedMainSha");
            GitHubReadTarget.RequireSha(main);
            var observed = Time(details, "observedAtUtc");
            Require(Text(details, "repository") == "GTX537/CP6.Platform" &&
                Text(details, "sourceGitSha") == S06ReleaseIdentity.Source &&
                details.GetProperty("mainProtectedAtObservation").ValueKind == System.Text.Json.JsonValueKind.True &&
                Text(details, "relationship") == "SourceReachableFromMain" &&
                observed == statement.CreatedAtUtc, "s06-source-claims");
            return new("GTX537/CP6.Platform", S06ReleaseIdentity.Source, main, true, observed);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-source-shape"); }
    }
}
