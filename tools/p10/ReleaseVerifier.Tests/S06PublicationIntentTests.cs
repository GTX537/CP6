using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Payload copying and rejection vectors only. They cannot produce an authenticated current-run intent.
public sealed class S06PublicationIntentTests
{
    [Fact]
    public void Intent_payloads_are_exact_raw_owned_copies()
    {
        var locator = new byte[] { 1, 2, 3 };
        var bundle = new byte[] { 4, 5, 6 };
        var artifact = Archive(new() { ["locator.json"] = locator, ["locator.sigstore.json"] = bundle });
        locator[0] = 99;
        var first = S06PublicationIntent.ReadPayloads(artifact);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Locator);
        Assert.Equal(bundle, first.Bundle);
        first.Locator[0] = 88;
        first.Bundle[0] = 77;
        var second = S06PublicationIntent.ReadPayloads(artifact);
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Locator);
        Assert.Equal(new byte[] { 4, 5, 6 }, second.Bundle);
    }

    [Theory]
    [InlineData("missing-locator")]
    [InlineData("missing-bundle")]
    [InlineData("extra")]
    [InlineData("case")]
    [InlineData("discovery-name")]
    public void Only_the_two_fixed_job_artifact_names_are_allowed(string mutation)
    {
        var files = Files();
        if (mutation == "missing-locator") files.Remove("locator.json");
        if (mutation == "missing-bundle") files.Remove("locator.sigstore.json");
        if (mutation == "extra") files.Add("artifact-index.json", "{}"u8.ToArray());
        if (mutation == "case")
        {
            files.Remove("locator.json");
            files.Add("Locator.json", "{}"u8.ToArray());
        }
        if (mutation == "discovery-name")
        {
            files.Remove("locator.json");
            files.Add("candidate-locator.v1.json", "{}"u8.ToArray());
        }
        Assert.Equal("s06-intent-files", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadPayloads(Archive(files))).Code);
    }

    [Theory]
    [InlineData("locator.json", 0)]
    [InlineData("locator.sigstore.json", 0)]
    [InlineData("locator.json", 4194305)]
    [InlineData("locator.sigstore.json", 4194305)]
    public void Payload_sizes_remain_bounded(string name, int length)
    {
        var files = Files();
        files[name] = new byte[length];
        Assert.Equal("s06-intent-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadPayloads(Archive(files))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_artifact_IDs_fail_before_capturing_a_current_workflow(long id) =>
        Assert.Equal("s06-intent-id", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadAsync("v0.10.1-p10.1", id, new("missing-cosign"), ""))).Code);

    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    public async Task Unsafe_tags_fail_before_capturing_a_current_workflow(string tag) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadAsync(tag, 1, new("missing-cosign"), ""));

    [Fact]
    public async Task Precancelled_intent_read_performs_no_external_work()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06PublicationIntent.ReadAsync("v0.10.1-p10.1", 1, new("missing-cosign"), "", cancellation.Token));
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Files() => new()
    {
        ["locator.json"] = "{}"u8.ToArray(),
        ["locator.sigstore.json"] = "{}"u8.ToArray()
    };

    private static DownloadedWorkflowArtifact Archive(Dictionary<string, ReadOnlyMemory<byte>> files) =>
        new(new(1, "unit-vector-only", "sha256:" + new string('a', 64), 1,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)), files, DateTimeOffset.UnixEpoch);
}
