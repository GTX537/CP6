# P10 S06 Content-Addressed I/O Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` inline, as already selected by the Owner. Execute the checkboxes in order; do not delegate.

**Goal:** Add tested content-address and bounded-byte readers for the S06 verifier and publisher without expanding the formal package's Schema ownership.

**Architecture:** This subplan supplies the byte boundary used by the evidence graph and R2 transport. Object references select only the fixed storage authority, allowed package-owned media types and a SHA-256-derived key; reads require exact metadata and raw-byte identity. The component performs no network request, signature verification, candidate acceptance or publication.

**Tech Stack:** Existing .NET 8.0.424, formal `CP6.Platform.Release 0.10.1`, xUnit, built-in stream/JSON APIs.

---

## Scope and sequencing

The approved parent is Platform `3ff27e26962dcfd722887afb80a4306010dd9ee1` / `docs/superpowers/specs/2026-09-01-p10-release-governance-design.md`, with its pinned-self-signed amendment and public CP6 distinct-key prerequisites. This is one independently testable component of that design, not the complete S06 plan or final acceptance.

Baseline: public task branch `codex/p10-s06-platform-candidate@7fab7156b87c2beae6a3a70c724cedad9a00cd79`, two local foundation/erratum commits above unchanged remote main `6f9d09f4e3b1627a25ec7859b748eba8cd66f621`. The CRM CI task is delivered through #48/#49; its exact-main light run `34175788024` succeeded. This subplan returns to public CP6 only.

Third-party SPDX/SARIF/in-toto bytes must retain their raw hash; they are not silently reserialized under the CP6 integer-only canonical profile. CP6-authored control documents still require the released package's canonical/schema validators at the graph boundary. A metadata/hash success here is not a signature or release-gate success.

## File map

| File | Responsibility |
| --- | --- |
| `tools/p10/ReleaseVerifier/ContentAddressedObjects.cs` | Immutable checked reference, safe key derivation, bounded read and independent metadata/hash enforcement |
| `tools/p10/ReleaseVerifier.Tests/ContentAddressedObjectsTests.cs` | Real byte/stream positive and negative tests; no remote acceptance fixtures |
| This plan | Auditable TDD and local checkpoint |

No existing R2 workflow, application project, Platform source/Schema, production path, trust instance, package lock or historical evidence changes.

## Task 1: Write the byte-boundary tests

- [x] Create `tools/p10/ReleaseVerifier.Tests/ContentAddressedObjectsTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// These are byte/transport regression fixtures; they are not candidate acceptance evidence.
public sealed class ContentAddressedObjectsTests
{
    private static readonly byte[] Bytes = "{\"source\":\"unchanged\"}"u8.ToArray();
    private static ContentAddress Address() => ContentAddress.Create(Bytes, Cp6ReleaseMediaTypes.InToto, "proof.json");

    [Fact]
    public void Content_address_round_trip_preserves_the_raw_byte_identity()
    {
        var address = Address();
        var parsed = ContentAddress.Parse(address.ToJson());
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(Bytes), parsed.Sha256);
        Assert.Equal($"objects/sha256/{parsed.Sha256[..2]}/{parsed.Sha256}/proof.json", parsed.Key);
        Assert.Equal(Bytes.Length, parsed.ByteLength);
        Assert.Equal(Cp6ReleaseMediaTypes.InToto, parsed.MediaType);
    }

    [Theory]
    [InlineData("storageAuthority", "other")]
    [InlineData("sha256", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sha256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaz")]
    [InlineData("mediaType", "application/json")]
    [InlineData("mediaType", "application/vnd.in-toto+json; charset=utf-8")]
    [InlineData("key", "https://untrusted.invalid/proof.json")]
    [InlineData("key", "objects/sha256/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/proof.json")]
    [InlineData("key", "candidates/platform/v0.10.1/candidate-locator.v1.json")]
    public void Changed_reference_fields_are_rejected(string field, string changed)
    {
        var node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
        node[field] = changed;
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement(node)));
    }

    [Theory]
    [InlineData("../proof.json")]
    [InlineData("a/proof.json")]
    [InlineData("a\\proof.json")]
    [InlineData("proof.json?token=private")]
    [InlineData("proof.json#fragment")]
    [InlineData("Proof.json")]
    [InlineData("proof.txt")]
    [InlineData("proof..json")]
    public void Unsafe_publication_names_are_rejected(string name) =>
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Create(Bytes, Cp6ReleaseMediaTypes.InToto, name));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4194305)]
    public void Invalid_declared_lengths_are_rejected(int length)
    {
        var node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
        node["byteLength"] = length;
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement(node)));
    }

    [Fact]
    public void Missing_unknown_duplicate_and_wrong_kind_fields_are_rejected()
    {
        var node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
        node.Remove("sha256");
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement(node)));
        node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
        node["extra"] = "untrusted";
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement(node)));
        using var duplicate = JsonDocument.Parse(Address().ToJson().GetRawText().Replace("\"key\":", "\"byteLength\":1,\"key\":", StringComparison.Ordinal));
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(duplicate.RootElement));
        node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
        node["byteLength"] = 1.5;
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement(node)));
        Assert.Throws<Cp6ReleaseContractException>(() => ContentAddress.Parse(JsonSerializer.SerializeToElement("not-an-object")));
    }

    [Fact]
    public async Task Exact_metadata_and_bytes_pass_without_reserializing_third_party_json()
    {
        byte[] raw = "{ \"score\": 9.8 }\n"u8.ToArray();
        var address = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.Sarif, "scan.json");
        using var stream = new MemoryStream(raw);
        Assert.Equal(raw, await BoundedObjectReader.ReadCheckedAsync(stream, address, address.MediaType, raw.Length));
    }

    [Theory]
    [InlineData(null, 22L)]
    [InlineData("application/json", 22L)]
    [InlineData("application/vnd.in-toto+json", null)]
    [InlineData("application/vnd.in-toto+json", 0L)]
    [InlineData("application/vnd.in-toto+json", 999L)]
    public async Task Metadata_mismatch_stops_before_reading(string? media, long? length)
    {
        using var stream = new MemoryStream(Bytes);
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            BoundedObjectReader.ReadCheckedAsync(stream, Address(), media, length));
        Assert.Equal("object-metadata", error.Code);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Same_length_changed_bytes_fail_the_independent_hash()
    {
        var changed = Bytes.ToArray();
        changed[2] ^= 1;
        using var stream = new MemoryStream(changed);
        Assert.Equal("object-hash", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            BoundedObjectReader.ReadCheckedAsync(stream, Address(), Address().MediaType, Bytes.Length))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Actual_length_mismatch_is_not_accepted_as_a_hash_only_success(int length)
    {
        using var stream = new MemoryStream(new byte[length]);
        Assert.Equal("object-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            BoundedObjectReader.ReadCheckedAsync(stream, Address(), Address().MediaType, Bytes.Length))).Code);
        Assert.True(stream.Position <= Bytes.Length + 1);
    }

    [Fact]
    public async Task Maximum_object_is_allowed_but_read_stops_at_maximum_plus_one()
    {
        using var exact = new MemoryStream(new byte[Cp6DeterministicJson.MaximumBytes]);
        Assert.Equal(Cp6DeterministicJson.MaximumBytes, (await BoundedObjectReader.ReadAsync(exact)).Length);
        using var oversize = new MemoryStream(new byte[Cp6DeterministicJson.MaximumBytes + 64]);
        Assert.Equal("object-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            BoundedObjectReader.ReadAsync(oversize))).Code);
        Assert.Equal(Cp6DeterministicJson.MaximumBytes + 1, oversize.Position);
    }

    [Fact]
    public async Task Cancellation_and_invalid_limits_do_not_consume_the_stream()
    {
        using var stream = new MemoryStream(Bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedObjectReader.ReadAsync(stream, cancellationToken: cancellation.Token));
        foreach (var limit in new[] { 0, -1, Cp6DeterministicJson.MaximumBytes + 1 })
            Assert.Equal("object-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                BoundedObjectReader.ReadAsync(stream, limit))).Code);
        Assert.Equal(0, stream.Position);
    }
}
```

- [x] Create this temporary, buildable throwing scaffold at `tools/p10/ReleaseVerifier/ContentAddressedObjects.cs` solely to observe assertion failures. It is not a passing implementation and must not remain in the checkpoint:

```csharp
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public sealed class ContentAddress
{
    public string Key => throw new NotImplementedException();
    public string MediaType => throw new NotImplementedException();
    public string Sha256 => throw new NotImplementedException();
    public int ByteLength => throw new NotImplementedException();
    public static ContentAddress Parse(JsonElement value) => throw new NotImplementedException();
    public static ContentAddress Create(ReadOnlySpan<byte> bytes, string mediaType, string fileName) => throw new NotImplementedException();
    public JsonElement ToJson() => throw new NotImplementedException();
}
public static class BoundedObjectReader
{
    public static Task<byte[]> ReadAsync(Stream stream, int maximumBytes = Cp6DeterministicJson.MaximumBytes,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public static Task<byte[]> ReadCheckedAsync(Stream stream, ContentAddress expected, string? actualMediaType,
        long? actualContentLength, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
```

- [x] Run from `tools/p10`:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --filter FullyQualifiedName~ContentAddressedObjectsTests
```

Expected: the new cases fail because the feature throws `NotImplementedException`; compiler/environment failures do not establish the behavioral RED.

## Task 2: Implement the reference and stream adapter

- [x] Replace the throwing scaffold with this complete implementation:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// A CP6 I/O adapter over the package-owned object-reference contract, not a Schema copy.
public sealed class ContentAddress
{
    private static readonly string[] Fields = ["byteLength", "key", "mediaType", "sha256", "storageAuthority"];
    private static readonly Regex FileNamePattern = new(
        @"^[a-z0-9][a-z0-9.-]{0,127}\.json$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private ContentAddress(string key, string mediaType, string sha256, int byteLength)
    {
        Key = key;
        MediaType = mediaType;
        Sha256 = sha256;
        ByteLength = byteLength;
    }

    public const string StorageAuthority = "cp6-release-r2-v1";
    public string Key { get; }
    public string MediaType { get; }
    public string Sha256 { get; }
    public int ByteLength { get; }

    public static ContentAddress Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(Fields, StringComparer.Ordinal))
            throw Error("object-reference");
        var authority = ReadString(value, "storageAuthority");
        var key = ReadString(value, "key");
        var media = ReadString(value, "mediaType");
        var hash = ReadString(value, "sha256");
        var length = value.GetProperty("byteLength");
        if (authority != StorageAuthority || hash.Length != 64 ||
            hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            !Cp6ReleaseMediaTypes.All.Contains(media, StringComparer.Ordinal) ||
            length.ValueKind != JsonValueKind.Number || !length.TryGetInt32(out var count) ||
            count is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw Error("object-reference");
        var prefix = $"objects/sha256/{hash[..2]}/{hash}/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || !IsFileName(key[prefix.Length..]))
            throw Error("object-key-binding");
        return new(key, media, hash, count);
    }

    public static ContentAddress Create(ReadOnlySpan<byte> bytes, string mediaType, string fileName)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes ||
            !Cp6ReleaseMediaTypes.All.Contains(mediaType, StringComparer.Ordinal) || !IsFileName(fileName))
            throw Error("object-reference");
        var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        return new($"objects/sha256/{hash[..2]}/{hash}/{fileName}", mediaType, hash, bytes.Length);
    }

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new
    {
        storageAuthority = StorageAuthority,
        key = Key,
        mediaType = MediaType,
        sha256 = Sha256,
        byteLength = ByteLength
    });

    private static bool IsFileName(string value) =>
        value.Length <= 133 && !value.Contains("..", StringComparison.Ordinal) && FileNamePattern.IsMatch(value);

    private static string ReadString(JsonElement value, string name)
    {
        var field = value.GetProperty(name);
        return field.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(field.GetString())
            ? field.GetString()!
            : throw Error("object-reference");
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Object reference violates the pinned I/O policy.");
}

public static class BoundedObjectReader
{
    public static async Task<byte[]> ReadAsync(Stream stream, int maximumBytes = Cp6DeterministicJson.MaximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (maximumBytes is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("object-size");
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[maximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == 0 || count > maximumBytes) throw Error("object-size");
        return buffer.AsSpan(0, count).ToArray();
    }

    public static async Task<byte[]> ReadCheckedAsync(Stream stream, ContentAddress expected,
        string? actualMediaType, long? actualContentLength, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(expected.MediaType, actualMediaType, StringComparison.Ordinal) ||
            actualContentLength != expected.ByteLength)
            throw Error("object-metadata");
        var bytes = await ReadAsync(stream, expected.ByteLength, cancellationToken);
        if (bytes.Length != expected.ByteLength) throw Error("object-size");
        if (!string.Equals(Cp6DeterministicJson.Sha256Hex(bytes), expected.Sha256, StringComparison.Ordinal))
            throw Error("object-hash");
        return bytes;
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Remote object bytes violate the pinned I/O policy.");
}
```

- [x] Repeat the focused test command. Expect every new case to pass with zero skips. Then run the complete existing verifier suite, Release build and formatting check:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: new cases and all 14 foundation cases pass; build has zero errors/warnings; formatting is clean. If formatting needs mechanical correction, format and repeat both checks. No NuGet restore or package rewrite is needed because dependencies did not change.

## Task 3: Review and checkpoint

- [x] Review the complete three-file diff, run `git diff --check`, verify no trust/lock/production/historical payload changed, and scan added content for private keys, credentials or machine-specific paths.
- [ ] Stage these exact paths and commit normally:

```powershell
git add -- tools/p10/ReleaseVerifier/ContentAddressedObjects.cs tools/p10/ReleaseVerifier.Tests/ContentAddressedObjectsTests.cs docs/superpowers/plans/2026-09-07-p10-s06-content-addressed-io.md
git commit -m "feat(p10): enforce content-addressed object byte boundaries"
```

This is a local S06 checkpoint, not an isolated completion PR. The already approved S06 outcome still requires package/RFC3161 verification, original CRM evidence bindings, OCI signatures/SBOM/scans, full graph and current trust, two completed-workflow phases, create-only R2 transport, clean pre/post verification and final cross-repository audit. Those components get their own executable subplans before their code changes. No additional execution-mode or already-approved OCI-choice question is required.

## Self-review

Coverage includes authority/media/hash/key binding, exact fields and types, safe filenames, exact metadata before reading, empty/truncated/oversize bodies, same-length tampering, cancellation and raw third-party byte preservation. Tests exercise actual reader behavior rather than mocking its result. References are immutable after validation. All code and test types used in this subplan are defined above; no acceptance or cloud-write capability is introduced.

## Local checkpoint (2026-09-08 UTC)

- Selected the existing exact SDK 8.0.424 after the workstation's default .NET 10 host correctly refused the pinned project. The SDK-selection error was not counted as a behavioral RED.
- Corrected four xUnit data literals to Int64 values for a nullable Int64 parameter. With the buildable throwing scaffold, all 35 cases then failed on the missing implementation, without argument-binding or compilation errors. The scaffold was replaced completely.
- Focused Debug: 35/35 passed, zero skips. Full Release: 49/49 passed, zero skips, including the 14 foundation cases. Formatting verification passed. The source and tests match their complete plan blocks.
- Reviewed the new adapter and all test paths. Trust, package locks, application and legacy release paths are unchanged. No network request, image publication, R2 operation or candidate acceptance was performed by this component.
