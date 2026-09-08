using System.Collections.ObjectModel;
using CP6.Platform.Release;
using NuGet.Packaging;
using static CP6.P10.ReleaseVerifier.PinnedNuGetTrust;

namespace CP6.P10.ReleaseVerifier;

public static class FormalNuGetVerifier
{
    public const int MaximumPackageBytes = 8 * 1024 * 1024;

    public static async Task<VerifiedNuGetPackage> VerifyAsync(string expectedPackageId,
        ReadOnlyMemory<byte> package, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Require(!string.IsNullOrEmpty(expectedPackageId) &&
            S06ReleaseIdentity.PackageHashes.ContainsKey(expectedPackageId), "nuget-package-id");
        Require(package.Length is > 0 and <= MaximumPackageBytes, "nuget-package-size");
        var bytes = package.ToArray();
        var expectedHash = S06ReleaseIdentity.PackageHashes[expectedPackageId];
        Require(Cp6DeterministicJson.Sha256Hex(bytes) == expectedHash, "nuget-package-hash");
        var policy = PinnedNuGetTrust.Load();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new PackageArchiveReader(stream);
            var proof = await NuGetPackageChecks.VerifyAsync(archive, expectedPackageId, policy,
                DateTimeOffset.UtcNow, cancellationToken);
            return new(expectedPackageId, expectedHash, policy.CurrentSigner.SpkiKeyId, proof.TimestampUtc,
                proof.CertificateChainSha256);
        }
        catch (OperationCanceledException) { throw; }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error("nuget-verification");
        }
    }
}

// A verified package is not a verified candidate. Construction is confined to this assembly.
public sealed class VerifiedNuGetPackage
{
    internal VerifiedNuGetPackage(string packageId, string sha256, string spkiKeyId,
        DateTimeOffset timestampUtc, IReadOnlyList<string> timestampChain)
    {
        PackageId = packageId;
        Sha256 = sha256;
        SpkiKeyId = spkiKeyId;
        TimestampUtc = timestampUtc;
        TimestampCertificateChainSha256 = Array.AsReadOnly(timestampChain.ToArray());
    }

    public string PackageId { get; }
    public string Version => S06ReleaseIdentity.Version;
    public string SourceGitSha => S06ReleaseIdentity.Source;
    public string Sha256 { get; }
    public string SignerFingerprint => S06ReleaseIdentity.Signer;
    public string SpkiKeyId { get; }
    public string TimestampPolicyOid => "2.16.840.1.114412.7.1";
    public DateTimeOffset TimestampUtc { get; }
    public ReadOnlyCollection<string> TimestampCertificateChainSha256 { get; }
    public bool PublicCaTrusted => false;
    public bool InternallyTrusted => true;
}
