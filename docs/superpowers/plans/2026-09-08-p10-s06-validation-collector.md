# P10 S06 two-phase validation collector implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Collect actual package/source/CRM evidence and finalize a validation artifact only after the real native OCI/report proof succeeds.

**Architecture:** Preparation captures the actual protected validation job and collects seven authenticated package downloads, public summaries, pinned S04 archives and compiled policies into a fresh local stage. Finalization recaptures that same producer, verifies the stage and native image inputs, performs complete real payload verification, then creates a new 24-file validation artifact.

**Tech Stack:** .NET 8.0.424, existing fixed GitHub/feed/OCI transports, native cosign, Platform Release 0.10.1 contracts, bounded local handoffs and xUnit.

---

## Scope and self-review

- Preparation uses three distinct read inputs: public GitHub, private CRM and package feed. Only its private collector reads raw CRM archives; no private token or raw private logs enter public payloads.
- Initial stage is exactly producer.json, seven public payloads and seven actual signed nupkg files. Exact current producer bytes and all seven package hashes are mandatory at finalization.
- Native image input stage is exactly five files: canonical build-input.json and raw Buildx metadata, SPDX, SARIF, OCI bundle.
- File codecs return data only, not accepted evidence. Unit selected-field vectors cannot satisfy the complete collector.
- Build/sign/scan and automated test steps execute in the protected workflow between phases. They must succeed before the finalizer runs; no success boolean is supplied to bypass them.
- Finalization uses actual NuGet verification, compiled OCI trust, real GHCR manifest and complete payload proof before generating Success gate records. It never claims the current validation workflow has already completed; the later completed-artifact reader enforces that.
- The OCI build cannot predate the current job, and signing cannot precede build completion.
- Both output directories are fresh create-new outputs. No remote write, deployment, old R2 changes, package republish or fabricated positive acceptance.

## Files

- Create: `tools/p10/ReleaseVerifier/S06ValidationInputs.cs`
- Create: `tools/p10/ReleaseVerifier/S06ValidationCollector.cs`
- Test: `tools/p10/ReleaseVerifier.Tests/S06ValidationCollectorTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-validation-collector.md`

### Task 1: Tests and RED

- [x] Add the tests and throwing data/collector scaffolds with these method signatures.
- [x] Run focused tests and confirm missing-implementation failures after compilation, no skips.

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Local selected-field vectors only. Complete collector success requires a real hosted protected S06 run.
public sealed class S06ValidationCollectorTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, new string('a', 40));

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("producer")]
    [InlineData("noncanonical-producer")]
    [InlineData("package")]
    [InlineData("empty-payload")]
    [InlineData("oversized-payload")]
    public void Preparation_stage_cannot_change_entry_set_producer_or_package_bytes(string mutation)
    {
        var files = S06ValidationInputs.PreparationNames.ToDictionary(n => n, _ => (ReadOnlyMemory<byte>)"{}"u8.ToArray());
        files["producer.json"] = S06ArtifactAssembly.Canonical(S06InToto.Workflow(Producer));
        if (mutation == "missing") files.Remove("producer.json");
        if (mutation == "extra") files.Add("extra.json", "{}"u8.ToArray());
        if (mutation == "producer") files["producer.json"] = S06ArtifactAssembly.Canonical(S06InToto.Workflow(Producer with { RunAttempt = 2 }));
        if (mutation == "noncanonical-producer") files["producer.json"] = files["producer.json"].ToArray().Append((byte)'\n').ToArray();
        if (mutation == "empty-payload") files["payloads/CrmConsumer.json"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversized-payload") files["payloads/CrmConsumer.json"] = new byte[4194305];
        var expected = mutation switch
        {
            "missing" or "extra" => "validation-stage-files",
            "producer" or "noncanonical-producer" => "validation-stage-producer",
            "empty-payload" or "oversized-payload" => "validation-stage-size",
            _ => "validation-stage-package"
        };
        Assert.Equal(expected, Assert.Throws<Cp6ReleaseContractException>(() => S06ValidationInputs.ReadPreparation(files, Producer)).Code);
    }

    [Fact]
    public void A_publication_identity_cannot_consume_a_validation_preparation() =>
        Assert.Equal("s06-attestation-producer", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06ValidationInputs.ReadPreparation(new Dictionary<string, ReadOnlyMemory<byte>>(),
                Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath })).Code);

    [Fact]
    public void Image_control_metadata_preserves_native_report_and_bundle_bytes_without_authenticating_them()
    {
        var files = ImageFiles();
        var value = S06ValidationInputs.ReadImage(files);
        Assert.Equal("sha256:" + new string('b', 64), value.Digest);
        Assert.Equal(files["spdx.json"].ToArray(), value.Spdx);
        Assert.Equal(files["sarif.json"].ToArray(), value.Sarif);
        Assert.Equal(files["oci.sigstore.json"].ToArray(), value.SignatureBundle);
        Assert.Equal(files["buildx-metadata.json"].ToArray(), value.BuildMetadata);
        value.Spdx[0] = 99;
        Assert.Equal((byte)'{', S06ValidationInputs.ReadImage(files).Spdx[0]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("unknown-field")]
    [InlineData("digest")]
    [InlineData("reversed")]
    [InlineData("future")]
    [InlineData("noncanonical")]
    [InlineData("offset-time")]
    public void Invalid_image_stage_data_fails_before_native_evidence_authentication(string mutation)
    {
        var files = ImageFiles();
        if (mutation == "missing") files.Remove("spdx.json");
        if (mutation == "extra") files.Add("extra.json", "{}"u8.ToArray());
        if (mutation == "empty") files["spdx.json"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversize") files["spdx.json"] = new byte[4194305];
        if (mutation == "noncanonical") files["build-input.json"] = files["build-input.json"].ToArray().Append((byte)'\n').ToArray();
        if (mutation is "unknown-field" or "digest" or "reversed" or "future" or "offset-time")
        {
            var root = JsonNode.Parse(files["build-input.json"].Span)!.AsObject();
            if (mutation == "unknown-field") root["extra"] = true;
            if (mutation == "digest") root["imageDigest"] = "mutable:latest";
            if (mutation == "reversed") root["buildCompletedAtUtc"] = "2026-09-01T11:00:00.000Z";
            if (mutation == "future") root["buildCompletedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
            if (mutation == "offset-time") root["buildStartedAtUtc"] = "2026-09-01T12:00:00.000+00:00";
            files["build-input.json"] = Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
        }
        Assert.Throws<Cp6ReleaseContractException>(() => S06ValidationInputs.ReadImage(files));
    }

    [Fact]
    public async Task Precancelled_preparation_cannot_read_secrets_or_create_stage_files()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06ValidationCollector.PrepareAsync("never-created", "", "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_finalization_cannot_create_a_success_gate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06ValidationCollector.FinalizeAsync(
            "never-read", "never-read", "never-created", new("missing-cosign"), "", "", cancellation.Token));
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> ImageFiles() => new()
    {
        ["build-input.json"] = S06ArtifactAssembly.Canonical(new
        {
            imageDigest = "sha256:" + new string('b', 64),
            buildStartedAtUtc = "2026-09-01T12:00:00.000Z",
            buildCompletedAtUtc = "2026-09-01T12:01:00.000Z"
        }),
        ["buildx-metadata.json"] = "{ \"native\": 1 }\n"u8.ToArray(),
        ["spdx.json"] = "{ \"native\": 2 }\n"u8.ToArray(),
        ["sarif.json"] = "{ \"native\": 3 }\n"u8.ToArray(),
        ["oci.sigstore.json"] = "{ \"native\": 4 }\n"u8.ToArray()
    };
}
```

### Task 2: Stage codecs and collector

- [x] Implement the stage codecs.

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Local stage codecs only. Neither a matching producer document nor these raw bytes authenticate evidence.
internal static class S06ValidationInputs
{
    internal static IReadOnlyList<string> InitialKinds { get; } = Array.AsReadOnly(new[]
    {
        "CrmConsumer", "FormalPackagePublication", "FormalPackageVerification", "NuGetTrustPolicy",
        "PackageProvenance", "SourceReference", "TrustPolicy"
    });

    internal static IReadOnlyList<string> PreparationNames { get; } = Array.AsReadOnly(InitialKinds.Select(PayloadPath)
        .Concat(S06ReleaseIdentity.PackageHashes.Keys.Select(PackagePath)).Append("producer.json")
        .Order(StringComparer.Ordinal).ToArray());

    internal static IReadOnlyList<string> ImageNames { get; } = Array.AsReadOnly(new[]
    {
        "build-input.json", "buildx-metadata.json", "oci.sigstore.json", "sarif.json", "spdx.json"
    });

    internal static string PayloadPath(string kind) => "payloads/" + kind + ".json";
    internal static string PackagePath(string packageId) => "packages/" + packageId + "." + S06ReleaseIdentity.Version + ".nupkg";

    internal static (Dictionary<string, ReadOnlyMemory<byte>> Payloads, byte[] ReleasePackage) ReadPreparation(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, GitHubWorkflowIdentity currentProducer)
    {
        RequireProducer(currentProducer);
        RequireFiles(files, PreparationNames);
        Require(files["producer.json"].Span.SequenceEqual(S06ArtifactAssembly.Canonical(Workflow(currentProducer))),
            "validation-stage-producer");
        foreach (var package in S06ReleaseIdentity.PackageHashes)
            Require(Cp6DeterministicJson.Sha256Hex(files[PackagePath(package.Key)].Span) == package.Value,
                "validation-stage-package");
        return (InitialKinds.ToDictionary(k => k, k => (ReadOnlyMemory<byte>)files[PayloadPath(k)].ToArray(),
            StringComparer.Ordinal), files[PackagePath(S06ReleaseIdentity.ReleasePackage)].ToArray());
    }

    internal static S06ImageInputs ReadImage(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        RequireFiles(files, ImageNames);
        var bytes = files["build-input.json"].ToArray();
        Require(bytes.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(bytes)), "non-canonical-json");
        try
        {
            var value = GitHubApiJson.Parse(bytes);
            Exact(value, "imageDigest", "buildStartedAtUtc", "buildCompletedAtUtc");
            var digest = Text(value, "imageDigest");
            OciWirePolicy.RequireDigest(digest);
            var started = Time(value, "buildStartedAtUtc");
            var completed = Time(value, "buildCompletedAtUtc");
            Require(started <= completed && completed <= DateTimeOffset.UtcNow, "validation-image-time");
            return new(digest, started, completed, files["buildx-metadata.json"].ToArray(),
                files["spdx.json"].ToArray(), files["sarif.json"].ToArray(), files["oci.sigstore.json"].ToArray());
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("validation-image-shape"); }
    }

    private static void RequireFiles(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, IReadOnlyCollection<string> names)
    {
        Require(files.Keys.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal),
            StringComparer.Ordinal), "validation-stage-files");
        Require(files.All(p => p.Value.Length > 0 && p.Value.Length <=
            (p.Key.EndsWith(".nupkg", StringComparison.Ordinal) ? FormalNuGetVerifier.MaximumPackageBytes : Cp6DeterministicJson.MaximumBytes)) &&
            files.Values.Sum(v => (long)v.Length) <= WorkflowArtifactSelection.MaximumArchiveBytes, "validation-stage-size");
    }
}

internal sealed record S06ImageInputs(string Digest, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    byte[] BuildMetadata, byte[] Spdx, byte[] Sarif, byte[] SignatureBundle);
```

- [x] Implement actual two-phase collection.

```csharp
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
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            var producer = current.Workflow;
            var source = await SourceReferenceEvidence.CreateAsync(producer, githubReadToken, deadline.Token);
            var packages = await FormalVerificationEvidence.CollectAsync(producer, feedReadToken, deadline.Token);
            var crm = await CrmPublicEvidence.CreateAsync(producer, crmReadToken, deadline.Token);
            var publication = await ReleaseArchiveSource.ReadAsync("publication", crmReadToken, deadline.Token);
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
            var final = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.ValidationPath, githubReadToken, deadline.Token);
            Require(final.Workflow == producer && final.JobStartedAtUtc == current.JobStartedAtUtc, "validation-current-job");
            S06LocalFiles.WriteNew(outputDirectory, files);
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
```

- [x] Rerun focused tests; require all passing.

### Task 3: Verification and commit

- [x] Run full Release tests and format check.
- [x] Verify the three code blocks and exact four-file diff; scan for secret/debug residue.
- [ ] Stage only these files and commit `feat(p10): collect real validation evidence before creating gates`.
- [ ] Prove positive collection in the real hosted S06 validation workflow before recording stage completion.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06ValidationCollectorTests
  if ($LASTEXITCODE -ne 0) { throw 'Focused verification failed.' }
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw 'Full verification failed.' }
  & $env:DOTNET_HOST_PATH format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
} finally {
  Remove-Item Env:P10_GITHUB_READ_TOKEN -ErrorAction SilentlyContinue
  Remove-Item Env:P10_FEED_READ_TOKEN -ErrorAction SilentlyContinue
}
```

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-validation-collector.md tools/p10/ReleaseVerifier/S06ValidationInputs.cs tools/p10/ReleaseVerifier/S06ValidationCollector.cs tools/p10/ReleaseVerifier.Tests/S06ValidationCollectorTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): collect real validation evidence before creating gates"
```

## Verification outcome (2026-09-08)

- RED: 21 expected missing-implementation failures, 0 passes/skips after compilation.
- Focused GREEN: 21 passed, 0 failed, 0 skipped.
- Full Release suite: 1,428 passed, 0 failed, 0 skipped in 47 seconds; formatting exited 0.
- All three plan code blocks match source/tests. Complete hosted collection, native runtime execution and remote acceptance remain explicitly pending.
