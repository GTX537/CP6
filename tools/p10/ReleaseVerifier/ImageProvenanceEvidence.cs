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
