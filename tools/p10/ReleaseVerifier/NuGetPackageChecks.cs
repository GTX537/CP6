using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGetHash = NuGet.Common.HashAlgorithmName;
using static CP6.P10.ReleaseVerifier.PinnedNuGetTrust;

namespace CP6.P10.ReleaseVerifier;

// Production crypto component, separated from public fixed-hash selection for direct tamper regression.
internal static class NuGetPackageChecks
{
    internal static async Task<NuGetCryptographyProof> VerifyAsync(PackageArchiveReader archive,
        string expectedId, Cp6PinnedNuGetTrustPolicy policy, DateTimeOffset evaluationUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nuspec = await archive.GetNuspecReaderAsync(cancellationToken);
        Require(nuspec.GetId() == expectedId && nuspec.GetVersion().ToNormalizedString() == S06ReleaseIdentity.Version,
            "nuget-identity");
        Require(nuspec.GetRepositoryMetadata()?.Commit == S06ReleaseIdentity.Source, "nuget-source");
        var primary = await archive.GetPrimarySignatureAsync(cancellationToken);
        if (primary is not AuthorPrimarySignature author) throw Error("nuget-author");
        Require(author.Timestamps.Count == 1, "nuget-timestamp-count");
        Require(author.SignerInfo.CounterSignerInfos.Count == 0, "nuget-countersignature");
        var fingerprint = author.GetSigningCertificateFingerprint(NuGetHash.SHA256).ToLowerInvariant();
        var timestamp = author.Timestamps[0];
        Require(timestamp.GeneralizedTime <= evaluationUtc, "nuget-timestamp-future");
        var signer = policy.RequireSigner(fingerprint, timestamp.GeneralizedTime, evaluationUtc, Cp6ReleaseValidationMode.Current);
        await RequireNuGetSignatureAsync(archive, signer.CertificateSha256, cancellationToken);
        return RequireTimestamp(author, timestamp, cancellationToken);
    }

    internal static async Task RequireNuGetSignatureAsync(PackageArchiveReader package, string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalized = fingerprint.ToUpperInvariant();
        ISignatureVerificationProvider[] providers =
        [
            new IntegrityVerificationProvider(),
            new SignatureTrustAndValidityVerificationProvider(
                [new KeyValuePair<string, NuGetHash>(normalized, NuGetHash.SHA256)]),
            new AllowListVerificationProvider(
                [new CertificateHashAllowListEntry(VerificationTarget.Author, SignaturePlacement.PrimarySignature,
                    normalized, NuGetHash.SHA256)], requireNonEmptyAllowList: true)
        ];
        var settings = new SignedPackageVerifierSettings(
            allowUnsigned: false, allowIllegal: false, allowUntrusted: false, allowIgnoreTimestamp: false,
            allowMultipleTimestamps: false, allowNoTimestamp: false, allowUnknownRevocation: false, reportUnknownRevocation: true,
            verificationTarget: VerificationTarget.Author, signaturePlacement: SignaturePlacement.PrimarySignature,
            repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
            revocationMode: NuGet.Common.RevocationMode.Online);
        var result = await new PackageSignatureVerifier(providers).VerifySignaturesAsync(package, settings, cancellationToken);
        Require(result.IsSigned && result.IsValid, "nuget-signature");
    }

    private static NuGetCryptographyProof RequireTimestamp(AuthorPrimarySignature author, Timestamp timestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = author.SignerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>().ToArray();
        Require(attributes.Length == 1 && attributes[0].Oid.Value == "1.2.840.113549.1.9.16.2.14" &&
            attributes[0].Values.Count == 1, "nuget-timestamp-attribute");
        var raw = attributes[0].Values[0].RawData;
        Require(Rfc3161TimestampToken.TryDecode(raw, out var token, out var consumed) &&
            consumed == raw.Length && token is not null, "nuget-timestamp-token");
        Require(token!.TokenInfo.PolicyId.Value == "2.16.840.1.114412.7.1" &&
            token.TokenInfo.HashAlgorithmId.Value == "2.16.840.1.101.3.4.2.1" &&
            token.TokenInfo.Timestamp == timestamp.GeneralizedTime, "nuget-timestamp-policy");
        Require(token.VerifySignatureForSignerInfo(author.SignerInfo, out var certificate) && certificate is not null,
            "nuget-timestamp-signature");
        using var chain = new X509Chain();
        chain.ChainPolicy.ExtraStore.AddRange(token.AsSignedCms().Certificates);
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.System;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.8"));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = token.TokenInfo.Timestamp.UtcDateTime;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        chain.ChainPolicy.DisableCertificateDownloads = false;
        Require(chain.Build(certificate!), "nuget-timestamp-chain");
        cancellationToken.ThrowIfCancellationRequested();
        var hashes = chain.ChainElements.Cast<X509ChainElement>()
            .Select(e => Cp6DeterministicJson.Sha256Hex(e.Certificate.RawData)).ToArray();
        NuGetTimestampPaths.Require(hashes);
        return new(timestamp.GeneralizedTime, hashes);
    }
}

internal sealed record NuGetCryptographyProof(DateTimeOffset TimestampUtc, IReadOnlyList<string> CertificateChainSha256);
