using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using NuGet.Packaging;

namespace CP6.P10.ReleaseVerifier.Tests;

// Positive cases use the seven actual S04 GitHub Packages readback archives.
// Mutations are in memory, never uploaded, and never count as release evidence.
public sealed class FormalNuGetVerifierTests
{
    private const string ReleaseId = "CP6.Platform.Release";
    private const string Fingerprint = "1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d";

    [Theory]
    [InlineData("CP6.Platform.Abstractions")]
    [InlineData("CP6.Platform.AspNetCore")]
    [InlineData("CP6.Platform.Contracts")]
    [InlineData("CP6.Platform.Deployment")]
    [InlineData("CP6.Platform.EntityFramework")]
    [InlineData("CP6.Platform.Messaging")]
    [InlineData("CP6.Platform.Release")]
    public async Task Actual_formal_readback_bytes_pass_author_integrity_and_real_RFC3161_verification(string id)
    {
        var bytes = PackageBytes(id);
        var result = await FormalNuGetVerifier.VerifyAsync(id, bytes);
        using var publication = JsonDocument.Parse(EvidenceGraphBaseline.Publication());
        var package = publication.RootElement.GetProperty("packages").EnumerateArray()
            .Single(p => p.GetProperty("packageId").GetString() == id);
        Assert.Equal(package.GetProperty("publishedPackageSha256").GetString(), result.Sha256);
        Assert.Equal(id, result.PackageId);
        Assert.Equal("0.10.1", result.Version);
        Assert.Equal("3ff27e26962dcfd722887afb80a4306010dd9ee1", result.SourceGitSha);
        Assert.Equal(Fingerprint, result.SignerFingerprint);
        Assert.Equal("sha256:27ecc2239a1b3c2368610d3602aadc5260b44e26baffe896b9a2449662c696d6", result.SpkiKeyId);
        Assert.Equal("2.16.840.1.114412.7.1", result.TimestampPolicyOid);
        Assert.Equal(package.GetProperty("timestampCertificateChainSha256").EnumerateArray().Select(e => e.GetString()),
            result.TimestampCertificateChainSha256);
        Assert.InRange(result.TimestampUtc, DateTimeOffset.Parse("2026-09-07T13:17:00Z"), DateTimeOffset.Parse("2026-09-07T13:23:00Z"));
        Assert.False(result.PublicCaTrusted);
        Assert.True(result.InternallyTrusted);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.TimestampCertificateChainSha256)[0] = "replacement");
    }

    [Fact]
    public void Compiled_NuGet_policy_and_certificate_match_independent_bootstrap_pins()
    {
        var policy = PinnedNuGetTrust.Load();
        Assert.Equal("da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f", policy.ValidatedDocument.Sha256);
        Assert.Equal(Fingerprint, policy.CurrentSigner.CertificateSha256);
        Assert.Equal(policy.ValidatedDocument.Sha256,
            PinnedNuGetTrust.Parse(EvidenceGraphBaseline.NuGetTrust(), CertificateBytes()).ValidatedDocument.Sha256);
        Assert.False(policy.PublicCaTrusted);
        Assert.True(policy.InternallyTrusted);
    }

    [Theory]
    [InlineData("whitespace")]
    [InlineData("policy-version")]
    [InlineData("claim")]
    public void Replacing_the_policy_cannot_select_a_new_trust_root(string mutation)
    {
        var raw = EvidenceGraphBaseline.NuGetTrust();
        var node = JsonNode.Parse(raw)!.AsObject();
        if (mutation == "policy-version") node["policyVersion"] = 2;
        if (mutation == "claim") node["publicCaTrusted"] = true;
        var bytes = mutation == "whitespace" ? raw.Concat(new byte[] { 10 }).ToArray() : EvidenceGraphFixture.Canonical(node);
        Assert.Equal("nuget-trust-hash", Assert.Throws<Cp6ReleaseContractException>(() =>
            PinnedNuGetTrust.Parse(bytes, CertificateBytes())).Code);
    }

    [Fact]
    public void Replacing_the_certificate_cannot_change_the_pinned_author()
    {
        var certificate = CertificateBytes();
        certificate[^1] ^= 1;
        Assert.Equal("nuget-certificate-hash", Assert.Throws<Cp6ReleaseContractException>(() =>
            PinnedNuGetTrust.Parse(EvidenceGraphBaseline.NuGetTrust(), certificate)).Code);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 4194305)]
    [InlineData(false, 0)]
    [InlineData(false, 65537)]
    public void Bootstrap_resources_are_bounded_before_parsing(bool policy, int count) =>
        Assert.Equal("nuget-trust-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            PinnedNuGetTrust.Parse(policy ? new byte[count] : EvidenceGraphBaseline.NuGetTrust(),
                policy ? CertificateBytes() : new byte[count])).Code);

    [Theory]
    [InlineData("")]
    [InlineData("cp6.platform.release")]
    [InlineData("Unapproved.Package")]
    public async Task Public_entry_cannot_verify_an_unselected_package(string id) =>
        await Reject(() => FormalNuGetVerifier.VerifyAsync(id, PackageBytes(ReleaseId)), "nuget-package-id");

    [Theory]
    [InlineData(0)]
    [InlineData(8388609)]
    public async Task Package_bytes_are_bounded_before_opening_an_archive(int length) =>
        await Reject(() => FormalNuGetVerifier.VerifyAsync(ReleaseId, new byte[length]), "nuget-package-size");

    [Theory]
    [InlineData("flip")]
    [InlineData("append")]
    [InlineData("substitute")]
    public async Task Public_entry_rejects_changed_or_cross_package_bytes_before_crypto(string mutation)
    {
        var bytes = PackageBytes(ReleaseId);
        if (mutation == "flip") bytes[^1] ^= 1;
        if (mutation == "append") bytes = bytes.Concat(new byte[] { 10 }).ToArray();
        if (mutation == "substitute") bytes = PackageBytes("CP6.Platform.Contracts");
        await Reject(() => FormalNuGetVerifier.VerifyAsync(ReleaseId, bytes), "nuget-package-hash");
    }

    [Fact]
    public async Task Pre_cancelled_verification_does_not_read_or_verify_any_package()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FormalNuGetVerifier.VerifyAsync("", ReadOnlyMemory<byte>.Empty, cancellation.Token));
    }

    [Theory]
    [InlineData("unsigned", "nuget-author")]
    [InlineData("content", "nuget-signature")]
    [InlineData("no-timestamp", "nuget-timestamp-count")]
    [InlineData("version", "nuget-identity")]
    [InlineData("source", "nuget-source")]
    public async Task Real_crypto_component_rejects_mutated_archives_independently_of_the_outer_hash(string mutation, string expected)
    {
        var bytes = MutateArchive(PackageBytes(ReleaseId), mutation);
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new PackageArchiveReader(stream);
        await Reject(() => NuGetPackageChecks.VerifyAsync(archive, ReleaseId, PinnedNuGetTrust.Load(),
            DateTimeOffset.UtcNow, CancellationToken.None), expected);
    }

    [Fact]
    public async Task Real_NuGet_timestamp_parser_rejects_the_corrupted_CMS_signature()
    {
        var bytes = MutateArchive(PackageBytes(ReleaseId), "bad-timestamp-signature");
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new PackageArchiveReader(stream);
        var failure = await Assert.ThrowsAsync<NuGet.Packaging.Signing.TimestampException>(() =>
            NuGetPackageChecks.VerifyAsync(archive, ReleaseId, PinnedNuGetTrust.Load(),
                DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.IsType<CryptographicException>(failure.InnerException);
    }

    [Fact]
    public async Task Real_provider_rejects_a_valid_package_under_an_unpinned_allowlist()
    {
        using var stream = new MemoryStream(PackageBytes(ReleaseId), writable: false);
        using var archive = new PackageArchiveReader(stream);
        await Reject(() => NuGetPackageChecks.RequireNuGetSignatureAsync(archive, new string('a', 64), CancellationToken.None),
            "nuget-signature");
    }

    [Fact]
    public async Task Cryptographically_valid_package_is_not_another_package_identity()
    {
        using var stream = new MemoryStream(PackageBytes(ReleaseId), writable: false);
        using var archive = new PackageArchiveReader(stream);
        await Reject(() => NuGetPackageChecks.VerifyAsync(archive, "CP6.Platform.Contracts", PinnedNuGetTrust.Load(),
            DateTimeOffset.UtcNow, CancellationToken.None), "nuget-identity");
    }

    [Theory]
    [InlineData("2026-09-01T00:00:00Z", "nuget-timestamp-future")]
    [InlineData("2029-01-01T00:00:00Z", "signer-validity")]
    public async Task Current_consumption_rejects_future_timestamp_or_expired_author_policy(string evaluation, string expected)
    {
        using var stream = new MemoryStream(PackageBytes(ReleaseId), writable: false);
        using var archive = new PackageArchiveReader(stream);
        await Reject(() => NuGetPackageChecks.VerifyAsync(archive, ReleaseId, PinnedNuGetTrust.Load(),
            DateTimeOffset.Parse(evaluation), CancellationToken.None), expected);
    }

    internal static byte[] PackageBytes(string id)
    {
        var root = Environment.GetEnvironmentVariable("P10_FORMAL_PACKAGE_ROOT")
            ?? throw new InvalidOperationException("The actual seven-package formal readback directory is required; tests do not skip it.");
        return File.ReadAllBytes(Path.Combine(root, id + ".0.10.1.nupkg"));
    }

    private static byte[] CertificateBytes()
    {
        using var resource = typeof(PinnedNuGetTrust).Assembly.GetManifestResourceStream("CP6.P10.formal-author.cer")
            ?? throw new InvalidOperationException("Embedded public author certificate is required.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        return memory.ToArray();
    }

    private static byte[] MutateArchive(byte[] bytes, string mutation)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                if (mutation == "unsigned" && entry.FullName == ".signature.p7s") continue;
                using var entryStream = entry.Open();
                using var content = new MemoryStream();
                entryStream.CopyTo(content);
                var data = content.ToArray();
                if (mutation == "content" && entry.FullName.EndsWith(".dll", StringComparison.Ordinal)) data[^1] ^= 1;
                if ((mutation is "source" or "version") && entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                {
                    var text = Encoding.UTF8.GetString(data);
                    text = mutation == "source"
                        ? text.Replace("3ff27e26962dcfd722887afb80a4306010dd9ee1", new string('a', 40), StringComparison.Ordinal)
                        : text.Replace("<version>0.10.1</version>", "<version>0.10.0</version>", StringComparison.Ordinal);
                    data = Encoding.UTF8.GetBytes(text);
                }
                if ((mutation is "no-timestamp" or "bad-timestamp-signature") && entry.FullName == ".signature.p7s")
                {
                    var cms = new SignedCms();
                    cms.Decode(data);
                    var signer = cms.SignerInfos[0];
                    var attribute = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single();
                    var value = attribute.Values[0];
                    signer.RemoveUnsignedAttribute(value);
                    if (mutation == "bad-timestamp-signature")
                    {
                        var raw = value.RawData.ToArray();
                        raw[^1] ^= 1;
                        signer.AddUnsignedAttribute(new AsnEncodedData(new Oid(attribute.Oid.Value!), raw));
                    }
                    data = cms.Encode();
                }
                var destination = target.CreateEntry(entry.FullName, entry.FullName == ".signature.p7s"
                    ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                destination.ExternalAttributes = entry.ExternalAttributes;
                destination.LastWriteTime = entry.LastWriteTime;
                using var write = destination.Open();
                write.Write(data);
            }
        }
        return output.ToArray();
    }

    private static async Task Reject(Func<Task> action, string expected) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);
}
