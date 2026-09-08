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
    [InlineData(false)]
    [InlineData(true)]
    public void Terminal_newline_is_not_a_valid_content_address_file_name(bool parse)
    {
        Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            if (!parse) ContentAddress.Create(Bytes, Cp6ReleaseMediaTypes.InToto, "proof.json\n");
            else
            {
                var node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
                node["key"] = Address().Key + "\n";
                ContentAddress.Parse(JsonSerializer.SerializeToElement(node));
            }
        });
    }

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
