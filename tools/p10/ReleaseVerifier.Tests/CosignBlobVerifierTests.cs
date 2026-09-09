using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Ephemeral signatures are cryptographic regression fixtures, never release evidence.
public sealed class CosignBlobVerifierTests
{
    private static readonly byte[] Payload = "{ \"fixture\": \"cosign-byte-binding\" }\n"u8.ToArray();
    private static CosignBlobVerifier Verifier() => new(
        Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
        throw new InvalidOperationException("P10_COSIGN_PATH must name the checksum-pinned cosign v3.1.3-cp6.2 binary."));

    [Fact]
    public async Task Actual_cosign_accepts_the_exact_bytes_signed_by_the_supplied_P256_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Verifier().VerifyAsync(Payload, Bundle(key), PublicKey(key));
    }

    [Fact]
    public async Task Actual_cosign_rejects_changed_payload_even_with_the_original_valid_bundle()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var changed = Payload.ToArray();
        changed[3] ^= 1;
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyAsync(changed, Bundle(key), PublicKey(key)))).Code);
    }

    [Fact]
    public async Task Actual_cosign_rejects_a_different_valid_public_key()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyAsync(Payload, Bundle(signer), PublicKey(other)))).Code);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("digest")]
    [InlineData("unsigned")]
    [InlineData("empty")]
    public async Task Actual_cosign_rejects_tampered_or_unsigned_bundles(string mutation)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var node = JsonNode.Parse(Bundle(key))!.AsObject();
        if (mutation == "empty") node = new JsonObject();
        else if (mutation == "digest")
            node["messageSignature"]!["messageDigest"]!["digest"] = Convert.ToBase64String(new byte[32]);
        else
            node["messageSignature"]!["signature"] = mutation == "unsigned"
                ? "" : Convert.ToBase64String(new byte[72]);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyAsync(Payload, JsonSerializer.SerializeToUtf8Bytes(node), PublicKey(key)))).Code);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 4194305)]
    [InlineData(false, 4194305)]
    public async Task Unbounded_inputs_are_rejected_before_resolving_an_executable(bool payload, int length)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new CosignBlobVerifier(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));
        Assert.Equal("cosign-input", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            verifier.VerifyAsync(payload ? new byte[length] : Payload,
                payload ? Bundle(key) : new byte[length], PublicKey(key)))).Code);
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("purpose")]
    [InlineData("pem")]
    [InlineData("curve")]
    public async Task Malformed_key_metadata_cannot_override_the_fixed_key_profile(string mutation)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var trusted = PublicKey(key);
        trusted = mutation switch
        {
            "identifier" => trusted with { KeyId = "sha256:" + new string('a', 64) },
            "purpose" => trusted with { Purpose = "arbitrary" },
            "pem" => trusted with { PublicKey = trusted.PublicKey + "\n" },
            _ => PublicKey(wrongCurve)
        };
        var verifier = new CosignBlobVerifier(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));
        Assert.Equal("cosign-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            verifier.VerifyAsync(Payload, Bundle(key), trusted))).Code);
    }

    [Fact]
    public async Task Missing_tool_failure_is_sanitized()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new CosignBlobVerifier(Path.Combine(Path.GetTempPath(), "private-path-marker", "cosign"));
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            verifier.VerifyAsync(Payload, Bundle(key), PublicKey(key)));
        Assert.Equal("cosign-tool", error.Code);
        Assert.DoesNotContain("private-path-marker", error.ToString());
    }

    [Fact]
    public async Task Arbitrary_executable_bytes_are_never_launched_as_cosign()
    {
        var directory = Directory.CreateTempSubdirectory("cp6-p10-cosign-test-").FullName;
        var path = Path.Combine(directory, "not-cosign.bin");
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                file.Write("not an executable"u8);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Assert.Equal("cosign-tool", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                new CosignBlobVerifier(path).VerifyAsync(Payload, Bundle(key), PublicKey(key)))).Code);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory); // Non-recursive; this test owns the single created file.
        }
    }

    [Fact]
    public async Task Precancelled_verification_never_starts_a_process()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosignBlobVerifier(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"))
                .VerifyAsync(Payload, Bundle(key), PublicKey(key), cancellation.Token));
    }

    private static Cp6PinnedTrustKey PublicKey(ECDsa key)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        return new("sha256:" + Cp6DeterministicJson.Sha256Hex(spki), "candidate-locator",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2028-01-01T00:00:00Z"),
            PemEncoding.WriteString("PUBLIC KEY", spki), null, null);
    }

    private static byte[] Bundle(ECDsa key) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
        verificationMaterial = new
        {
            publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo())) }
        },
        messageSignature = new
        {
            messageDigest = new { algorithm = "SHA2_256", digest = Convert.ToBase64String(SHA256.HashData(Payload)) },
            signature = Convert.ToBase64String(key.SignData(Payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence))
        }
    });
}
