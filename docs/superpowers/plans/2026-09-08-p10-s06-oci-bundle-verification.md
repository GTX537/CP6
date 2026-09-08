# P10 S06 OCI Digest Bundle Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Authenticate cosign v3 OCI signature bundles against an exact SHA-256 image digest and return only the authenticated DSSE payload bytes.

**Architecture:** Extend the existing checksum-pinned cosign adapter with an internal OCI-bundle entry. Both blob and OCI verification use the existing isolated subprocess, bounded output, cancellation, temporary public files and sanitized errors. Decode the owned bundle once through the duplicate-rejecting bounded JSON reader, invoke actual cosign with mandatory digest/predicate checks, then return those same payload bytes.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release [0.10.1], official cosign v3.1.3, xUnit, ephemeral ECDSA P-256 DSSE regression signatures.

---

## Scope and evidence boundary

This is cryptography, not candidate acceptance. It proves neither registry availability nor the build workflow, current trust policy, SBOM, scan or provenance. Callers must obtain keys from the compiled current trust policy and validate signed claims against the independently expected repository/source/run. The future protected workflow must supply the real ghcr.io/gtx537/cp6-p10-verifier image and its real bundle; no test fixture may become release evidence.

The chosen bundle is the actual cosign v3 default image-signature format: DSSE in-toto statement with predicate https://sigstore.dev/cosign/sign/v1. The verifier uses verify-blob-attestation, --digest (hex), --digestAlg sha256, --check-claims=true and the exact predicate. No mutable tag, registry URL, certificate identity or caller-controlled CLI flag reaches this entry. No image build, registry write, R2 mutation or deployment occurs in this module.

Official sources checked for this plan:

- [cosign v3.1.3 image signing](https://raw.githubusercontent.com/sigstore/cosign/v3.1.3/cmd/cosign/cli/sign/sign.go)
- [cosign v3.1.3 bundle verification](https://raw.githubusercontent.com/sigstore/cosign/v3.1.3/cmd/cosign/cli/verify/verify_blob_attestation.go)
- [fixed cosign predicate](https://raw.githubusercontent.com/sigstore/cosign/v3.1.3/pkg/types/predicate.go)
- [DSSE protocol](https://github.com/secure-systems-lab/dsse/blob/master/protocol.md)

Key-only pinned trust intentionally does not claim Rekor/Fulcio authority. The existing NuGet RFC3161 policy is unchanged. Test keys exist only in process memory. The duplicate-rejecting GitHubApiJson reader is reused for its general bounded JSON behavior; no GitHub request occurs and any reader error is sanitized to cosign-bundle. Neither envelope nor payload is CP6-canonicalized or reserialized.

## File map

- Modify tools/p10/ReleaseVerifier/CosignBlobVerifier.cs: shared private process core and internal digest-bundle entry.
- Create tools/p10/ReleaseVerifier/OciBundlePayload.cs: bounded, unambiguous DSSE payload extraction only.
- Create tools/p10/ReleaseVerifier.Tests/CosignOciBundleTests.cs: real cosign positive/tamper/digest/predicate tests and pre-process guards.
- Create this plan.

## Task 1: Write and observe failing tests

- [x] Add the complete test file first.

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real ephemeral P-256 DSSE signatures exercise cosign, never formal OCI acceptance.
// No image is built, uploaded, downloaded or claimed to exist by these tests.
public sealed class CosignOciBundleTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PayloadType = "application/vnd.in-toto+json";
    private static CosignBlobVerifier Verifier() => new(Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
        throw new InvalidOperationException("Checksum-pinned cosign v3.1.3 is required."));
    private static CosignBlobVerifier MissingVerifier() => new(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));

    [Fact]
    public async Task Actual_cosign_authenticates_DSSE_and_returns_the_exact_unreserialized_payload()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Payload();
        var bundle = Bundle(signer, payload);
        var verified = await Verifier().VerifyOciBundleAsync(Digest, bundle, PublicKey(signer));
        Assert.Equal(payload, verified);
        Assert.Contains("\n", Encoding.UTF8.GetString(verified));
        Array.Fill(bundle, (byte)0);
        Assert.Equal(payload, verified);
    }

    [Fact]
    public async Task Actual_cosign_rejects_a_valid_signature_by_a_different_key()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyOciBundleAsync(Digest, Bundle(signer, Payload()), PublicKey(other)))).Code);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("signature")]
    [InlineData("wrong-subject")]
    [InlineData("wrong-predicate")]
    public async Task Actual_cosign_rejects_signature_digest_or_predicate_substitution(string mutation)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Payload(node =>
        {
            if (mutation == "wrong-subject") node["subject"]![0]!["digest"]!["sha256"] = new string('b', 64);
            if (mutation == "wrong-predicate") node["predicateType"] = "https://example.invalid/not-an-image-signature";
        });
        var bundle = JsonNode.Parse(Bundle(signer, payload))!;
        if (mutation == "body")
            bundle["dsseEnvelope"]!["payload"] = Convert.ToBase64String(
                Payload(node => node["subject"]![0]!["annotations"]!["fixture"] = "changed"));
        if (mutation == "signature")
            bundle["dsseEnvelope"]!["signatures"]![0]!["sig"] = Convert.ToBase64String(new byte[72]);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyOciBundleAsync(Digest, JsonSerializer.SerializeToUtf8Bytes(bundle), PublicKey(signer)))).Code);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("payload-type")]
    [InlineData("payload-base64")]
    [InlineData("empty-payload")]
    [InlineData("payload-json")]
    [InlineData("payload-duplicate")]
    [InlineData("no-signature")]
    [InlineData("multiple-signatures")]
    [InlineData("blob-signature")]
    [InlineData("envelope-duplicate")]
    public async Task Ambiguous_or_wrong_bundle_shapes_fail_before_starting_cosign(string mutation)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundle = JsonNode.Parse(Bundle(signer, Payload()))!;
        var envelope = bundle["dsseEnvelope"]!;
        if (mutation == "media") bundle["mediaType"] = "application/json";
        if (mutation == "payload-type") envelope["payloadType"] = "text/plain";
        if (mutation == "payload-base64") envelope["payload"] = "invalid base64";
        if (mutation == "empty-payload") envelope["payload"] = "";
        if (mutation == "payload-json") envelope["payload"] = Convert.ToBase64String("not-json"u8);
        if (mutation == "payload-duplicate")
            envelope["payload"] = Convert.ToBase64String("{\"subject\":[],\"subject\":[]}"u8);
        if (mutation == "no-signature") envelope["signatures"] = new JsonArray();
        if (mutation == "multiple-signatures")
            envelope["signatures"]!.AsArray().Add(envelope["signatures"]![0]!.DeepClone());
        if (mutation == "blob-signature") bundle["messageSignature"] = new JsonObject();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle);
        if (mutation == "envelope-duplicate")
        {
            var json = Encoding.UTF8.GetString(bytes);
            bytes = Encoding.UTF8.GetBytes("{\"dsseEnvelope\":{}," + json[1..]);
        }
        Assert.Equal("cosign-bundle", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, bytes, PublicKey(signer)))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:abcd")]
    [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData("ghcr.io/gtx537/cp6-p10-verifier:latest")]
    public async Task Only_an_exact_lowercase_digest_can_reach_the_process(string digest)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-digest", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(digest, Bundle(signer, Payload()), PublicKey(signer)))).Code);
    }

    [Fact]
    public async Task Locator_purpose_cannot_be_used_to_authenticate_an_OCI_bundle()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, Bundle(signer, Payload()),
                PublicKey(signer) with { Purpose = "candidate-locator" }))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public async Task Bundle_size_is_checked_before_parsing_or_process_start(int length)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-input", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, new byte[length], PublicKey(signer)))).Code);
    }

    [Fact]
    public async Task Precancelled_bundle_verification_never_starts_cosign()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MissingVerifier().VerifyOciBundleAsync(
            Digest, Bundle(signer, Payload()), PublicKey(signer), cancellation.Token));
    }

    private static Cp6PinnedTrustKey PublicKey(ECDsa key)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        return new("sha256:" + Cp6DeterministicJson.Sha256Hex(spki), "oci",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2028-01-01T00:00:00Z"),
            PemEncoding.WriteString("PUBLIC KEY", spki), null, null);
    }

    private static byte[] Payload(Action<JsonObject>? mutate = null)
    {
        var node = new JsonObject
        {
            ["_type"] = "https://in-toto.io/Statement/v1",
            ["subject"] = new JsonArray(new JsonObject
            {
                ["digest"] = new JsonObject { ["sha256"] = Digest[7..] },
                ["annotations"] = new JsonObject { ["fixture"] = "not formal release evidence" }
            }),
            ["predicateType"] = "https://sigstore.dev/cosign/sign/v1",
            ["predicate"] = new JsonObject()
        };
        mutate?.Invoke(node);
        return JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] Bundle(ECDsa key, byte[] payload)
    {
        var prefix = Encoding.UTF8.GetBytes("DSSEv1 " +
            Encoding.UTF8.GetByteCount(PayloadType).ToString(CultureInfo.InvariantCulture) + " " + PayloadType + " " +
            payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
        var signature = key.SignData(prefix.Concat(payload).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
            verificationMaterial = new
            {
                publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo())) }
            },
            dsseEnvelope = new
            {
                payloadType = PayloadType,
                payload = Convert.ToBase64String(payload),
                signatures = new[] { new { sig = Convert.ToBase64String(signature) } }
            }
        });
    }
}
```

- [x] Add this throwing scaffold inside the existing CosignBlobVerifier class solely to compile the red tests; leave its existing methods unchanged.

```csharp
    internal Task<byte[]> VerifyOciBundleAsync(string imageDigest, ReadOnlyMemory<byte> bundle,
        Cp6PinnedTrustKey key, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
```

- [x] Run the focused suite and inspect the TRX for all 26 expected NotImplementedException failures, zero skips and no compile errors.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CosignOciBundleTests --logger 'trx;LogFileName=oci-bundle-red.trx' --results-directory ../../artifacts/p10/oci-bundle-red
```

## Task 2: Implement the minimum behavior

- [x] Add OciBundlePayload.cs with the complete code below.

```csharp
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// The envelope and payload are third-party bytes, not CP6 canonical JSON.
// This shape check provides no authentication; only the real cosign process does.
internal static class OciBundlePayload
{
    internal const string PredicateType = "https://sigstore.dev/cosign/sign/v1";

    internal static byte[] Read(ReadOnlyMemory<byte> bundle)
    {
        try
        {
            // Reuse the bounded duplicate-rejecting JSON reader without reserializing any signed byte.
            var root = GitHubApiJson.Parse(bundle);
            if (root.GetProperty("mediaType").GetString() != "application/vnd.dev.sigstore.bundle.v0.3+json" ||
                root.TryGetProperty("messageSignature", out _)) throw new FormatException();
            var envelope = root.GetProperty("dsseEnvelope");
            var signatures = envelope.GetProperty("signatures");
            if (envelope.GetProperty("payloadType").GetString() != "application/vnd.in-toto+json" ||
                signatures.ValueKind != JsonValueKind.Array || signatures.GetArrayLength() != 1)
                throw new FormatException();
            var encoded = envelope.GetProperty("payload").GetString()!;
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length is < 1 or > 49152 || Convert.ToBase64String(bytes) != encoded)
                throw new FormatException();
            _ = GitHubApiJson.Parse(bytes);
            return bytes;
        }
        catch (Exception)
        {
            throw new Cp6ReleaseContractException("cosign-bundle", "OCI bundle bytes violate the bounded DSSE profile.");
        }
    }
}
```

- [x] Replace CosignBlobVerifier.cs with the following complete implementation. Preserve the public blob entry's behavior, binary hashes, environment isolation, timeout and output bounds.

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Cryptography only: the caller must obtain the key from the pinned current trust policy.
// A successful blob signature is not candidate, workflow, storage or package acceptance.
public sealed class CosignBlobVerifier(string executablePath)
{
    private const int MaximumOutputBytes = 64 * 1024;
    private const string WindowsSha256 = "9fe59be0eca1271873ce019061335eb1ac419b7059202e797828467ddabe33be";
    private const string LinuxSha256 = "4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71";

    public Task VerifyAsync(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> bundle,
        Cp6PinnedTrustKey key, CancellationToken cancellationToken = default) =>
        VerifyCoreAsync(payload, bundle, key, null, cancellationToken);

    // Authenticates a cosign v3 image-signature bundle against an exact digest.
    // It does not prove registry availability, build identity, current key policy or candidate acceptance.
    internal async Task<byte[]> VerifyOciBundleAsync(string imageDigest, ReadOnlyMemory<byte> bundle,
        Cp6PinnedTrustKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (imageDigest is null || imageDigest.Length != 71 ||
            !imageDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            imageDigest[7..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("cosign-digest");
        if (key.Purpose != "oci") throw Error("cosign-key");
        if (bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("cosign-input");
        var bytes = bundle.ToArray();
        // Decode once before verification to reject duplicate JSON members. Never reparse the envelope afterwards.
        var payload = OciBundlePayload.Read(bytes);
        await VerifyCoreAsync(ReadOnlyMemory<byte>.Empty, bytes, key, imageDigest[7..], cancellationToken);
        return payload; // Returned only after the actual cosign signature, digest and predicate checks succeed.
    }

    private async Task VerifyCoreAsync(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> bundle,
        Cp6PinnedTrustKey key, string? imageDigest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((imageDigest is null && payload.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) ||
            bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("cosign-input");
        var payloadBytes = payload.ToArray();
        var bundleBytes = bundle.ToArray();
        var publicKeyBytes = ValidatePublicKey(key);
        using var executable = await OpenPinnedExecutableAsync(cancellationToken);
        string? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("cp6-p10-cosign-verify-").FullName;
            var payloadPath = imageDigest is null ? CreateFile(directory, "payload.json", payloadBytes) : null;
            var bundlePath = CreateFile(directory, "bundle.json", bundleBytes);
            var keyPath = CreateFile(directory, "public.pem", publicKeyBytes);
            var start = new ProcessStartInfo(executable.Name)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = directory
            };
            start.Environment.Clear();
            foreach (var name in new[] { "SystemRoot", "WINDIR" })
                if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
            foreach (var name in new[] { "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR" })
                start.Environment[name] = directory;
            var arguments = imageDigest is null ? new[]
            {
                "verify-blob", "--key", keyPath, "--bundle", bundlePath,
                "--offline=true", "--new-bundle-format=true", "--insecure-ignore-tlog=true", payloadPath!
            } : new[]
            {
                "verify-blob-attestation", "--key", keyPath, "--bundle", bundlePath,
                "--new-bundle-format=true", "--insecure-ignore-tlog=true", "--check-claims=true",
                "--digest", imageDigest, "--digestAlg", "sha256", "--type", OciBundlePayload.PredicateType
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw Error("cosign-process");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await Task.WhenAll(
                    process.WaitForExitAsync(timeout.Token),
                    DrainAsync(process.StandardOutput.BaseStream, process, timeout.Token),
                    DrainAsync(process.StandardError.BaseStream, process, timeout.Token));
                if (process.ExitCode != 0) throw Error("cosign-signature");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Error("cosign-timeout");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Error("cosign-process"); }
        finally
        {
            if (directory is not null)
            {
                try
                {
                    // No recursive removal or path supplied by a document or caller.
                    foreach (var name in new[] { "payload.json", "bundle.json", "public.pem" })
                        File.Delete(Path.Combine(directory, name));
                    Directory.Delete(directory);
                }
                catch (Exception) { throw Error("cosign-cleanup"); }
            }
        }
    }

    private async Task<FileStream> OpenPinnedExecutableAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = null;
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) ||
                !Path.IsPathFullyQualified(executablePath) ||
                File.GetAttributes(executablePath).HasFlag(FileAttributes.ReparsePoint))
                throw Error("cosign-tool");
            stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var expected = OperatingSystem.IsWindows() ? WindowsSha256 : LinuxSha256;
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw Error("cosign-tool");
            return stream; // Keep the validated file handle open until the child exits.
        }
        catch (OperationCanceledException) { stream?.Dispose(); throw; }
        catch (Exception) { stream?.Dispose(); throw Error("cosign-tool"); }
    }

    private static byte[] ValidatePublicKey(Cp6PinnedTrustKey key)
    {
        try
        {
            if (key.Purpose is not ("oci" or "candidate-locator") || key.PublicKey.Length > 4096)
                throw Error("cosign-key");
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(key.PublicKey);
            var spki = verifier.ExportSubjectPublicKeyInfo();
            if (verifier.KeySize != 256 || verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                key.PublicKey != PemEncoding.WriteString("PUBLIC KEY", spki) ||
                key.KeyId != "sha256:" + Cp6DeterministicJson.Sha256Hex(spki)) throw Error("cosign-key");
            return Encoding.UTF8.GetBytes(key.PublicKey);
        }
        catch (Exception) { throw Error("cosign-key"); }
    }

    private static string CreateFile(string directory, string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        return path;
    }

    private static async Task DrainAsync(Stream stream, Process process, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return;
            total += count;
            if (total > MaximumOutputBytes)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw Error("cosign-output");
            }
        }
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Pinned signature verification did not complete successfully.");
}
```

- [x] Run the new and existing cosign suites, then all tests and formatting with the existing actual formal package and read credentials.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~CosignOciBundleTests|FullyQualifiedName~CosignBlobVerifierTests'
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: new 26 cases plus prior 18 cosign cases pass; total 758 pass, none skipped. Fresh actual cosign must reject changed signatures, keys, digests and predicates; correct signed raw payloads are returned unchanged.

## Task 3: Review and checkpoint

- [x] Compare all three complete-code blocks with their final files, review the complete four-file diff, and confirm public trust bytes, package locks, workflow files, runtime code and remote systems are unchanged.
- [x] Record observed results, stage only these files and commit normally.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-oci-bundle-verification.md tools/p10/ReleaseVerifier/CosignBlobVerifier.cs tools/p10/ReleaseVerifier/OciBundlePayload.cs tools/p10/ReleaseVerifier.Tests/CosignOciBundleTests.cs
git diff --cached --check
git commit -m "feat(p10): verify OCI digest bundles with pinned cosign"
```

- [ ] Complete actual S06 image/workflow/evidence/publication/audit gates before claiming P10 complete.

## Observed results (2026-09-08 UTC)

- Red TRX: 26 failures, all 26 caused by the throwing scaffold; zero unexpected failures, zero skipped/not-executed tests.
- Green: all 44 new/existing cosign tests passed using the official checksum-pinned executable. Correct DSSE authenticated and returned byte-identical payload; signature, key, digest and predicate substitutions failed in actual cosign.
- Complete Release suite: 758/758 passed, zero skips, no build warnings/errors; full format verification exited 0.
- Exact plan parity and the complete four-file scope were reviewed; hygiene and diff checks passed. Public blob behavior and all existing trust/package/workflow/R2 pins remain unchanged. Remote main remains at 6f9d09f4e3b1627a25ec7859b748eba8cd66f621. No formal OCI, R2 or deployment acceptance is claimed.
