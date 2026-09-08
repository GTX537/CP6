using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Real payload proofs only. This is not proof of a completed S06 producer or a committed Locator.
// No caller-supplied trust policy, transport, clock, success flag or private CRM token is accepted.
internal sealed class S06PayloadProof
{
    private S06PayloadProof(GitHubWorkflowIdentity producer, string digest,
        AuthenticatedOciSignature signature, GitHubWorkflowObservation formalWorkflow)
    {
        Producer = producer;
        ImageDigest = digest;
        OciSignature = signature;
        FormalWorkflow = formalWorkflow;
        ObservedAtUtc = DateTimeOffset.UtcNow;
    }

    internal GitHubWorkflowIdentity Producer { get; }
    internal string ImageDigest { get; }
    internal AuthenticatedOciSignature OciSignature { get; }
    internal GitHubWorkflowObservation FormalWorkflow { get; }
    internal DateTimeOffset ObservedAtUtc { get; }

    internal static async Task<S06PayloadProof> VerifyAsync(GitHubWorkflowIdentity producer, string imageDigest,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> payloads, DateTimeOffset createdBeforeUtc,
        CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        OciWirePolicy.RequireDigest(imageDigest);
        GitHubEvidenceChecks.RequireCutoff(createdBeforeUtc);
        Require(createdBeforeUtc <= DateTimeOffset.UtcNow, "s06-proof-cutoff");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        try
        {
            Require(payloads.Count == S06EvidenceBindings.MediaTypes.Count &&
                payloads.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                    S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "s06-proof-set");
            Require(payloads.Values.All(p => p.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes),
                "s06-proof-size");
            var owned = payloads.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
            RequireHash(owned["FormalPackagePublication"], S06ReleaseIdentity.PublicationHash);
            RequireHash(owned["PackageProvenance"], S06ReleaseIdentity.ProvenanceHash);
            RequireHash(owned["TrustPolicy"], VerifierTrust.Load().ValidatedDocument.Sha256);
            var nugetTrust = PinnedNuGetTrust.Load();
            RequireHash(owned["NuGetTrustPolicy"], nugetTrust.ValidatedDocument.Sha256);
            _ = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                owned["FormalPackagePublication"], nugetTrust, DateTimeOffset.UtcNow);
            _ = Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(owned["PackageProvenance"]);

            // Reject malformed/unsigned public statements before reading either registry or feed.
            _ = SourceReferenceEvidence.Read(owned["SourceReference"], producer, createdBeforeUtc);
            _ = CrmPublicEvidence.Read(owned["CrmConsumer"], producer, createdBeforeUtc);
            _ = S06InToto.Read(owned["FormalPackageVerification"], "FormalPackageVerification", producer, createdBeforeUtc);
            _ = S06InToto.Read(owned["ImageProvenance"], "ImageProvenance", producer, createdBeforeUtc, imageDigest);
            var bundle = OciSignatureEvidence.ReadBundleForAuthentication(owned["OciSignature"],
                producer, createdBeforeUtc, imageDigest);
            var signature = await AuthenticatedOciSignature.AuthenticateAsync(
                imageDigest, producer, bundle, cosign, deadline.Token);
            _ = OciSignatureEvidence.RequireAuthenticated(owned["OciSignature"], producer,
                createdBeforeUtc, imageDigest, signature);

            // Every independent verification downloads fresh bytes from the sole formal feed.
            var packages = new List<DownloadedNuGetPackage>();
            foreach (var id in S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal))
                packages.Add(await FormalPackageSource.DownloadAndVerifyAsync(id, feedReadToken, deadline.Token));
            _ = FormalVerificationEvidence.Read(owned["FormalPackageVerification"], producer, createdBeforeUtc, packages);
            var release = packages.Single(p => p.Proof.PackageId == S06ReleaseIdentity.ReleasePackage).Proof;
            var image = await OciImageSource.ReadAsync(imageDigest, feedReadToken, deadline.Token);
            _ = ImageProvenanceEvidence.Read(owned["ImageProvenance"], producer, createdBeforeUtc,
                image.CopyBytes(), imageDigest, image.Description.MediaType, release, owned["ImageSbom"], owned["ImageScan"]);

            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6.Platform",
                S06ReleaseIdentity.Source, githubReadToken, deadline.Token);
            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6", producer.CommitSha,
                githubReadToken, deadline.Token);
            var formal = await GitHubEvidenceSource.ReadWorkflowAsync(S06WorkflowProfile.FormalPublication,
                S06WorkflowProfile.FormalJobs, createdBeforeUtc, githubReadToken, deadline.Token);
            return new(producer, imageDigest, signature, formal);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-proof-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("s06-proof-failed"); }
    }

    private static void RequireHash(ReadOnlyMemory<byte> bytes, string expected) =>
        Require(Cp6DeterministicJson.Sha256Hex(bytes.Span) == expected, "s06-proof-baseline");
}
