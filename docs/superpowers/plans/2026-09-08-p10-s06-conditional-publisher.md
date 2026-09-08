# P10 S06 Conditional R2 Publisher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user already selected no delegation.

**Goal:** Wire actual completed validation, public-object upload, authenticated bundle reuse and isolated read-only precommit into the fixed R2 conditional-create protocol.

**Architecture:** S06PublicationObjects prepares plain public bytes and byte comparisons; it cannot authenticate evidence or authorize publication. S06Publisher uses only the existing fixed GitHub, formal-feed, cosign and R2 implementations. Prepare verifies actual completed validation before uploading; StoreBundle revalidates the graph and reuses only an existing bundle valid for the exact Locator; Commit reads an immutable current-run intent, awaits the actual isolated verifier process, rereads the stored bundle, then conditionally creates the Locator. No overwrite, delete, injected transport or serialized success flag exists.

**Tech Stack:** .NET 8, CP6.Platform.Release [0.10.1], xUnit, GitHub Actions, Cloudflare R2.

---

## Scope and protocol review

The existing approved R2 authority and pinned keys remain unchanged. Prepare accepts only a distinct successful validation run of the exact current publisher source, completed before this publication run started. The actual immutable artifact's 11 payloads are independently verified again. The candidate is frozen once, and its exact timestamp is reused in the Locator. Upload covers exactly 11 payloads, 11 records, one gate and one candidate; all writes are If-None-Match:* with 200/412 followed by exact readback. Preparation refuses an already-used discovery tag.

Bundle publication first authenticates the local two-file handoff, fetches and verifies the real R2 graph, then reads the fixed bundle key before attempting conditional creation. A different valid ECDSA encoding may be reused; invalid existing bytes stop publication and are never repaired. The immutable intent contains the actual stored bundle.

Commit takes only tag and actual immutable intent artifact identity, not caller-provided local Locator bytes or a clean-success boolean. The new read-only child must return an exact artifact/Locator/candidate-bound precommit summary. Current publisher identity is recaptured, stored bundle bytes are compared and authenticated, and only then is a fresh 900-second R2 publishing session minted. A 412 requires identical Locator bytes and valid stored signature. A 200 is Published-Unconfirmed, not candidate acceptance; a separate read-only postcheck and completed publication workflow remain mandatory.

No remote operation is performed by the unit suite. Its positive byte vectors are deliberately unpublished; the actual protected-run success path remains pending. Signing, CLI wiring, workflow files, final audit and project closeout follow separately.

## File responsibilities

- Create tools/p10/ReleaseVerifier/S06PublicationObjects.cs: public byte preparation and exact comparison.
- Create tools/p10/ReleaseVerifier/S06Publisher.cs: fixed real publication control flow.
- Create tools/p10/ReleaseVerifier.Tests/S06PublisherTests.cs: structural vectors and pre-I/O failure/cancellation.

## Task 1: Tests first

- [ ] Add the complete tests below and throwing method scaffolds with matching signatures.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Unpublished structural vectors only. No fake transport, signing authority or positive publication capability.
public sealed class S06PublisherTests
{
    [Fact]
    public void Object_preparation_preserves_all_23_graph_objects_and_places_the_candidate_last()
    {
        var fixture = EvidenceGraphFixture.Build();
        var graph = EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
        var candidate = new AssembledPlatformCandidate(fixture.Candidate, fixture.Objects, graph);
        var objects = S06PublicationObjects.Create(candidate);
        Assert.Equal(24, objects.Count);
        Assert.Equal(24, objects.Select(o => o.Reference.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(objects, item =>
        {
            Assert.Equal(item.Reference.ByteLength, item.Bytes.Length);
            Assert.Equal(item.Reference.Sha256, Cp6DeterministicJson.Sha256Hex(item.Bytes.Span));
        });
        foreach (var item in objects.Take(23)) Assert.Equal(fixture.Objects[item.Reference.Key].ToArray(), item.Bytes.ToArray());
        Assert.Equal(fixture.Candidate, objects[^1].Bytes.ToArray());
        Assert.Equal(Cp6ReleaseMediaTypes.PlatformReleaseCandidate, objects[^1].Reference.MediaType);
        Assert.False(graph.CandidateAccepted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("extra")]
    public void Object_preparation_reinspects_bytes_instead_of_trusting_an_old_structural_result(string mutation)
    {
        var fixture = EvidenceGraphFixture.Build();
        var graph = EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
        var key = fixture.Objects.Keys.First();
        if (mutation == "missing") fixture.Objects.Remove(key);
        if (mutation == "changed") fixture.Objects[key] = "{}"u8.ToArray();
        if (mutation == "extra") fixture.Objects["objects/unused"] = "{}"u8.ToArray();
        var candidate = new AssembledPlatformCandidate(fixture.Candidate, fixture.Objects, graph);
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Create(candidate));
    }

    [Fact]
    public void Unsigned_locator_binds_exact_candidate_time_bytes_fixed_signer_and_platform_lane()
    {
        var fixture = EvidenceGraphFixture.Build();
        var bytes = S06PublicationObjects.Locator("v0.10.1-p10.1", fixture.Candidate);
        var root = GitHubApiJson.Parse(bytes);
        _ = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        Assert.Equal(bytes, Cp6DeterministicJson.Canonicalize(bytes));
        Assert.Equal(EvidenceGraphFixture.Created, S06InToto.Text(root, "createdAtUtc"));
        Assert.Equal(S06PublicationObjects.LocatorKeyId, S06InToto.Text(root, "signerKeyId"));
        Assert.Equal("PlatformReleaseCandidate", S06InToto.Text(root, "subjectKind"));
        var reference = ContentAddress.Parse(root.GetProperty("subject"));
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(fixture.Candidate), reference.Sha256);
        Assert.Equal(fixture.Candidate.Length, reference.ByteLength);
        Assert.False(root.TryGetProperty("candidateAccepted", out _));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("expired-key-window")]
    [InlineData("package")]
    [InlineData("deployable")]
    public void Locator_preparation_rejects_unpinned_or_out_of_time_candidate_bytes(string mutation)
    {
        var fixture = EvidenceGraphFixture.Build(candidateChange: root =>
        {
            if (mutation == "future") root["createdAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
            if (mutation == "expired-key-window") root["createdAtUtc"] = "2026-01-01T00:00:00.000Z";
            if (mutation == "package") root["packages"]![0]!["version"] = "0.10.2";
            if (mutation == "deployable") root["deployable"] = true;
        });
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Locator("v0.10.1-p10.1", fixture.Candidate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../bad")]
    [InlineData("v0.10.1\n")]
    public void Locator_tag_is_checked_before_document_processing(string tag) =>
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Locator(tag, ReadOnlyMemory<byte>.Empty));

    [Fact]
    public void Identical_readback_is_only_a_byte_comparison() =>
        S06PublicationObjects.RequireIdentical("{\"a\":1}"u8.ToArray(), "{\"a\":1}"u8.ToArray());

    [Theory]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("empty-expected")]
    [InlineData("oversized-expected")]
    public void Missing_or_changed_readback_never_counts_as_idempotent_success(string mutation)
    {
        var expected = mutation == "empty-expected" ? Array.Empty<byte>() :
            mutation == "oversized-expected" ? new byte[4194305] : "{\"a\":1}"u8.ToArray();
        byte[]? actual = mutation == "missing" ? null :
            mutation == "different" ? "{\"a\":2}"u8.ToArray() : expected;
        var error = Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.RequireIdentical(expected, actual));
        Assert.Equal("publication-readback-conflict", error.Code);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 2147483648, 1)]
    [InlineData(1, 1, 0)]
    public async Task Invalid_validation_selection_fails_before_any_remote_read_or_write(long run, long attempt, long artifact) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.PrepareAsync("v0.10.1-p10.1", run, attempt, artifact, "", null!, "", "", "", ""));

    [Fact]
    public async Task Invalid_commit_selection_cannot_start_a_child_or_write_a_locator() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.CommitAsync("v0.10.1-p10.1", 0, "", "", "", "", "", "", ""));

    [Fact]
    public async Task Bundle_step_rejects_missing_local_handoff_before_network() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.StoreBundleAsync("v0.10.1-p10.1", "", "", null!, "", "", "", "", "", ""));

    [Theory]
    [InlineData("prepare")]
    [InlineData("bundle")]
    [InlineData("commit")]
    public async Task Precancelled_publication_never_touches_local_or_remote_storage(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => phase switch
        {
            "prepare" => S06Publisher.PrepareAsync("", 0, 0, 0, "", null!, "", "", "", "", cancellation.Token),
            "bundle" => S06Publisher.StoreBundleAsync("", "", "", null!, "", "", "", "", "", "", cancellation.Token),
            _ => S06Publisher.CommitAsync("", 0, "", "", "", "", "", "", "", cancellation.Token)
        });
    }
}
```

- [ ] Run the focused suite and confirm failures are NotImplementedException from the new scaffolds, not compilation errors:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06PublisherTests
```

## Task 2: Implement after the red run

- [ ] Replace the public-byte scaffolds with:

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Pure public-byte preparation and comparison. None of these methods authenticates evidence or permits a write.
internal static class S06PublicationObjects
{
    internal const string LocatorKeyId = "sha256:9c0fd05b3159651cc2e9138555f32387988c6961889ee00211139e710f1febaa";

    internal static IReadOnlyList<S06PublicationObject> Create(AssembledPlatformCandidate candidate)
    {
        var bytes = candidate.CopyBytes();
        var objects = candidate.CopyObjects();
        var graph = EvidenceGraphInspection.Inspect(bytes, objects);
        var root = graph.Candidate;
        var references = root.GetProperty("evidence").EnumerateArray().Select(ContentAddress.Parse)
            .Concat(graph.Evidence.Values.Select(e => e.Payload))
            .Append(ContentAddress.Parse(root.GetProperty("releaseGateResult"))).ToArray();
        Require(references.Length == 23 && objects.Count == 23 &&
            references.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() == 23, "publication-object-set");
        var result = new List<S06PublicationObject>();
        foreach (var reference in references.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            Require(objects.TryGetValue(reference.Key, out var payload) &&
                payload.Length == reference.ByteLength &&
                Cp6DeterministicJson.Sha256Hex(payload.Span) == reference.Sha256, "publication-object-binding");
            result.Add(new(reference, payload.ToArray()));
        }
        result.Add(new(ContentAddress.Create(bytes, Cp6ReleaseMediaTypes.PlatformReleaseCandidate,
            "platform-release-candidate.v1.json"), bytes));
        return result.AsReadOnly();
    }

    internal static byte[] Locator(string releaseTag, ReadOnlyMemory<byte> candidateBytes)
    {
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        var bytes = candidateBytes.ToArray();
        _ = Cp6ReleaseValidator.ValidatePlatformCandidate(bytes);
        var root = GitHubApiJson.Parse(bytes);
        S06ReleaseIdentity.RequireCandidate(root);
        var created = Time(root, "createdAtUtc");
        var now = DateTimeOffset.UtcNow;
        Require(created <= now, "publication-locator-time");
        _ = VerifierTrust.Load().RequireKey(LocatorKeyId, "candidate-locator", 1, created, now,
            Cp6ReleaseValidationMode.Current);
        var locator = S06ArtifactAssembly.Control(Cp6ReleaseContractIds.CandidateLocator, new
        {
            releaseTag,
            subjectKind = "PlatformReleaseCandidate",
            subject = ContentAddress.Create(bytes, Cp6ReleaseMediaTypes.PlatformReleaseCandidate,
                "platform-release-candidate.v1.json").ToJson(),
            signerKeyId = LocatorKeyId,
            trustPolicyVersion = 1,
            createdAtUtc = Text(root, "createdAtUtc")
        });
        _ = Cp6ReleaseValidator.ValidateCandidateLocator(locator);
        return locator;
    }

    internal static void RequireIdentical(ReadOnlyMemory<byte> expected, byte[]? actual) =>
        Require(expected.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            actual is not null && expected.Span.SequenceEqual(actual), "publication-readback-conflict");
}

internal sealed record S06PublicationObject(ContentAddress Reference, ReadOnlyMemory<byte> Bytes);
```

- [ ] Replace the publisher scaffolds with:

```csharp
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
```

## Task 3: Verify, review and checkpoint

- [ ] Run the focused tests, full real-input verifier suite and formatter from tools/p10. Use the task's already configured .NET 8 host and actual official cosign binary, formal package readback directory, GitHub and feed read credentials. Do not print credentials or skip real signature/package tests.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06PublisherTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

Expected: all tests pass with zero skips and format exits 0. Confirm the three complete C# blocks match their files, inspect the whole scoped diff, and verify there is no secret, injected I/O success, overwrite, delete, fake workflow completion or scope drift.

- [ ] Record observed results and commit only these four files:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-conditional-publisher.md tools/p10/ReleaseVerifier/S06PublicationObjects.cs tools/p10/ReleaseVerifier/S06Publisher.cs tools/p10/ReleaseVerifier.Tests/S06PublisherTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): orchestrate conditional R2 candidate publication"
```

## Execution evidence

2026-09-08: all 26 new cases failed against throwing scaffolds with NotImplementedException, then passed against the implementation. The full suite passed 1,490/1,490, zero skipped, in 46 seconds; format verification exited 0. All three code blocks match their files; the four-file scope, object/metadata readback, conditional-write sequence and absence of secret/overwrite/delete/injected-success paths were reviewed. No S06 remote writes were performed. Actual protected-run publication and final acceptance remain pending.
