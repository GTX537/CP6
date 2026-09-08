# P10 S06 Authenticated Locator Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` inline as already selected by the user. Do not delegate. Follow checkboxes in order.

**Goal:** Authenticate Platform Locators with the independently pinned current trust policy before exposing any signed object reference.

**Architecture:** Embed the reviewed public trust bytes in the verifier and require their compiled SHA-256 before package-owned policy parsing. The public entry uses that policy and actual UTC evaluation time; a deterministic internal core selects only the signature key, verifies the original bytes through real pinned cosign, and only then validates the full package contract and content-addressed subject. A sealed result with no public constructor represents authenticated discovery, not candidate acceptance.

**Tech Stack:** .NET SDK 8.0.424, exact formal CP6.Platform.Release 0.10.1, existing pinned cosign adapter, xUnit.

## Scope and invariants

- Continue S06 on `codex/p10-s06-platform-candidate` after `dcff6ff1ea03b68153a55168680c124d240a4dbb`; freshly fetched public main remains `6f9d09f4e3b1627a25ec7859b748eba8cd66f621`.
- Only the source, test, project resource/friend declaration and plan below change. No new dependency or lock change, trust-file change, workflow, remote object or acceptance CLI.
- Public trust anchor: `0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9`. An input cannot choose a replacement expected hash or policy. Returning a fresh parsed policy avoids a mutable global policy cache.
- Before signature verification, use only bounded canonical JSON and the key ID, policy version and exact UTC-millisecond signing-time selectors. Reject a future claimed signing time; evaluate validity/revocation using package-owned `Current` rules. A Locator timestamp remains a signed claim, not RFC3161 evidence.
- The expected tag comes from the caller's discovery request, not from a downloaded reference. Bound it to 128 characters, reject whitespace/control characters, then use the package-owned fixed-prefix key factory. After a valid signature, require full Locator contract, Platform lane, exact tag and strengthened content-address/hash binding.
- There is no graph fetching in this component. The eventual graph reader accepts the authenticated result and still must independently validate every referenced object's bytes and semantics.
- Tests exercise the internal deterministic core with actual ephemeral signatures and package-parsed fixture policy. `InternalsVisibleTo` is a normal unit-test seam, not a production CLI option: the public entry cannot accept a test policy or a caller-selected evaluation time. A separate test proves it refuses that valid self-signed fixture.
- Fixture subject bytes `{}` are intentionally not a valid candidate. Authentication authorizes reading the signed reference only; the later graph validator must reject it as a candidate. Do not publish these fixtures or count them as release evidence.

## File map

| Path | Responsibility |
| --- | --- |
| `tools/p10/ReleaseVerifier/VerifierTrust.cs` | Compiled hash and embedded public policy bootstrap |
| `tools/p10/ReleaseVerifier/AuthenticatedLocator.cs` | Signature-before-reference ordering and authenticated discovery result |
| `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj` | Embedded reviewed trust and internal-test visibility |
| `tools/p10/ReleaseVerifier.Tests/LocatorAuthenticationTests.cs` | Actual crypto and policy/order regression fixtures |
| This plan | Sequential work and local evidence |

## Task 1: Write failing authentication regressions

- [x] Create `tools/p10/ReleaseVerifier.Tests/LocatorAuthenticationTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// These ephemeral signed Locators exercise ordering and policy; none is release evidence.
public sealed class LocatorAuthenticationTests
{
    private const string Tag = "v0.10.1-test.991";
    private static readonly DateTimeOffset Evaluation = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
    private static byte[] TrustBytes() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
    private static CosignBlobVerifier Verifier() => new(
        Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ?? throw new InvalidOperationException("Pinned cosign is required."));
    private static CosignBlobVerifier MissingVerifier() => new(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));

    [Fact]
    public void Embedded_and_disk_trust_are_bound_to_the_independent_bootstrap_hash()
    {
        const string expected = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";
        Assert.Equal(expected, VerifierTrust.Load().ValidatedDocument.Sha256);
        Assert.Equal(expected, VerifierTrust.Parse(TrustBytes()).ValidatedDocument.Sha256);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("policy")]
    [InlineData("key")]
    public void Even_well_formed_replacement_trust_cannot_become_a_new_bootstrap(string mutation)
    {
        using var fixture = new Fixture();
        var node = JsonNode.Parse(TrustBytes())!.AsObject();
        if (mutation == "policy") node["minimumAcceptedPolicyVersion"] = 1 + node["policyVersion"]!.GetValue<int>();
        if (mutation == "key") node["keys"] = fixture.PolicyNode()["keys"]!.DeepClone();
        var bytes = mutation == "bytes" ? TrustBytes().Concat(new byte[] { 10 }).ToArray() : Canonical(node);
        Assert.Equal("trust-bootstrap-hash", Assert.Throws<Cp6ReleaseContractException>(() => VerifierTrust.Parse(bytes)).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public void Bootstrap_size_is_checked_before_hashing(int length) =>
        Assert.Equal("trust-bootstrap-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            VerifierTrust.Parse(new byte[length])).Code);

    [Fact]
    public async Task Real_signature_then_contract_validation_returns_only_the_authenticated_reference()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        var result = await Verify(fixture, bytes);
        Assert.Equal(Tag, result.ReleaseTag);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(fixture.KeyId, result.SignerKeyId);
        Assert.Equal(1, result.PolicyVersion);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex("{}"u8), result.Subject.Sha256);
        Assert.Equal($"candidates/platform/{Tag}/candidate-locator.v1.json", result.DiscoveryKeys.LocatorKey);
    }

    [Fact]
    public async Task Public_entry_rejects_a_valid_self_signed_fixture_not_in_the_compiled_trust()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        Assert.Equal("trust-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateAsync(Tag, bytes, fixture.Bundle(bytes), Verifier()))).Code);
    }

    [Theory]
    [InlineData("purpose", "trust-purpose")]
    [InlineData("revoked", "trust-revoked")]
    [InlineData("validity", "trust-validity")]
    public async Task Current_policy_failures_precede_any_signature_process(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var node = fixture.PolicyNode();
        var key = node["keys"]![0]!;
        if (mutation == "purpose") key["purpose"] = "oci";
        if (mutation == "validity") key["validUntilUtc"] = "2026-08-01T00:00:00.000Z";
        if (mutation == "revoked")
        {
            key["revokedAtUtc"] = "2026-09-07T11:00:00.000Z";
            key["revocationReason"] = "Regression fixture only";
        }
        var bytes = fixture.Locator();
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy.Parse(Canonical(node)),
                Evaluation, Tag, bytes, fixture.Bundle(bytes), MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("future", "locator-time")]
    [InlineData("fraction", "locator-selector")]
    [InlineData("version-zero", "locator-selector")]
    [InlineData("version-future", "trust-policy-version")]
    [InlineData("missing-key", "locator-selector")]
    [InlineData("unknown-key", "trust-key")]
    public async Task Invalid_selectors_fail_before_executable_or_subject_access(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator(node =>
        {
            if (mutation == "future") node["createdAtUtc"] = "2026-09-08T00:00:00.000Z";
            if (mutation == "fraction") node["createdAtUtc"] = "2026-09-07T00:00:00Z";
            if (mutation == "version-zero") node["trustPolicyVersion"] = 0;
            if (mutation == "version-future") node["trustPolicyVersion"] = 2;
            if (mutation == "missing-key") node.Remove("signerKeyId");
            if (mutation == "unknown-key") node["signerKeyId"] = "sha256:" + new string('a', 64);
            node["subject"] = "untrusted-subject-not-read";
        });
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, bytes, verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("tag", "locator-tag")]
    [InlineData("lane", "locator-lane")]
    [InlineData("key-binding", "object-key-binding")]
    public async Task A_real_signature_does_not_authorize_wrong_lane_tag_or_object_key(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator(node =>
        {
            if (mutation == "tag") node["releaseTag"] = "v0.10.1-test.992";
            if (mutation == "lane")
            {
                node["subjectKind"] = "SystemCandidateResult";
                node["subject"]!["mediaType"] = Cp6ReleaseMediaTypes.CandidateResult;
            }
            if (mutation == "key-binding")
                node["subject"]!["key"] = "objects/sha256/aa/" + new string('a', 64) + "/candidate.json";
        });
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(fixture, bytes))).Code);
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_before_even_parsing_a_malformed_subject()
    {
        using var fixture = new Fixture();
        var original = fixture.Locator();
        var altered = fixture.Locator(node => node["subject"] = "not-an-object");
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, altered, fixture.Bundle(original)))).Code);
    }

    [Fact]
    public async Task Correctly_signed_but_noncanonical_bytes_are_rejected()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator().Concat(new byte[] { 10 }).ToArray();
        Assert.Equal("non-canonical-json", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, bytes, verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    [InlineData("")]
    public async Task Caller_discovery_tag_cannot_escape_its_fixed_prefix(string tag)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, tag,
                bytes, fixture.Bundle(bytes), MissingVerifier()));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 4194305)]
    [InlineData(false, 4194305)]
    public async Task Locator_and_bundle_inputs_are_bounded_before_parsing(bool locator, int length)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        Assert.Equal("locator-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag,
                locator ? new byte[length] : bytes, locator ? fixture.Bundle(bytes) : new byte[length], MissingVerifier()))).Code);
    }

    [Fact]
    public async Task Cancellation_precedes_authentication_work()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag,
                fixture.Locator(), "{}"u8.ToArray(), MissingVerifier(), cancellation.Token));
    }

    private static Task<AuthenticatedLocator> Verify(Fixture fixture, byte[] bytes, byte[]? bundle = null,
        CosignBlobVerifier? verifier = null) =>
        AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag, bytes,
            bundle ?? fixture.Bundle(bytes), verifier ?? Verifier());

    private static byte[] Canonical(JsonNode node) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(node));

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string KeyId => "sha256:" + Cp6DeterministicJson.Sha256Hex(_key.ExportSubjectPublicKeyInfo());

        public JsonObject PolicyNode()
        {
            var root = JsonNode.Parse(TrustBytes())!.AsObject();
            root["keys"] = new JsonArray(new JsonObject
            {
                ["keyId"] = KeyId,
                ["purpose"] = "candidate-locator",
                ["validFromUtc"] = "2026-01-01T00:00:00.000Z",
                ["validUntilUtc"] = "2028-01-01T00:00:00.000Z",
                ["publicKey"] = PemEncoding.WriteString("PUBLIC KEY", _key.ExportSubjectPublicKeyInfo())
            });
            return root;
        }

        public Cp6PinnedTrustPolicy Policy() => Cp6PinnedTrustPolicy.Parse(Canonical(PolicyNode()));

        public byte[] Locator(Action<JsonObject>? change = null)
        {
            var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.PlatformReleaseCandidate, "candidate.json");
            var node = new JsonObject
            {
                ["$schemaId"] = Cp6ReleaseContractIds.CandidateLocator,
                ["releaseTag"] = Tag,
                ["subjectKind"] = "PlatformReleaseCandidate",
                ["subject"] = JsonNode.Parse(reference.ToJson().GetRawText()),
                ["trustPolicyVersion"] = 1,
                ["signerKeyId"] = KeyId,
                ["createdAtUtc"] = "2026-09-07T10:00:00.000Z"
            };
            change?.Invoke(node);
            return Canonical(node);
        }

        public byte[] Bundle(byte[] bytes) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
            verificationMaterial = new
            {
                publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(_key.ExportSubjectPublicKeyInfo())) }
            },
            messageSignature = new
            {
                messageDigest = new { algorithm = "SHA2_256", digest = Convert.ToBase64String(SHA256.HashData(bytes)) },
                signature = Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
            }
        });

        public void Dispose() => _key.Dispose();
    }
}
```

- [x] Run from `tools/p10` with approved .NET 8.0.424 host selected and `P10_COSIGN_PATH` set to the already checksum-verified binary:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --filter FullyQualifiedName~LocatorAuthenticationTests
```

Expected: missing new types. Add the following resource wiring and throwing scaffolds solely to produce an executable RED, then repeat the command; expected all 30 cases fail from the unimplemented entry points, zero skipped.

Complete `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj`:

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
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="../../../eng/p10/trust/pinned-trust-store.v1.json"
                      LogicalName="CP6.P10.pinned-trust-store.v1.json" />
    <InternalsVisibleTo Include="CP6.P10.ReleaseVerifier.Tests" />
  </ItemGroup>
</Project>
```

Scaffold `tools/p10/ReleaseVerifier/VerifierTrust.cs`:

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class VerifierTrust
{
    public static Cp6PinnedTrustPolicy Load() => throw new NotImplementedException();
    public static Cp6PinnedTrustPolicy Parse(ReadOnlySpan<byte> bytes) => throw new NotImplementedException();
}
```

Scaffold `tools/p10/ReleaseVerifier/AuthenticatedLocator.cs`:

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public sealed class AuthenticatedLocator
{
    public string ReleaseTag => throw new NotImplementedException();
    public string Sha256 => throw new NotImplementedException();
    public string SignerKeyId => throw new NotImplementedException();
    public int PolicyVersion => throw new NotImplementedException();
    public ContentAddress Subject => throw new NotImplementedException();
    public Cp6CandidateLocatorKeys DiscoveryKeys => throw new NotImplementedException();

    public static Task<AuthenticatedLocator> AuthenticateAsync(string expectedReleaseTag,
        ReadOnlyMemory<byte> locator, ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal static Task<AuthenticatedLocator> AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy policy,
        DateTimeOffset evaluationUtc, string expectedReleaseTag, ReadOnlyMemory<byte> locator,
        ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
```

## Task 2: Implement pinned bootstrap and signature-first authentication

- [x] Replace `tools/p10/ReleaseVerifier/VerifierTrust.cs` with:

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class VerifierTrust
{
    private const string ExpectedSha256 = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";

    public static Cp6PinnedTrustPolicy Load()
    {
        using var stream = typeof(VerifierTrust).Assembly.GetManifestResourceStream("CP6.P10.pinned-trust-store.v1.json")
            ?? throw Error("trust-bootstrap-resource");
        using var reader = new BinaryReader(stream);
        return Parse(reader.ReadBytes(Cp6DeterministicJson.MaximumBytes + 1));
    }

    public static Cp6PinnedTrustPolicy Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("trust-bootstrap-size");
        if (!string.Equals(Cp6DeterministicJson.Sha256Hex(bytes), ExpectedSha256, StringComparison.Ordinal))
            throw Error("trust-bootstrap-hash");
        return Cp6PinnedTrustPolicy.Parse(bytes);
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Verifier trust does not match the independently compiled trust anchor.");
}
```

- [x] Replace `tools/p10/ReleaseVerifier/AuthenticatedLocator.cs` with:

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// This authenticates one Locator only. It does not accept the referenced candidate graph.
public sealed class AuthenticatedLocator
{
    private AuthenticatedLocator(string releaseTag, string sha256, string signerKeyId, int policyVersion,
        DateTimeOffset createdAtUtc, ContentAddress subject, Cp6CandidateLocatorKeys discoveryKeys)
    {
        ReleaseTag = releaseTag;
        Sha256 = sha256;
        SignerKeyId = signerKeyId;
        PolicyVersion = policyVersion;
        CreatedAtUtc = createdAtUtc;
        Subject = subject;
        DiscoveryKeys = discoveryKeys;
    }

    public string ReleaseTag { get; }
    public string Sha256 { get; }
    public string SignerKeyId { get; }
    public int PolicyVersion { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public ContentAddress Subject { get; }
    public Cp6CandidateLocatorKeys DiscoveryKeys { get; }

    public static Task<AuthenticatedLocator> AuthenticateAsync(string expectedReleaseTag,
        ReadOnlyMemory<byte> locator, ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default) =>
        AuthenticateWithPolicyAsync(VerifierTrust.Load(), DateTimeOffset.UtcNow,
            expectedReleaseTag, locator, bundle, verifier, cancellationToken);

    // Internal separation permits deterministic policy regression with real ephemeral signatures.
    // The public entry point cannot accept caller-supplied policy, evaluation time or verification success.
    internal static async Task<AuthenticatedLocator> AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy policy,
        DateTimeOffset evaluationUtc, string expectedReleaseTag, ReadOnlyMemory<byte> locator,
        ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedReleaseTag.Length is < 1 or > 128 ||
            expectedReleaseTag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw Error("locator-tag");
        var discoveryKeys = Cp6CandidateLocatorKeys.ForPlatformTag(expectedReleaseTag);
        if (locator.Length is < 1 or > Cp6DeterministicJson.MaximumBytes ||
            bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("locator-size");
        var bytes = locator.ToArray();
        var bundleBytes = bundle.ToArray();
        var canonical = Cp6DeterministicJson.Canonicalize(bytes);
        if (!bytes.AsSpan().SequenceEqual(canonical)) throw Error("non-canonical-json");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var keyId = Selector(root, "signerKeyId");
        var time = Selector(root, "createdAtUtc");
        if (!root.TryGetProperty("trustPolicyVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var policyVersion) ||
            policyVersion < 1 || !DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var signedAtUtc)) throw Error("locator-selector");
        if (signedAtUtc > evaluationUtc) throw Error("locator-time");
        var key = policy.RequireKey(keyId, "candidate-locator", policyVersion, signedAtUtc, evaluationUtc,
            Cp6ReleaseValidationMode.Current);

        // Do not inspect or follow the subject or any document-chosen location before this succeeds.
        await verifier.VerifyAsync(bytes, bundleBytes, key, cancellationToken);
        var validated = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        if (validated.SubjectKind != "PlatformReleaseCandidate") throw Error("locator-lane");
        if (!string.Equals(root.GetProperty("releaseTag").GetString(), expectedReleaseTag, StringComparison.Ordinal))
            throw Error("locator-tag");
        var subject = ContentAddress.Parse(root.GetProperty("subject"));
        _ = policy.RequireStorageAuthority(ContentAddress.StorageAuthority);
        return new(expectedReleaseTag, validated.Sha256, keyId, policyVersion, signedAtUtc, subject, discoveryKeys);
    }

    private static string Selector(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw Error("locator-selector");
        return value.GetString()!;
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Locator authentication violates the pinned Platform discovery policy.");
}
```

- [x] Repeat the focused tests. Expected: 30/30, including actual cosign signed-reference success, all signed semantic mutations rejected, and bad signature rejected before malformed subject parsing.
- [x] Run the complete verifier suite and formatting:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configuration Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: 97/97 (67 previous plus 30 new), zero skips/warnings/errors, no format changes required.

## Task 3: Review and checkpoint

- [x] Review all five files, compare source/project against the complete code blocks, scan for private keys/machine paths and require `git diff --check`. Confirm unchanged package locks, public trust input, application and old R2 workflows.
- [ ] Stage exactly the task files and commit:

```powershell
git add docs/superpowers/plans/2026-09-07-p10-s06-authenticated-locator.md tools/p10/ReleaseVerifier/VerifierTrust.cs tools/p10/ReleaseVerifier/AuthenticatedLocator.cs tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj tools/p10/ReleaseVerifier.Tests/LocatorAuthenticationTests.cs
git diff --cached --check
git commit -m "feat(p10): authenticate locators before using signed references"
```

- [ ] Continue the full S06 graph, live workflow/package/OCI/R2 adapters and publication workflow, CI wiring, four-ledger updates and pre/post checks before opening a completion PR. The actual Environment-key positive signature and live post-commit discovery remain mandatory final evidence.

## Local authentication checkpoint (2026-09-07 owner time)

- The missing-type compilation result was followed by an executable throwing-scaffold RED: all 30 cases failed from unimplemented authentication/bootstrap APIs, zero passed and zero skipped.
- Focused implementation tests passed 30/30. Full Release regression passed 97/97 with zero skips, warnings or errors. Formatting initially identified four same-line dictionary entries in the new test fixture; only their line breaks were corrected, synchronized to this plan, and both format verification and all 97 Release tests passed again.
- Real ephemeral signatures prove signature-before-subject ordering and rejection of signed wrong-lane, wrong-tag and wrong-key-path references. The public entry rejects the independently valid fixture key, while embedded and disk trust must both equal the compiled anchor. Current revocation and purpose/validity failures precede executable access.
- The five-file scope was reviewed; original trust bytes, package graph/locks, old R2, application and workflows are unchanged. This is local authenticated discovery only, not a valid candidate graph or real Environment-key publication.
