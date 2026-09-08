using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Actual hosted validation collection. Private CRM access is confined to preparation.
// Native build/sign/scan and test steps run in the protected workflow between these phases.
internal static class S06ValidationCollector
{
    internal static async Task<GitHubWorkflowIdentity> PrepareAsync(string outputDirectory,
        string githubReadToken, string crmReadToken, string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            Console.Error.WriteLine("p10-validation-stage current-workflow");
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            var producer = current.Workflow;
            Console.Error.WriteLine("p10-validation-stage platform-source");
            var source = await SourceReferenceEvidence.CreateAsync(producer, githubReadToken, deadline.Token);
            Console.Error.WriteLine("p10-validation-stage formal-packages");
            var packages = await FormalVerificationEvidence.CollectAsync(producer, feedReadToken, deadline.Token);
            Console.Error.WriteLine("p10-validation-stage crm-consumer");
            var crm = await CrmPublicEvidence.CreateAsync(producer, crmReadToken, deadline.Token);
            Console.Error.WriteLine("p10-validation-stage publication-archive");
            var publication = await ReleaseArchiveSource.ReadAsync("publication", crmReadToken, deadline.Token);
            Console.Error.WriteLine("p10-validation-stage package-provenance");
            var provenance = await ReleaseArchiveSource.ReadAsync("package-provenance", crmReadToken, deadline.Token);
            var payloads = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                ["SourceReference"] = source,
                ["FormalPackageVerification"] = packages.CopyBytes(),
                ["CrmConsumer"] = crm,
                ["FormalPackagePublication"] = publication.CopyBytes(),
                ["PackageProvenance"] = provenance.CopyBytes(),
                ["NuGetTrustPolicy"] = PinnedNuGetTrust.Load().ValidatedDocument.CanonicalUtf8,
                ["TrustPolicy"] = VerifierTrust.Load().ValidatedDocument.CanonicalUtf8
            };
            var files = payloads.ToDictionary(p => S06ValidationInputs.PayloadPath(p.Key), p => p.Value, StringComparer.Ordinal);
            foreach (var package in packages.Packages)
                files.Add(S06ValidationInputs.PackagePath(package.Proof.PackageId), package.CopyPackageBytes());
            files.Add("producer.json", S06ArtifactAssembly.Canonical(Workflow(producer)));
            _ = S06ValidationInputs.ReadPreparation(files, producer);
            Console.Error.WriteLine("p10-validation-stage final-current-workflow");
            var final = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            Require(final.Workflow == producer && final.JobStartedAtUtc == current.JobStartedAtUtc, "validation-current-job");
            S06LocalFiles.WriteNew(outputDirectory, files);
            Console.Error.WriteLine("p10-validation-stage prepared");
            return producer;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("validation-prepare-timeout");
        }
    }

    internal static async Task<InspectedValidationArtifact> FinalizeAsync(string preparationDirectory,
        string imageDirectory, string outputDirectory, CosignBlobVerifier cosign, string githubReadToken,
        string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            var producer = current.Workflow;
            var prepared = S06ValidationInputs.ReadPreparation(
                S06LocalFiles.ReadExact(preparationDirectory, S06ValidationInputs.PreparationNames), producer);
            var image = S06ValidationInputs.ReadImage(S06LocalFiles.ReadExact(imageDirectory, S06ValidationInputs.ImageNames));
            Require(image.StartedAtUtc >= current.JobStartedAtUtc, "validation-image-time");
            var release = await FormalNuGetVerifier.VerifyAsync(S06ReleaseIdentity.ReleasePackage, prepared.ReleasePackage, deadline.Token);
            var signature = await AuthenticatedOciSignature.AuthenticateAsync(image.Digest, producer, image.SignatureBundle, cosign, deadline.Token);
            Require(signature.SignedAtUtc >= image.CompletedAtUtc, "validation-image-time");
            var manifest = await OciImageSource.ReadAsync(image.Digest, feedReadToken, deadline.Token);
            var payloads = prepared.Payloads;
            payloads.Add("ImageSbom", image.Spdx);
            payloads.Add("ImageScan", image.Sarif);
            payloads.Add("OciSignature", OciSignatureEvidence.Create(signature));
            payloads.Add("ImageProvenance", ImageProvenanceEvidence.Create(producer, manifest.CopyBytes(), image.Digest,
                manifest.Description.MediaType, release, image.BuildMetadata, image.Spdx, image.Sarif,
                image.StartedAtUtc, image.CompletedAtUtc));
            _ = await S06PayloadProof.VerifyAsync(producer, image.Digest, payloads, DateTimeOffset.UtcNow,
                cosign, githubReadToken, feedReadToken, deadline.Token);
            var final = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            Require(final.Workflow == producer && final.JobStartedAtUtc == current.JobStartedAtUtc, "validation-current-job");
            var artifact = S06ArtifactAssembly.Create(producer, image.Digest, payloads);
            S06LocalFiles.WriteNew(outputDirectory, artifact.CopyFiles());
            return artifact;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("validation-finalize-timeout");
        }
    }
}
