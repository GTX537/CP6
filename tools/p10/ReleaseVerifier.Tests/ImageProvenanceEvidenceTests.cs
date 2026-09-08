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
