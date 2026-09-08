using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.ImageReportProfile;

namespace CP6.P10.ReleaseVerifier;

// Parses native third-party bytes, not a claim that syft ran or that the image is acceptable.
internal static class SpdxImageReport
{
    internal static SpdxImageObservation Read(ReadOnlyMemory<byte> bytes, string imageDigest)
    {
        OciWirePolicy.RequireDigest(imageDigest);
        if (bytes.Length is < 1 or > 4 * 1024 * 1024) throw Error("image-report-size");
        var owned = bytes.ToArray();
        var root = ImageReportProfile.Read(owned);
        try
        {
            Require(Text(root, "spdxVersion") == "SPDX-2.3" && Text(root, "dataLicense") == "CC0-1.0" &&
                Text(root, "SPDXID") == "SPDXRef-DOCUMENT" && Text(root, "name") == S06ReleaseIdentity.ImageRepository,
                "spdx-document-binding");
            var space = Text(root, "documentNamespace");
            Require(Uri.TryCreate(space, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host == "anchore.com" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                uri.AbsolutePath.StartsWith("/syft/image/", StringComparison.Ordinal), "spdx-document-binding");
            var creation = root.GetProperty("creationInfo");
            Require(creation.GetProperty("creators").EnumerateArray().Select(v => v.GetString()).SequenceEqual(
                new[] { "Organization: Anchore, Inc", "Tool: syft-" + SyftVersion }, StringComparer.Ordinal), "spdx-tool");
            Require(DateTimeOffset.TryParseExact(Text(creation, "created"), "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var created) && created >= DateTimeOffset.UnixEpoch, "spdx-time");
            var packages = root.GetProperty("packages").EnumerateArray().ToArray();
            Require(packages.Length >= 2 && packages.Select(p => Text(p, "SPDXID"))
                .Distinct(StringComparer.Ordinal).Count() == packages.Length &&
                packages.All(p => Text(p, "SPDXID").StartsWith("SPDXRef-", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(Text(p, "name"))), "spdx-package-binding");
            var describes = root.GetProperty("relationships").EnumerateArray()
                .Where(r => Text(r, "relationshipType") == "DESCRIBES").ToArray();
            Require(describes.Length == 1 && Text(describes[0], "spdxElementId") == "SPDXRef-DOCUMENT", "spdx-image-binding");
            var roots = packages.Where(p => Text(p, "SPDXID") == Text(describes[0], "relatedSpdxElement")).ToArray();
            Require(roots.Length == 1, "spdx-image-binding");
            var image = roots[0];
            Require(Text(image, "name") == S06ReleaseIdentity.ImageRepository &&
                Text(image, "versionInfo") == imageDigest && Text(image, "primaryPackagePurpose") == "CONTAINER",
                "spdx-image-binding");
            var hashes = image.GetProperty("checksums").EnumerateArray().ToArray();
            Require(hashes.Length == 1 && Text(hashes[0], "algorithm") == "SHA256" &&
                Text(hashes[0], "checksumValue") == imageDigest[7..], "spdx-image-binding");
            var release = packages.Where(p => Text(p, "name") == S06ReleaseIdentity.ReleasePackage).ToArray();
            Require(release.Length > 0 && release.All(p => Text(p, "versionInfo") == S06ReleaseIdentity.Version),
                "spdx-release-package");
            return new(Cp6DeterministicJson.Sha256Hex(owned), owned.Length, created, packages.Length - 1);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("spdx-shape"); }
    }
}

internal sealed record SpdxImageObservation(string Sha256, int ByteLength, DateTimeOffset CreatedAtUtc, int PackageCount);
