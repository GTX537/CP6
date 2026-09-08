using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A typed public summary of actual authenticated NuGet reads, not an acceptance capability.
// Read requires the caller's independently downloaded/verified package set; it never trusts a success flag alone.
internal static class FormalVerificationEvidence
{
    internal static async Task<CollectedFormalEvidence> CollectAsync(GitHubWorkflowIdentity producer, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var packages = new List<DownloadedNuGetPackage>();
            foreach (var id in S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal))
                packages.Add(await FormalPackageSource.DownloadAndVerifyAsync(id, feedReadToken, deadline.Token));
            var created = DateTimeOffset.UtcNow;
            var details = JsonSerializer.SerializeToElement(new
            {
                feedServiceIndex = FormalFeedPolicy.Index,
                sourceGitSha = S06ReleaseIdentity.Source,
                trustPolicySha256 = S06ReleaseIdentity.NuGetTrustHash,
                packages = packages.Select(p => Claim(p, p.RetrievedAtUtc)).ToArray()
            });
            var bytes = Create("FormalPackageVerification", producer, created, details);
            _ = Read(bytes, producer, DateTimeOffset.UtcNow, packages);
            return new(bytes, packages);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-packages-timeout");
        }
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, IReadOnlyCollection<DownloadedNuGetPackage> independentlyVerifiedPackages)
    {
        var statement = S06InToto.Read(bytes, "FormalPackageVerification", producer, cutoff);
        try
        {
            var actual = Select(independentlyVerifiedPackages);
            var details = statement.Details;
            Exact(details, "feedServiceIndex", "sourceGitSha", "trustPolicySha256", "packages");
            Require(Text(details, "feedServiceIndex") == FormalFeedPolicy.Index &&
                Text(details, "sourceGitSha") == S06ReleaseIdentity.Source &&
                Text(details, "trustPolicySha256") == S06ReleaseIdentity.NuGetTrustHash, "s06-packages-identity");
            var claims = details.GetProperty("packages");
            Require(claims.ValueKind == JsonValueKind.Array && claims.GetArrayLength() == actual.Length, "s06-packages-set");
            for (var index = 0; index < actual.Length; index++)
            {
                var retrieved = Time(claims[index], "retrievedAtUtc");
                Require(retrieved >= actual[index].Proof.TimestampUtc && retrieved <= statement.CreatedAtUtc,
                    "s06-packages-time");
                // Retrieval time is a historical signed observation, not part of the immutable NuGet proof.
                // A clean consumer's current download will have a later retrieval time.
                Require(Canonical(claims[index]).AsSpan().SequenceEqual(Canonical(Claim(actual[index], retrieved))),
                    "s06-packages-proof");
            }
            return statement;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-packages-shape"); }
    }

    private static DownloadedNuGetPackage[] Select(IReadOnlyCollection<DownloadedNuGetPackage> packages)
    {
        Require(packages is not null && packages.Count == 7 && packages.All(p => p is not null), "s06-packages-set");
        var selected = packages!.OrderBy(p => p.Proof.PackageId, StringComparer.Ordinal).ToArray();
        Require(selected.Select(p => p.Proof.PackageId).SequenceEqual(
            S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal), "s06-packages-set");
        foreach (var package in selected)
            Require(package.Proof.Sha256 == S06ReleaseIdentity.PackageHashes[package.Proof.PackageId] &&
                package.FeedServiceIndex == FormalFeedPolicy.Index, "s06-packages-proof");
        return selected;
    }

    private static JsonElement Claim(DownloadedNuGetPackage package, DateTimeOffset retrievedAtUtc)
    {
        var proof = package.Proof;
        return JsonSerializer.SerializeToElement(new
        {
            packageId = proof.PackageId,
            version = proof.Version,
            sourceGitSha = proof.SourceGitSha,
            authorSignedPackageSha256 = proof.Sha256,
            publishedPackageSha256 = proof.Sha256,
            feedTransformation = "BytePreserving",
            signerFingerprint = proof.SignerFingerprint,
            spkiKeyId = proof.SpkiKeyId,
            publicCaTrusted = proof.PublicCaTrusted,
            internallyTrusted = proof.InternallyTrusted,
            timestampPolicyOid = proof.TimestampPolicyOid,
            timestampUtc = FormatTime(proof.TimestampUtc),
            timestampCertificateChainSha256 = proof.TimestampCertificateChainSha256,
            retrievedAtUtc = FormatTime(retrievedAtUtc)
        });
    }

    private static byte[] Canonical(JsonElement value) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(value));
}

internal sealed class CollectedFormalEvidence
{
    private readonly byte[] _bytes;
    internal CollectedFormalEvidence(byte[] bytes, IEnumerable<DownloadedNuGetPackage> packages)
    {
        _bytes = bytes.ToArray();
        Packages = Array.AsReadOnly(packages.ToArray());
    }

    internal IReadOnlyList<DownloadedNuGetPackage> Packages { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
