using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Actual fixed-source publication orchestration. No injected transport, synthetic success or overwrite/delete path.
internal static class S06Publisher
{
    internal static async Task PrepareAsync(string releaseTag, long validationRunId, long validationAttempt, long artifactId,
        string newStageDirectory, CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        string publishAccessKeyId, string publishSecret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(validationRunId > 0 && validationAttempt is > 0 and <= int.MaxValue && artifactId > 0,
            "publication-selection");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        try
        {
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath,
                githubReadToken, deadline.Token);
            using var github = new GitHubReadClient(githubReadToken);
            var contents = await github.ReadAsync(GitHubReadTarget.Workflow("GTX537/CP6",
                S06ReleaseIdentity.ValidationPath, current.Workflow.CommitSha), deadline.Token);
            var producer = new GitHubWorkflowIdentity("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
                GitHubEvidenceChecks.Text(contents, "sha"), validationRunId, validationAttempt, current.Workflow.CommitSha);
            _ = GitHubWorkflowChecks.File(contents, producer);
            var completed = await S06CompletedValidation.ReadAsync(producer, artifactId, current.RunStartedAtUtc,
                cosign, githubReadToken, feedReadToken, deadline.Token);
            var candidate = S06CandidateAssembly.Create(completed.Artifact, current.Workflow);
            var objects = S06PublicationObjects.Create(candidate);
            var locator = S06PublicationObjects.Locator(releaseTag, candidate.CopyBytes());
            var fresh = await RequireCurrentAsync(current, githubReadToken, deadline.Token);
            S06EvidenceChronology.RequirePublication(candidate.Inspection.Candidate, completed.Workflow,
                fresh.Workflow, fresh.RunStartedAtUtc, fresh.ObservedAtUtc);
            // Mint the bounded R2 session only after all expensive real validation has completed.
            using var publisher = R2ObjectClient.Publisher(publishAccessKeyId, publishSecret);
            Require(await publisher.ReadAsync(R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator), deadline.Token) is null &&
                await publisher.ReadAsync(R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Bundle), deadline.Token) is null,
                "publication-tag-used");
            foreach (var item in objects)
            {
                var target = R2ObjectTarget.Addressed(item.Reference);
                _ = await publisher.CreateAsync(target, item.Bytes, deadline.Token);
                // Both 200 and 412 require byte-identical readback with target-bound metadata.
                S06PublicationObjects.RequireIdentical(item.Bytes, await publisher.ReadAsync(target, deadline.Token));
            }
            S06LocalFiles.WriteNew(newStageDirectory, new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                [S06PublicationIntent.LocatorName] = locator
            });
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("publication-prepare-timeout");
        }
    }

    internal static async Task StoreBundleAsync(string releaseTag, string signedStageDirectory, string newIntentDirectory,
        CosignBlobVerifier cosign, string githubReadToken, string feedReadToken, string readAccessKeyId, string readSecret,
        string publishAccessKeyId, string publishSecret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        try
        {
            var files = S06LocalFiles.ReadExact(signedStageDirectory,
                new[] { S06PublicationIntent.LocatorName, S06PublicationIntent.BundleName });
            var locator = files[S06PublicationIntent.LocatorName];
            var bundle = files[S06PublicationIntent.BundleName];
            var fetched = await PlatformCandidateSource.IntendedAsync(releaseTag, locator, bundle,
                readAccessKeyId, readSecret, cosign, deadline.Token);
            _ = await S06PublicationConfirmation.VerifyAsync(fetched, cosign, githubReadToken, feedReadToken, deadline.Token);
            using var publisher = R2ObjectClient.Publisher(publishAccessKeyId, publishSecret);
            var target = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Bundle);
            var stored = await publisher.ReadAsync(target, deadline.Token);
            if (stored is null)
            {
                _ = await publisher.CreateAsync(target, bundle, deadline.Token);
                stored = await publisher.ReadAsync(target, deadline.Token) ?? throw Error("publication-bundle-missing");
            }
            // ECDSA signatures may differ. Reuse existing bytes only when valid for this exact intended Locator.
            _ = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locator, stored, cosign, deadline.Token);
            S06LocalFiles.WriteNew(newIntentDirectory, new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                [S06PublicationIntent.LocatorName] = locator,
                [S06PublicationIntent.BundleName] = stored
            });
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("publication-bundle-timeout");
        }
    }

    internal static async Task<R2CreateStatus> CommitAsync(string releaseTag, long intentArtifactId, string cosignPath,
        string githubReadToken, string feedReadToken, string readAccessKeyId, string readSecret,
        string publishAccessKeyId, string publishSecret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(intentArtifactId > 0, "publication-selection");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(35));
        try
        {
            var cosign = new CosignBlobVerifier(cosignPath);
            var intent = await S06PublicationIntent.ReadAsync(releaseTag, intentArtifactId, cosign, githubReadToken, deadline.Token);
            // This awaited real new process is the sole precommit check; no CLI flag or artifact can replace it.
            await S06CleanPrecommit.VerifyAsync(intent, readAccessKeyId, readSecret, cosignPath,
                githubReadToken, feedReadToken, deadline.Token);
            _ = await RequireCurrentAsync(intent.Publication, githubReadToken, deadline.Token);
            using var reader = R2ObjectClient.Consumer(readAccessKeyId, readSecret);
            var locator = intent.CopyLocatorBytes();
            var bundleTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Bundle);
            var bundle = await reader.ReadAsync(bundleTarget, deadline.Token);
            S06PublicationObjects.RequireIdentical(intent.CopyBundleBytes(), bundle);
            _ = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locator, bundle!, cosign, deadline.Token);
            using var publisher = R2ObjectClient.Publisher(publishAccessKeyId, publishSecret);
            var locatorTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
            var result = await publisher.CreateAsync(locatorTarget, locator, deadline.Token);
            if (result == R2CreateStatus.AlreadyExists)
            {
                S06PublicationObjects.RequireIdentical(locator, await reader.ReadAsync(locatorTarget, deadline.Token));
                var existingBundle = await reader.ReadAsync(bundleTarget, deadline.Token) ?? throw Error("publication-bundle-missing");
                _ = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locator, existingBundle, cosign, deadline.Token);
            }
            // A successful PUT is Published-Unconfirmed. A separate read-only postcheck and completed run are still required.
            return result;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("publication-commit-timeout");
        }
    }

    private static async Task<S06CurrentWorkflow> RequireCurrentAsync(S06CurrentWorkflow expected, string githubReadToken,
        CancellationToken cancellationToken)
    {
        var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, githubReadToken, cancellationToken);
        Require(current.Workflow == expected.Workflow && current.JobStartedAtUtc == expected.JobStartedAtUtc &&
            current.RunStartedAtUtc == expected.RunStartedAtUtc, "publication-current-run");
        return current;
    }
}
