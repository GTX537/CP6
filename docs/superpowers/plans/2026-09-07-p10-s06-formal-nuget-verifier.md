# P10 S06 Formal NuGet Cryptography Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user selected sequential execution within the current task; no delegation or repeated execution-choice question.

**Goal:** Independently verify the selected seven formal package archives with the compiled author policy, real NuGet integrity/author verification, and a real RFC3161 chain, without claiming full candidate acceptance.

**Architecture:** Add an embedded, hash-pinned NuGet policy plus public certificate bootstrap. The public verifier first checks bounded raw package bytes against the independently fixed release hash, then calls a focused crypto component with current policy/time. It verifies NuSpec identity, author primary signature, a single RFC3161 timestamp, strict NuGet providers, CMS/imprint/ESS certificate binding and an online system-root TSA chain; return immutable package proof only.

**Tech Stack:** .NET SDK 8.0.424/.NET 8, CP6.Platform.Release [0.10.1], NuGet.Packaging [6.11.2], System.Security.Cryptography.Pkcs [8.0.1], xUnit.

---

## Scope, sources and boundaries

- Continue public S06 worktree after df42b630e4f54d93bc218049091f850c12e1f28a; no other repository edit, release republish, Registry/R2 write, runtime activation or deployment.
- Platform package remains owner of certificate and trust-policy validation. Public CP6 adds the I/O/cryptography adapter, not copied schemas or Platform source.
- The existing public policy bytes are SHA-256 da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f; public author DER is 1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d. Both are embedded and independently hash checked before parsing.
- Positive local tests use all seven actual S04 GitHub Packages readback archives retained from the publication evidence. Their hashes have already been checked against the frozen 0.10.1 publication. They are historical readback bytes, not a new download in this module; later remote validation still must download and independently verify them.
- The author exception remains exactly the pinned self-signed leaf: publicCaTrusted=false, internallyTrusted=true. This never relaxes RFC3161 TSA system-root trust or online revocation.
- Reuse the exact crypto library versions already exercised by Platform/CRM. NuGet lists 6.11.2 among the patched versions for its 2026-04-14 package identity defense update. [NuGet advisory](https://github.com/NuGet/NuGet.Client/security/advisories/GHSA-g4vj-cjjj-v7hg). [Pkcs 8.0.1 package](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/8.0.1).
- Rfc3161TimestampToken.VerifySignatureForSignerInfo verifies TSA certificate/token/imprint binding, not a CA chain by itself. Add a separate X509Chain with system roots, Online/EntireChain, no verification flags, timestamp verification time and a 10-second certificate URL retrieval timeout; compare the resulting exact four certificate hashes with S04. [Microsoft API contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamptoken.verifysignatureforsignerinfo).
- Public entry cannot choose package version/source/hash, trust policy, evaluation time, historical mode, arbitrary success flags or missing-timestamp exceptions. The selected release pins remain in S06ReleaseIdentity.
- Internal crypto composition permits direct rejection tests against actual corrupted archives without weakening the public fixed-hash entry. Tests make no synthetic positive certificate/timestamp, private key, mock crypto provider or publication.
- This module changes only three implementation files, one test file, production project resources/dependencies, both generated lock files and this plan. Existing raw trust/certificate bytes and runtime/workflow code stay unchanged.

## File map

| File | Responsibility |
|---|---|
| tools/p10/ReleaseVerifier/PinnedNuGetTrust.cs | Embedded exact-hash public policy and DER bootstrap |
| tools/p10/ReleaseVerifier/FormalNuGetVerifier.cs | Fixed raw-byte selection, bounded input, public current-policy wrapper and immutable result |
| tools/p10/ReleaseVerifier/NuGetPackageChecks.cs | NuSpec/author/NuGet integrity/RFC3161/online TSA chain verification |
| tools/p10/ReleaseVerifier.Tests/FormalNuGetVerifierTests.cs | Seven real package positives, trust/hash/bounds and real crypto mutation regressions |
| tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj | Exact crypto dependencies and embedded public resources |
| Both tools/p10 project packages.lock.json files | Generated dependency graph, then locked-restore verification |

## Task 1: Tests and dependency configuration

- [x] Add the complete test file before implementation.

```csharp
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
```

- [x] Change only the production project configuration to this complete content, then generate and review the necessary lock changes. Configuration does not implement verification behavior.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CP6.Platform.Release" Version="[0.10.1]" GeneratePathProperty="true" />
    <PackageReference Include="NuGet.Packaging" Version="[6.11.2]" />
    <PackageReference Include="System.Security.Cryptography.Pkcs" Version="[8.0.1]" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="../../../eng/p10/trust/pinned-trust-store.v1.json"
                      LogicalName="CP6.P10.pinned-trust-store.v1.json" />
    <EmbeddedResource Include="../../../eng/p10/trust/p10-formal-nuget-trust-store.v1.json"
                      LogicalName="CP6.P10.pinned-nuget-trust-store.v1.json" />
    <EmbeddedResource Include="../../../eng/p10/trust/certificates/1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d.cer"
                      LogicalName="CP6.P10.formal-author.cer" />
    <InternalsVisibleTo Include="CP6.P10.ReleaseVerifier.Tests" />
  </ItemGroup>
</Project>
```

- [x] Set the approved .NET 8 host, existing pinned cosign path and the actual seven-package directory through the process environment. These must not be stored as machine-specific repository configuration.

```powershell
if (-not $env:DOTNET_HOST_PATH -or -not (Test-Path -LiteralPath $env:DOTNET_HOST_PATH)) { throw 'An approved .NET 8 host is required.' }
if (-not $env:P10_FORMAL_PACKAGE_ROOT -or -not (Test-Path -LiteralPath $env:P10_FORMAL_PACKAGE_ROOT)) { throw 'The actual seven-package readback directory is required.' }
$env:DOTNET_ROOT = Split-Path -Parent $env:DOTNET_HOST_PATH
$env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --force-evaluate
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~FormalNuGetVerifierTests
```

Expected: restore succeeds with required signatures and the existing CP6 source mapping; tests initially fail to compile because the new verifier API is absent. Preserve all prior resolved package versions/hashes; additional NuGet/Common/Frameworks/Versioning and Pkcs dependencies must match the locked resolution. Do not suppress an audit, signature, restore or certificate error.

- [x] Add only these throwing scaffolds and repeat the focused test command to demonstrate actual missing-behavior failures.

### Throwing scaffold: tools/p10/ReleaseVerifier/PinnedNuGetTrust.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class PinnedNuGetTrust
{
    public static Cp6PinnedNuGetTrustPolicy Load() => throw new NotImplementedException();
    public static Cp6PinnedNuGetTrustPolicy Parse(ReadOnlySpan<byte> policy, ReadOnlySpan<byte> certificate) =>
        throw new NotImplementedException();
}
```

### Throwing scaffold: tools/p10/ReleaseVerifier/FormalNuGetVerifier.cs

```csharp
using System.Collections.ObjectModel;

namespace CP6.P10.ReleaseVerifier;

public static class FormalNuGetVerifier
{
    public static Task<VerifiedNuGetPackage> VerifyAsync(string expectedPackageId,
        ReadOnlyMemory<byte> package, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class VerifiedNuGetPackage
{
    public string PackageId => throw new NotImplementedException();
    public string Version => throw new NotImplementedException();
    public string SourceGitSha => throw new NotImplementedException();
    public string Sha256 => throw new NotImplementedException();
    public string SignerFingerprint => throw new NotImplementedException();
    public string SpkiKeyId => throw new NotImplementedException();
    public string TimestampPolicyOid => throw new NotImplementedException();
    public DateTimeOffset TimestampUtc => throw new NotImplementedException();
    public ReadOnlyCollection<string> TimestampCertificateChainSha256 => throw new NotImplementedException();
    public bool PublicCaTrusted => throw new NotImplementedException();
    public bool InternallyTrusted => throw new NotImplementedException();
}
```

### Throwing scaffold: tools/p10/ReleaseVerifier/NuGetPackageChecks.cs

```csharp
using CP6.Platform.Release;
using NuGet.Packaging;

namespace CP6.P10.ReleaseVerifier;

internal static class NuGetPackageChecks
{
    internal static Task<NuGetCryptographyProof> VerifyAsync(PackageArchiveReader archive,
        string expectedId, Cp6PinnedNuGetTrustPolicy policy, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    internal static Task RequireNuGetSignatureAsync(PackageArchiveReader package, string fingerprint,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}

internal sealed record NuGetCryptographyProof(DateTimeOffset TimestampUtc, IReadOnlyList<string> CertificateChainSha256);
```

Expected: all 35 cases fail with NotImplementedException, zero skipped. Inspect actual test results; fix any setup/fixture problem before adding behavior.

## Task 2: Fixed bootstrap and real cryptography

- [x] Replace the throwing scaffolds with the following complete implementation after observing Red.

### tools/p10/ReleaseVerifier/PinnedNuGetTrust.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class PinnedNuGetTrust
{
    public static Cp6PinnedNuGetTrustPolicy Load() =>
        Parse(Resource("CP6.P10.pinned-nuget-trust-store.v1.json", Cp6DeterministicJson.MaximumBytes),
            Resource("CP6.P10.formal-author.cer", 65536));

    public static Cp6PinnedNuGetTrustPolicy Parse(ReadOnlySpan<byte> policy, ReadOnlySpan<byte> certificate)
    {
        Require(policy.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            certificate.Length is > 0 and <= 65536, "nuget-trust-size");
        Require(Cp6DeterministicJson.Sha256Hex(policy) == S06ReleaseIdentity.NuGetTrustHash, "nuget-trust-hash");
        Require(Cp6DeterministicJson.Sha256Hex(certificate) == S06ReleaseIdentity.Signer, "nuget-certificate-hash");
        return Cp6PinnedNuGetTrustPolicy.Parse(policy, new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
        {
            ["certificates/" + S06ReleaseIdentity.Signer + ".cer"] = certificate.ToArray()
        });
    }

    private static byte[] Resource(string name, int maximumBytes)
    {
        using var stream = typeof(PinnedNuGetTrust).Assembly.GetManifestResourceStream(name)
            ?? throw Error("nuget-trust-resource");
        using var reader = new BinaryReader(stream);
        return reader.ReadBytes(maximumBytes + 1);
    }

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Formal NuGet verification violates the independently pinned policy.");
}
```

### tools/p10/ReleaseVerifier/FormalNuGetVerifier.cs

```csharp
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
```

### tools/p10/ReleaseVerifier/NuGetPackageChecks.cs

```csharp
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
    private static readonly string[] TimestampChain =
    [
        "2da09da7f4131f9fe72db6c5e6e9c9656755af043f1ea742cc0d2120e141ebfc",
        "ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd",
        "33846b545a49c9be4903c60e01713c1bd4e4ef31ea65cd95d69e62794f30b941",
        "3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c"
    ];

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
        Require(hashes.SequenceEqual(TimestampChain, StringComparer.Ordinal), "nuget-timestamp-chain-binding");
        return new(timestamp.GeneralizedTime, hashes);
    }
}

internal sealed record NuGetCryptographyProof(DateTimeOffset TimestampUtc, IReadOnlyList<string> CertificateChainSha256);
```

- [x] Repeat the focused test command. Expected: 35 passed, zero failed/skipped. Real TSA revocation/network unavailability is a failure, not a skip or trust exception.
- [x] Run the complete suite, formatting and dependency vulnerability check from tools/p10.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
dotnet list ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json
```

Expected: existing 171 plus 35 new cases pass; formatting exits 0; no known vulnerable dependency in the queried current advisory data. This is a time-bounded dependency check, not a guarantee of absence of vulnerabilities.

## Task 3: Review, record and scoped commit

- [x] Compare every source/test/project file with its complete plan block.
- [x] Review the lock-file diff: CP6.Platform.Release remains exactly 0.10.1, all existing resolutions retain their content hashes, and the newly introduced versions/content hashes are the verified intended ones.
- [x] Review all eight changed task files against main; verify no private key, token, machine-specific configuration, package overwrite, runtime registration, false public-CA claim or relaxed timestamp/provider flag.
- [x] Record observed test and audit outcomes below only after the commands complete.
- [ ] Stage only the listed files and make a normal commit.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-07-p10-s06-formal-nuget-verifier.md tools/p10/ReleaseVerifier/PinnedNuGetTrust.cs tools/p10/ReleaseVerifier/FormalNuGetVerifier.cs tools/p10/ReleaseVerifier/NuGetPackageChecks.cs tools/p10/ReleaseVerifier.Tests/FormalNuGetVerifierTests.cs tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj tools/p10/ReleaseVerifier/packages.lock.json tools/p10/ReleaseVerifier.Tests/packages.lock.json
git diff --cached --check
git commit -m "feat(p10): verify formal package bytes and RFC3161 signatures"
```

- [ ] Continue full S06: authenticated network reads, actual source/workflow/CRM forward binding, OCI build/sign/report provenance, candidate assembly, real R2 conditional commit protocol, clean pre/post-commit verification, workflows and four-ledger cross-repository audit. This module alone does not publish or accept a candidate.

## Self-review

The complete code consumes the formal Platform trust API and existing immutable identities, adds no caller-selected trust bypass, and keeps author self-signed trust distinct from TSA system trust. Positive cryptography is exercised against all seven real readback packages; mutations exercise actual NuGet signature providers. Workflow/Registry/R2 provenance, freshness, network credential isolation and candidate discovery remain explicit obligations of the full verifier.

## Observed execution

- Initial missing-API compile failure was followed by throwing scaffolds: all 35 cases actually failed with NotImplementedException, zero skipped (local TRX under artifacts/p10/nuget-red).
- First implementation run passed all seven actual package author/integrity/RFC3161 cases, but three mutation fixtures failed on ZIP signature-entry metadata. NuGet's signed-archive reader requires an uncompressed signature entry and zero external attributes; the test helper now preserves those requirements. No production trust check was relaxed.
- After that correction, the corrupted timestamp was rejected earlier by the real NuGet timestamp parser with TimestampException and an inner CryptographicException. The dedicated test now asserts that exact crypto rejection instead of incorrectly expecting the later provider result. Missing timestamp and changed content also reject as intended.
- Corrected focused run: 35 passed, zero failed/skipped. Complete Release suite: 206 passed, zero failed/skipped, no build warnings/errors. Whitespace verification exited 0. Current nuget.org advisory query reported no vulnerable direct/transitive production package.
- Both generated lock files preserve all pre-existing resolved versions/content hashes. New resolutions match the intended exact crypto versions and restored NuGet metadata. The signed raw package SHA-256 remains the fixed publication hash; it is distinct from NuGet's lock content-hash semantics. No trust bytes, certificate, workflow or application runtime file changed.
- Full source/test/project blocks were compared with the plan, and the scoped diff/hygiene review passed. No new Registry readback, candidate acceptance or S06 completion is implied by these historical-package local tests.
