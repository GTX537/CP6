using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class GraphObjectDownloadsTests
{
    [Fact]
    public async Task Exact_reference_is_read_once_and_owned_copies_are_isolated()
    {
        var raw = "{}"u8.ToArray();
        var reference = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var reads = 0;
        var downloads = new GraphObjectDownloads((_, _) => { reads++; return Task.FromResult<byte[]?>(raw); });
        var first = await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None);
        first[0] ^= 1;
        raw[0] ^= 1;
        Assert.Equal("{}"u8.ToArray(), await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None));
        Assert.Equal(1, reads);
        var snapshot = downloads.SnapshotExcept("absent-candidate-key");
        Assert.Equal("{}"u8.ToArray(), snapshot[reference.Key].ToArray());
        Assert.Empty(downloads.SnapshotExcept(reference.Key));
    }

    [Fact]
    public async Task Wrong_expected_media_fails_before_the_byte_supplier()
    {
        var calls = 0;
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(null); });
        await Error("graph-download-media", () => downloads.ReadAsync(reference, Cp6ReleaseMediaTypes.Spdx, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("missing", "graph-download-missing")]
    [InlineData("length", "graph-download-size")]
    [InlineData("hash", "graph-download-hash")]
    public async Task Missing_changed_or_truncated_bytes_cannot_pass(string change, string code)
    {
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        byte[]? supplied = change == "missing" ? null : change == "length" ? "{"u8.ToArray() : "[]"u8.ToArray();
        var downloads = new GraphObjectDownloads((_, _) => Task.FromResult(supplied));
        await Error(code, () => downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_key_with_conflicting_signed_metadata_is_rejected_without_refetch()
    {
        var raw = "{}"u8.ToArray();
        var reference = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        _ = await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None);
        var node = JsonNode.Parse(reference.ToJson().GetRawText())!.AsObject();
        node["mediaType"] = Cp6ReleaseMediaTypes.Spdx;
        var changed = ContentAddress.Parse(JsonSerializer.SerializeToElement(node));
        await Error("graph-download-reference", () => downloads.ReadAsync(changed, changed.MediaType, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Object_count_is_reserved_before_the_next_external_read()
    {
        var raw = "{}"u8.ToArray();
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        for (var index = 0; index < 33; index++)
            _ = await downloads.ReadAsync(ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto,
                "proof-" + index + ".json"), Cp6ReleaseMediaTypes.InToto, CancellationToken.None);
        var extra = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "extra.json");
        await Error("graph-download-count", () => downloads.ReadAsync(extra, extra.MediaType, CancellationToken.None));
        Assert.Equal(33, calls);
    }

    [Fact]
    public async Task Aggregate_64MiB_limit_is_reserved_before_the_next_read()
    {
        var raw = new byte[Cp6DeterministicJson.MaximumBytes];
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        for (var index = 0; index < 16; index++)
            _ = await downloads.ReadAsync(ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto,
                "proof-" + index + ".json"), Cp6ReleaseMediaTypes.InToto, CancellationToken.None);
        var extra = ContentAddress.Create("x"u8, Cp6ReleaseMediaTypes.InToto, "extra.json");
        await Error("graph-download-budget", () => downloads.ReadAsync(extra, extra.MediaType, CancellationToken.None));
        Assert.Equal(16, calls);
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_prevents_any_byte_read()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(null); });
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloads.ReadAsync(reference, reference.MediaType, cancellation.Token));
        Assert.Equal(0, calls);
    }

    private static async Task Error(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);
}
