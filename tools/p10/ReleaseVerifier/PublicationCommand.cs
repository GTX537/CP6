using System.Globalization;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Secret-bearing publisher CLI. It never signs, accepts a verification boolean, or claims candidate acceptance.
internal static class PublicationCommand
{
    internal static IReadOnlyList<string> RequiredVariables { get; } = Array.AsReadOnly(new[]
    {
        "P10_COSIGN_PATH", "P10_GITHUB_READ_TOKEN", "P10_FEED_READ_TOKEN",
        "P10_R2_PUBLISH_ACCESS_KEY_ID", "P10_R2_PUBLISH_SECRET_ACCESS_KEY"
    });

    internal static bool Matches(string[] arguments) => arguments is ["prepare-publication", _, _, _, _, _] or
        ["store-publication-bundle", _, _, _] or ["commit-publication", _, _];

    internal static async Task<byte[]> ExecuteAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        Require(Matches(args), "publication-command");
        var tag = args[1];
        _ = R2ObjectTarget.Discovery(tag, R2DiscoveryPart.Locator);
        long run = 0, attempt = 0, artifact = 0;
        if (args[0] == "prepare-publication")
        {
            run = Positive(args[2], long.MaxValue);
            attempt = Positive(args[3], int.MaxValue);
            artifact = Positive(args[4], long.MaxValue);
        }
        if (args[0] == "commit-publication") artifact = Positive(args[2], long.MaxValue);
        var forbidden = ReadOnlyVerificationCommand.ForbiddenVariables.Where(n =>
            n is not ("P10_R2_PUBLISH_ACCESS_KEY_ID" or "P10_R2_PUBLISH_SECRET_ACCESS_KEY"));
        Require(forbidden.All(n => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))), "publication-secret-scope");
        var values = RequiredVariables.ToDictionary(n => n, Required, StringComparer.Ordinal);
        var cosignPath = values["P10_COSIGN_PATH"];
        var github = values["P10_GITHUB_READ_TOKEN"];
        var feed = values["P10_FEED_READ_TOKEN"];
        var publishId = values["P10_R2_PUBLISH_ACCESS_KEY_ID"];
        var publishSecret = values["P10_R2_PUBLISH_SECRET_ACCESS_KEY"];
        string state;
        string? conditionalCreate = null;
        if (args[0] == "prepare-publication")
        {
            await S06Publisher.PrepareAsync(tag, run, attempt, artifact, args[5],
                new CosignBlobVerifier(cosignPath), github, feed, publishId, publishSecret, cancellationToken);
            state = "PreparedForLocatorSigning";
        }
        else
        {
            var readId = Required("P10_R2_CONSUMER_ACCESS_KEY_ID");
            var readSecret = Required("P10_R2_CONSUMER_SECRET_ACCESS_KEY");
            if (args[0] == "store-publication-bundle")
            {
                await S06Publisher.StoreBundleAsync(tag, args[2], args[3], new CosignBlobVerifier(cosignPath),
                    github, feed, readId, readSecret, publishId, publishSecret, cancellationToken);
                state = "StoredBundleForIntent";
            }
            else
            {
                conditionalCreate = (await S06Publisher.CommitAsync(tag, artifact, cosignPath, github, feed,
                    readId, readSecret, publishId, publishSecret, cancellationToken)).ToString();
                state = "PublishedUnconfirmed";
            }
        }
        return S06ArtifactAssembly.Canonical(new
        {
            state,
            releaseTag = tag,
            conditionalCreate,
            candidateAccepted = false,
            deployable = false,
            publicationWorkflowCompleted = false
        });
    }

    private static long Positive(string text, long maximum)
    {
        Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value > 0 && value <= maximum && value.ToString(CultureInfo.InvariantCulture) == text, "publication-selection");
        return value;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl), "publication-credential");
        return value!;
    }
}
