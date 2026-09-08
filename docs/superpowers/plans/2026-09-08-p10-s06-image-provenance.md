# P10 S06 Runtime Image and Build Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The owner already selected sequential execution in the current task; do not dispatch agents or ask again.

**Goal:** Define the single non-deployable verifier image build profile and its typed public ImageProvenance statement, binding native Buildx metadata, the actual signed Release package, and raw image reports.

**Architecture:** The Dockerfile packages precompiled output and the pinned official cosign into a fixed linux/amd64 runtime base. The codec cross-checks Buildx's manifest/config/descriptor against independently hashed manifest bytes; the decoder recomputes public claims against independent manifest/package/report inputs. Neither unsigned statements nor parser unit vectors confer candidate acceptance: the final consumer must authenticate the Locator, full graph and completed protected producer.

**Tech Stack:** .NET 8.0.424, xUnit, CP6.Platform.Release 0.10.1, fixed .NET 8.0.30 runtime, cosign 3.1.3, Syft 1.51.1 and Trivy 0.74.0; no new dependencies.

---

## Approved boundary and evidence

- Parent: Platform P10 release-governance design and pinned-self-signed-trust amendment. This is S06's ImageProvenance component only. Existing R2, deployment, trust stores, package hashes and published 0.10.1 artifacts remain unchanged.
- MCR read-only inspection on 2026-09-08 independently hashed the runtime index, selected linux/amd64 manifest and config. Selected child manifest is `sha256:0f8aaa92bdf4ea871aa0fc4c5903ca81fcae3af26d8fd2d6e918fdd226df3338`; config is `sha256:8ff1af58e78c657cdc58da7395e300b59144a5871ac62c38d8e2cfc791b5d99c`, with runtime 8.0.30, UID 1654 and invariant globalization. This is metadata verification, not a vulnerability scan.
- [Docker Buildx metadata documentation](https://docs.docker.com/reference/cli/docker/buildx/build/#metadata-file) defines `containerimage.digest`, `containerimage.config.digest`, and `containerimage.descriptor`. Only these selected outputs are checked; native metadata is hashed unchanged but private paths/build refs/warnings are not copied into public evidence.
- Metadata hash/length in a decoded public statement are signed-producer claims, not an assertion that the consumer independently downloaded the original metadata. Manifest, package and report bindings are independently recomputed. Execution proof comes from the fixed reviewed workflow plus completed run/jobs, not the codec alone.
- SPDX uses second precision; its timestamp must be at least the build completion second and no later than statement creation. All CP6 times remain exact UTC milliseconds.
- Dockerfile is pinned by its actual LF byte hash and embedded in the verifier; checkout normalization cannot silently alter the recipe. CLI publication compiles once outside the image; image packaging contains no SDK, shell commands, credentials or compilation.
- The tests below use deliberately unsigned OCI/report format vectors, never formal acceptance evidence. Only the package proof is real, verified from the actual unchanged formal feed-readback archive. No local Docker build/pull/scan or remote writes are performed by this module.

## Files and responsibilities

- Create `eng/p10/verifier.Dockerfile`: fixed runtime-only nonroot recipe.
- Modify `.gitattributes`: preserve this recipe's LF bytes.
- Modify `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj`: embed the recipe.
- Create `tools/p10/ReleaseVerifier/ImageBuildProfile.cs`: fixed source profile and bounded byte-checked resource reader.
- Create `tools/p10/ReleaseVerifier/ImageProvenanceEvidence.cs`: typed creation/decoding and independent cross-bindings.
- Create `tools/p10/ReleaseVerifier.Tests/ImageProvenanceEvidenceTests.cs`: actual package verification plus isolated native-format and tamper tests.

## Task 1 — Failure-first coverage

- [x] Write the complete tests below. Add only throwing method scaffolds (matching the signatures in Task 2) so failure is attributable to missing behavior, not compiler errors.
- [x] Run Release tests filtered to ImageProvenanceEvidenceTests and save a TRX; require every new case to fail from NotImplementedException with zero skips.

### tools/p10/ReleaseVerifier.Tests/ImageProvenanceEvidenceTests.cs

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Buildx/OCI/report examples are unsigned format vectors, not real image/build/scan evidence.
// Release proof is obtained by the actual verifier from the unchanged formal feed-readback package.
public sealed class ImageProvenanceEvidenceTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static readonly Lazy<Task<VerifiedNuGetPackage>> Release = new(async () =>
        await FormalNuGetVerifier.VerifyAsync(S06ReleaseIdentity.ReleasePackage,
            await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("P10_FORMAL_PACKAGE_ROOT") ??
                throw new InvalidOperationException("Actual formal feed-readback packages are required."),
                "CP6.Platform.Release.0.10.1.nupkg"))));
    private const string Config = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddMinutes(-3);
    private static readonly DateTimeOffset End = Start.AddMinutes(1);

    [Fact]
    public void Embedded_Dockerfile_is_byte_pinned_runtime_only_and_nonroot()
    {
        var bytes = ImageBuildProfile.DockerfileBytes();
        Assert.Equal("b2f21cd090f7ecc8d933f48858b6f4584fcc12fe502db4304798714ba1c22474",
            Cp6DeterministicJson.Sha256Hex(bytes));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("FROM " + ImageBuildProfile.BaseImage + "\n", text);
        Assert.Contains("USER 1654:1654\n", text);
        Assert.Contains("--chown=0:0 --chmod=0555 cosign /opt/cp6/cosign", text);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("RUN ", text);
        Assert.DoesNotContain("dotnet/sdk", text);
        bytes[0] = 0;
        Assert.Equal((byte)'F', ImageBuildProfile.DockerfileBytes()[0]);
    }

    [Fact]
    public async Task Roundtrip_binds_real_Release_proof_manifest_and_native_reports_without_rewriting_them()
    {
        var vectors = Vectors();
        var bytes = await Create(vectors);
        var result = Read(bytes, vectors, await Release.Value);
        Assert.Equal("ImageProvenance", result.Kind);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(2, JsonNode.Parse(bytes)!["subject"]!.AsArray().Count);
        var details = result.Details;
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(vectors.Metadata), details.GetProperty("buildMetadataSha256").GetString());
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(vectors.Sarif), details.GetProperty("scan").GetProperty("sha256").GetString());
        Assert.False(Encoding.UTF8.GetString(bytes).Contains("unit-build-ref", StringComparison.Ordinal));
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6DeterministicJson.Canonicalize(vectors.Sarif));
    }

    [Theory]
    [InlineData("image")]
    [InlineData("config")]
    [InlineData("descriptor-digest")]
    [InlineData("descriptor-media")]
    [InlineData("descriptor-size")]
    [InlineData("missing-descriptor")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("oversize")]
    public async Task Native_Buildx_metadata_must_match_the_independently_hashed_manifest(string mutation)
    {
        var vectors = Vectors();
        await Create(vectors);
        var metadata = JsonNode.Parse(vectors.Metadata)!.AsObject();
        if (mutation == "image") metadata["containerimage.digest"] = Config;
        if (mutation == "config") metadata["containerimage.config.digest"] = vectors.Digest;
        if (mutation == "descriptor-digest") metadata["containerimage.descriptor"]!["digest"] = Config;
        if (mutation == "descriptor-media") metadata["containerimage.descriptor"]!["mediaType"] = "application/vnd.oci.image.index.v1+json";
        if (mutation == "descriptor-size") metadata["containerimage.descriptor"]!["size"] = vectors.Manifest.Length + 1;
        if (mutation == "missing-descriptor") metadata.Remove("containerimage.descriptor");
        vectors = vectors with
        {
            Metadata = mutation switch
            {
                "empty" => Array.Empty<byte>(),
                "oversize" => new byte[4194305],
                "duplicate" => Encoding.UTF8.GetBytes("{\"duplicate\":1,\"duplicate\":2," + metadata.ToJsonString()[1..]),
                _ => Bytes(metadata)
            }
        };
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Create(vectors));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("base")]
    [InlineData("dockerfile")]
    [InlineData("runtime")]
    [InlineData("sdk")]
    [InlineData("platform")]
    [InlineData("cosign")]
    [InlineData("release")]
    [InlineData("metadata-hash")]
    [InlineData("metadata-size")]
    [InlineData("manifest-size")]
    [InlineData("config")]
    [InlineData("sbom-hash")]
    [InlineData("sbom-count")]
    [InlineData("scan-hash")]
    [InlineData("scan-count")]
    [InlineData("extra")]
    [InlineData("start-after-end")]
    [InlineData("build-after-sbom")]
    [InlineData("created-before-build")]
    public async Task Selected_public_claims_are_exact_and_recomputed_against_independent_inputs(string mutation)
    {
        var vectors = Vectors();
        var node = JsonNode.Parse(await Create(vectors))!;
        var detail = node["predicate"]!["details"]!;
        if (mutation == "profile") detail["buildProfile"]!["buildKind"] = "unknown";
        if (mutation == "base") detail["buildProfile"]!["baseImage"] = "mcr.microsoft.com/dotnet/runtime:latest";
        if (mutation == "dockerfile") detail["buildProfile"]!["dockerfileSha256"] = new string('d', 64);
        if (mutation == "runtime") detail["buildProfile"]!["runtimeVersion"] = "8.0.1";
        if (mutation == "sdk") detail["buildProfile"]!["sdkVersion"] = "10.0.100";
        if (mutation == "platform") detail["buildProfile"]!["platform"] = "linux/arm64";
        if (mutation == "cosign") detail["buildProfile"]!["cosignSha256"] = new string('d', 64);
        if (mutation == "release") detail["releasePackage"]!["sha256"] = new string('d', 64);
        if (mutation == "metadata-hash") detail["buildMetadataSha256"] = "not-a-hash";
        if (mutation == "metadata-size") detail["buildMetadataByteLength"] = 0;
        if (mutation == "manifest-size") detail["manifest"]!["byteLength"] = vectors.Manifest.Length + 1;
        if (mutation == "config") detail["manifest"]!["configDigest"] = vectors.Digest;
        if (mutation == "sbom-hash") detail["sbom"]!["sha256"] = new string('d', 64);
        if (mutation == "sbom-count") detail["sbom"]!["packageCount"] = 0;
        if (mutation == "scan-hash") detail["scan"]!["sha256"] = new string('d', 64);
        if (mutation == "scan-count") detail["scan"]!["findingCount"] = 0;
        if (mutation == "extra") detail["unreviewed"] = true;
        if (mutation == "start-after-end") detail["buildStartedAtUtc"] = S06InToto.FormatTime(End.AddSeconds(1));
        if (mutation == "build-after-sbom") detail["buildCompletedAtUtc"] = S06InToto.FormatTime(End.AddSeconds(2));
        if (mutation == "created-before-build") node["predicate"]!["createdAtUtc"] = S06InToto.FormatTime(Start);
        var package = await Release.Value;
        Assert.Throws<Cp6ReleaseContractException>(() => Read(Canonical(node), vectors, package));
    }

    [Theory]
    [InlineData("manifest-bytes")]
    [InlineData("sbom-digest")]
    [InlineData("scan-digest")]
    [InlineData("other-package")]
    [InlineData("reversed-time")]
    [InlineData("offset-time")]
    [InlineData("future-time")]
    [InlineData("sbom-before-build")]
    public async Task Collector_rejects_wrong_inputs_and_impossible_chronology(string mutation)
    {
        var vectors = Vectors();
        await Create(vectors);
        var package = await Release.Value;
        var start = Start;
        var end = End;
        if (mutation == "manifest-bytes") vectors = vectors with { Manifest = "{}"u8.ToArray() };
        if (mutation == "sbom-digest")
            vectors = vectors with { Spdx = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(vectors.Spdx).Replace(vectors.Digest, Config, StringComparison.Ordinal)) };
        if (mutation == "scan-digest")
            vectors = vectors with { Sarif = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(vectors.Sarif).Replace(vectors.Digest, Config, StringComparison.Ordinal)) };
        if (mutation == "other-package")
            package = await FormalNuGetVerifier.VerifyAsync("CP6.Platform.Contracts",
                await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("P10_FORMAL_PACKAGE_ROOT")!,
                    "CP6.Platform.Contracts.0.10.1.nupkg")));
        if (mutation == "reversed-time") start = End.AddSeconds(1);
        if (mutation == "offset-time") start = start.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "future-time") end = DateTimeOffset.UtcNow.AddHours(1);
        if (mutation == "sbom-before-build") end = End.AddSeconds(2);
        Assert.Throws<Cp6ReleaseContractException>(() => ImageProvenanceEvidence.Create(Producer, vectors.Manifest,
            vectors.Digest, OciWirePolicy.OciManifest, package, vectors.Metadata, vectors.Spdx, vectors.Sarif, start, end));
    }

    private static async Task<byte[]> Create(Inputs vectors) => ImageProvenanceEvidence.Create(Producer,
        vectors.Manifest, vectors.Digest, OciWirePolicy.OciManifest, await Release.Value, vectors.Metadata,
        vectors.Spdx, vectors.Sarif, Start, End);

    private static S06Attestation Read(byte[] bytes, Inputs vectors, VerifiedNuGetPackage package) =>
        ImageProvenanceEvidence.Read(bytes, Producer, DateTimeOffset.UtcNow, vectors.Manifest, vectors.Digest,
            OciWirePolicy.OciManifest, package, vectors.Spdx, vectors.Sarif);

    private static byte[] Bytes(JsonNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true });
    private static byte[] Canonical(JsonNode node) => Cp6DeterministicJson.Canonicalize(Bytes(node));

    private static Inputs Vectors()
    {
        var manifest = Encoding.UTF8.GetBytes($$$$"""
            {"schemaVersion":2,"mediaType":"application/vnd.oci.image.manifest.v1+json",
             "config":{"mediaType":"application/vnd.oci.image.config.v1+json","digest":"{{{{Config}}}}","size":123},
             "layers":[{"mediaType":"application/vnd.oci.image.layer.v1.tar+gzip",
               "digest":"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","size":234}]}
            """);
        var digest = "sha256:" + Cp6DeterministicJson.Sha256Hex(manifest);
        var metadata = Encoding.UTF8.GetBytes($$$$"""
            {"buildx.build.ref":"unit-build-ref","containerimage.digest":"{{{{digest}}}}",
             "containerimage.config.digest":"{{{{Config}}}}","containerimage.descriptor":{
               "digest":"{{{{digest}}}}","mediaType":"application/vnd.oci.image.manifest.v1+json","size":{{{{manifest.Length}}}}
             }}
            """);
        var sbomTime = End.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var repository = S06ReleaseIdentity.ImageRepository;
        var spdx = Encoding.UTF8.GetBytes($$$$"""
            {"spdxVersion":"SPDX-2.3","dataLicense":"CC0-1.0","SPDXID":"SPDXRef-DOCUMENT","name":"{{{{repository}}}}",
             "documentNamespace":"https://anchore.com/syft/image/unit-vector",
             "creationInfo":{"creators":["Organization: Anchore, Inc","Tool: syft-1.51.1"],"created":"{{{{sbomTime}}}}"},
             "packages":[{"name":"CP6.Platform.Release","SPDXID":"SPDXRef-Release","versionInfo":"0.10.1"},
               {"name":"{{{{repository}}}}","SPDXID":"SPDXRef-Image","versionInfo":"{{{{digest}}}}",
                "primaryPackagePurpose":"CONTAINER","checksums":[{"algorithm":"SHA256","checksumValue":"{{{{digest[7..]}}}}"}]}],
             "relationships":[{"spdxElementId":"SPDXRef-DOCUMENT","relatedSpdxElement":"SPDXRef-Image","relationshipType":"DESCRIBES"}]}
            """);
        var sarif = Encoding.UTF8.GetBytes($$$$"""
            {"version":"2.1.0","$schema":"https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
             "runs":[{"tool":{"driver":{"name":"Trivy","fullName":"Trivy Vulnerability Scanner","informationUri":"https://github.com/aquasecurity/trivy",
               "version":"0.74.0","rules":[{"id":"CVE-UNIT-1","name":"LanguageSpecificPackageVulnerability",
               "properties":{"tags":["vulnerability","security","MEDIUM"],"cvssv3_baseScore":5.7},"defaultConfiguration":{"level":"warning"}}]}},
             "properties":{"imageName":"{{{{repository}}}}@{{{{digest}}}}","imageID":"{{{{Config}}}}","repoDigests":["{{{{repository}}}}@{{{{digest}}}}"]},
             "results":[{"ruleIndex":0,"ruleId":"CVE-UNIT-1","level":"warning","message":{"text":"Unit vector only."}}]}]}
            """);
        return new(manifest, digest, metadata, spdx, sarif);
    }

    private sealed record Inputs(byte[] Manifest, string Digest, byte[] Metadata, byte[] Spdx, byte[] Sarif);
}
```

## Task 2 — Minimal implementation

- [x] Add the exact Dockerfile and scoped checkout/resource configuration below.
- [x] Replace throwing scaffolds with the two complete source files below.

### eng/p10/verifier.Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime@sha256:0f8aaa92bdf4ea871aa0fc4c5903ca81fcae3af26d8fd2d6e918fdd226df3338
ARG CP6_SOURCE_SHA
LABEL org.opencontainers.image.source="https://github.com/GTX537/CP6" \
      org.opencontainers.image.revision="${CP6_SOURCE_SHA}" \
      org.opencontainers.image.version="0.10.1" \
      dev.cp6.p10.deployable="false"
WORKDIR /app
COPY --chown=0:0 --chmod=0555 publish/ /app/
COPY --chown=0:0 --chmod=0555 cosign /opt/cp6/cosign
ENV P10_COSIGN_PATH=/opt/cp6/cosign
USER 1654:1654
ENTRYPOINT ["/usr/bin/dotnet", "/app/CP6.P10.ReleaseVerifier.dll"]
CMD []
```

### .gitattributes addition after the existing main-sync SQL rule

```gitattributes
# P10 verifier recipe is embedded and verified by its exact LF byte hash.
eng/p10/verifier.Dockerfile text eol=lf
```

### tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj addition in the resource ItemGroup

```xml
    <EmbeddedResource Include="../../../eng/p10/verifier.Dockerfile"
                      LogicalName="CP6.P10.verifier.Dockerfile" />
```

### tools/p10/ReleaseVerifier/ImageBuildProfile.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Fixed non-deployable S06 runtime image. Builds happen on hosted CI, never on the developer's PC.
internal static class ImageBuildProfile
{
    internal const string BaseImage = "mcr.microsoft.com/dotnet/runtime@sha256:0f8aaa92bdf4ea871aa0fc4c5903ca81fcae3af26d8fd2d6e918fdd226df3338";
    internal const string DockerfileSha256 = "b2f21cd090f7ecc8d933f48858b6f4584fcc12fe502db4304798714ba1c22474";
    internal const string RuntimeVersion = "8.0.30";
    internal const string SdkVersion = "8.0.424";
    internal const string Platform = "linux/amd64";
    internal const string CosignVersion = "3.1.3";
    internal const string CosignSha256 = "4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71";

    internal static byte[] DockerfileBytes()
    {
        using var stream = typeof(ImageBuildProfile).Assembly.GetManifestResourceStream("CP6.P10.verifier.Dockerfile") ??
            throw S06InToto.Error("image-build-profile");
        var bytes = new byte[2049];
        var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        S06InToto.Require(length <= 2048 &&
            Cp6DeterministicJson.Sha256Hex(bytes.AsSpan(0, length)) == DockerfileSha256, "image-build-profile");
        return bytes[..length];
    }
}
```

### tools/p10/ReleaseVerifier/ImageProvenanceEvidence.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Selected, signed-producer claims; not SLSA certification or proof that Docker/Syft/Trivy executed.
// Consumer must authenticate the Locator, graph and completed protected producer before accepting this statement.
internal static class ImageProvenanceEvidence
{
    internal static byte[] Create(GitHubWorkflowIdentity producer, ReadOnlyMemory<byte> manifest,
        string imageDigest, string mediaType, VerifiedNuGetPackage release, ReadOnlyMemory<byte> buildxMetadata,
        ReadOnlyMemory<byte> spdx, ReadOnlyMemory<byte> sarif, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
    {
        RequireProducer(producer);
        Require(startedAtUtc.Offset == TimeSpan.Zero && completedAtUtc.Offset == TimeSpan.Zero,
            "image-build-time");
        var image = OciManifestChecks.Read(manifest, imageDigest, mediaType);
        Require(buildxMetadata.Length is > 0 and <= 4 * 1024 * 1024, "image-build-metadata");
        var metadata = ImageReportProfile.Read(buildxMetadata);
        try
        {
            var descriptor = metadata.GetProperty("containerimage.descriptor");
            Require(Text(metadata, "containerimage.digest") == imageDigest &&
                Text(metadata, "containerimage.config.digest") == image.Config.Digest &&
                Text(descriptor, "digest") == imageDigest && Text(descriptor, "mediaType") == image.MediaType &&
                descriptor.GetProperty("size").GetInt64() == manifest.Length, "image-build-metadata");
            var created = DateTimeOffset.UtcNow;
            var details = Details(manifest, imageDigest, mediaType, release, spdx, sarif, startedAtUtc, completedAtUtc,
                created, Cp6DeterministicJson.Sha256Hex(buildxMetadata.Span), buildxMetadata.Length);
            var bytes = S06InToto.Create("ImageProvenance", producer, created, details, imageDigest);
            _ = Read(bytes, producer, DateTimeOffset.UtcNow, manifest, imageDigest, mediaType, release, spdx, sarif);
            return bytes;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("image-build-metadata"); }
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, ReadOnlyMemory<byte> independentlyReadManifest, string imageDigest, string mediaType,
        VerifiedNuGetPackage independentlyVerifiedRelease, ReadOnlyMemory<byte> spdx, ReadOnlyMemory<byte> sarif)
    {
        var statement = S06InToto.Read(bytes, "ImageProvenance", producer, cutoff, imageDigest);
        try
        {
            var actual = statement.Details;
            var expected = Details(independentlyReadManifest, imageDigest, mediaType, independentlyVerifiedRelease, spdx, sarif,
                Time(actual, "buildStartedAtUtc"), Time(actual, "buildCompletedAtUtc"), statement.CreatedAtUtc,
                Text(actual, "buildMetadataSha256"), actual.GetProperty("buildMetadataByteLength").GetInt32());
            Require(Canonical(actual).AsSpan().SequenceEqual(Canonical(expected)), "image-build-claims");
            return statement;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("image-build-claims"); }
    }

    private static JsonElement Details(ReadOnlyMemory<byte> manifest, string digest, string mediaType,
        VerifiedNuGetPackage release, ReadOnlyMemory<byte> spdxBytes, ReadOnlyMemory<byte> sarifBytes,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset created, string metadataSha256, int metadataByteLength)
    {
        _ = ImageBuildProfile.DockerfileBytes();
        var image = OciManifestChecks.Read(manifest, digest, mediaType);
        var spdx = SpdxImageReport.Read(spdxBytes, digest);
        var scan = SarifImageReport.Read(sarifBytes, digest, image.Config.Digest);
        Require(release.PackageId == S06ReleaseIdentity.ReleasePackage &&
            release.Sha256 == S06ReleaseIdentity.PackageHashes[S06ReleaseIdentity.ReleasePackage], "image-build-package");
        Require(start >= release.TimestampUtc && start <= end && end <= created &&
            spdx.CreatedAtUtc >= DateTimeOffset.FromUnixTimeSeconds(end.ToUnixTimeSeconds()) &&
            spdx.CreatedAtUtc <= created, "image-build-time");
        Require(metadataByteLength is > 0 and <= 4 * 1024 * 1024 && metadataSha256.Length == 64 &&
            metadataSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "image-build-metadata");
        return JsonSerializer.SerializeToElement(new
        {
            buildProfile = new
            {
                buildKind = "HostedRuntimeOnly",
                baseImage = ImageBuildProfile.BaseImage,
                dockerfileSha256 = ImageBuildProfile.DockerfileSha256,
                runtimeVersion = ImageBuildProfile.RuntimeVersion,
                sdkVersion = ImageBuildProfile.SdkVersion,
                platform = ImageBuildProfile.Platform,
                cosignVersion = ImageBuildProfile.CosignVersion,
                cosignSha256 = ImageBuildProfile.CosignSha256
            },
            buildStartedAtUtc = FormatTime(start),
            buildCompletedAtUtc = FormatTime(end),
            buildMetadataSha256 = metadataSha256,
            buildMetadataByteLength = metadataByteLength,
            manifest = new { digest, mediaType = image.MediaType, byteLength = manifest.Length, configDigest = image.Config.Digest },
            releasePackage = new
            {
                packageId = release.PackageId, version = release.Version, sha256 = release.Sha256, sourceGitSha = release.SourceGitSha
            },
            sbom = new
            {
                tool = "syft", version = ImageReportProfile.SyftVersion, sha256 = spdx.Sha256, byteLength = spdx.ByteLength,
                createdAtUtc = FormatTime(spdx.CreatedAtUtc), packageCount = spdx.PackageCount
            },
            scan = new
            {
                tool = "trivy", version = ImageReportProfile.TrivyVersion, sha256 = scan.Sha256, byteLength = scan.ByteLength,
                findingCount = scan.FindingCount, unknownCount = scan.UnknownCount, lowCount = scan.LowCount,
                mediumCount = scan.MediumCount, highCount = 0, criticalCount = 0
            }
        });
    }

    private static byte[] Canonical(JsonElement value) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(value));
}
```

## Task 3 — Verification and scoped checkpoint

- [x] Run focused Release tests; all new cases must pass without skipped tests.
- [x] Run the complete Release verifier suite with actual cosign, formal archive root, GitHub and feed read credentials supplied only as process environment variables; clean up tokens in finally.
- [x] Run formatting verification, exact plan/source parity, Dockerfile SHA-256 and git diff hygiene.
- [ ] Save the reviewed seven-file scope as an auditable local commit.

From `tools/p10` with .NET 8.0.424, actual cosign path, actual formal readback directory, and authorized read tokens already supplied in the process environment:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ImageProvenanceEvidenceTests --logger "trx;LogFileName=s06-image-provenance-red.trx" --results-directory ../../artifacts/p10/image-provenance-red
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ImageProvenanceEvidenceTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

From the task worktree after review:

```powershell
git diff --check
git add -- .gitattributes eng/p10/verifier.Dockerfile tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj tools/p10/ReleaseVerifier/ImageBuildProfile.cs tools/p10/ReleaseVerifier/ImageProvenanceEvidence.cs tools/p10/ReleaseVerifier.Tests/ImageProvenanceEvidenceTests.cs docs/superpowers/plans/2026-09-08-p10-s06-image-provenance.md
git diff --cached --check
git commit -m "feat(p10): bind verifier image build provenance"
```

## Self-review and remaining P10 acceptance

All component requirements map to Tasks 1–3. No new dependency, trust exception, scanner exclusion, fake acceptance or deployment authority is introduced. The build metadata is not mislabelled as SLSA-certified provenance. Config/layer payloads are not declared independently downloaded. The actual protected Linux build/scanner execution, graph assembly, completed-validation artifact binding, publication/pre-post verification, conditional Locator commit and cross-repository S06 audit are outside this codec checkpoint; P10 remains incomplete until they finish.

## Execution record — 2026-09-08

- Initial test compilation exposed C# raw-string delimiter ambiguity in the test vectors; corrected only the delimiters before recording any RED evidence.
- Proper RED: all 39 cases failed from the throwing implementation, independently counted from the TRX; zero unexpected failures or skips.
- GREEN: 39/39 passed using actual formal package cryptography; full Release suite passed 1,114/1,114 with zero skips. This full run preceded only a test indentation correction, not a behavioral change.
- Initial formatting verification flagged only the new test switch-expression indentation. Corrected that block in tests and plan, then formatting passed and the focused 39/39 passed again.
- Exact plan/source parity detected four documentation-only raw-string delimiter mismatches caused by text substitution; corrected all four. The four complete code/recipe blocks now match source, the Dockerfile byte hash matches the pin, and scoped hygiene review found no credential, local-machine path, temporary stub or unrelated behavior change.
- No image was built, pulled, scanned or published. No protected workflow ran and no R2 write occurred. This records component verification only, not S06 completion.
