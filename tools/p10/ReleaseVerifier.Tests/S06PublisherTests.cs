using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Unpublished structural vectors only. No fake transport, signing authority or positive publication capability.
public sealed class S06PublisherTests
{
    [Fact]
    public void Object_preparation_preserves_all_23_graph_objects_and_places_the_candidate_last()
    {
        var fixture = EvidenceGraphFixture.Build();
        var graph = EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
        var candidate = new AssembledPlatformCandidate(fixture.Candidate, fixture.Objects, graph);
        var objects = S06PublicationObjects.Create(candidate);
        Assert.Equal(24, objects.Count);
        Assert.Equal(24, objects.Select(o => o.Reference.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(objects, item =>
        {
            Assert.Equal(item.Reference.ByteLength, item.Bytes.Length);
            Assert.Equal(item.Reference.Sha256, Cp6DeterministicJson.Sha256Hex(item.Bytes.Span));
        });
        foreach (var item in objects.Take(23)) Assert.Equal(fixture.Objects[item.Reference.Key].ToArray(), item.Bytes.ToArray());
        Assert.Equal(fixture.Candidate, objects[^1].Bytes.ToArray());
        Assert.Equal(Cp6ReleaseMediaTypes.PlatformReleaseCandidate, objects[^1].Reference.MediaType);
        Assert.False(graph.CandidateAccepted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("extra")]
    public void Object_preparation_reinspects_bytes_instead_of_trusting_an_old_structural_result(string mutation)
    {
        var fixture = EvidenceGraphFixture.Build();
        var graph = EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
        var key = fixture.Objects.Keys.First();
        if (mutation == "missing") fixture.Objects.Remove(key);
        if (mutation == "changed") fixture.Objects[key] = "{}"u8.ToArray();
        if (mutation == "extra") fixture.Objects["objects/unused"] = "{}"u8.ToArray();
        var candidate = new AssembledPlatformCandidate(fixture.Candidate, fixture.Objects, graph);
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Create(candidate));
    }

    [Fact]
    public void Unsigned_locator_binds_exact_candidate_time_bytes_fixed_signer_and_platform_lane()
    {
        var fixture = EvidenceGraphFixture.Build();
        var bytes = S06PublicationObjects.Locator("v0.10.1-p10.1", fixture.Candidate);
        var root = GitHubApiJson.Parse(bytes);
        _ = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        Assert.Equal(bytes, Cp6DeterministicJson.Canonicalize(bytes));
        Assert.Equal(EvidenceGraphFixture.Created, S06InToto.Text(root, "createdAtUtc"));
        Assert.Equal(S06PublicationObjects.LocatorKeyId, S06InToto.Text(root, "signerKeyId"));
        Assert.Equal("PlatformReleaseCandidate", S06InToto.Text(root, "subjectKind"));
        var reference = ContentAddress.Parse(root.GetProperty("subject"));
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(fixture.Candidate), reference.Sha256);
        Assert.Equal(fixture.Candidate.Length, reference.ByteLength);
        Assert.False(root.TryGetProperty("candidateAccepted", out _));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("expired-key-window")]
    [InlineData("package")]
    [InlineData("deployable")]
    public void Locator_preparation_rejects_unpinned_or_out_of_time_candidate_bytes(string mutation)
    {
        var fixture = EvidenceGraphFixture.Build(candidateChange: root =>
        {
            if (mutation == "future") root["createdAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
            if (mutation == "expired-key-window") root["createdAtUtc"] = "2026-01-01T00:00:00.000Z";
            if (mutation == "package") root["packages"]![0]!["version"] = "0.10.2";
            if (mutation == "deployable") root["deployable"] = true;
        });
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Locator("v0.10.1-p10.1", fixture.Candidate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../bad")]
    [InlineData("v0.10.1\n")]
    public void Locator_tag_is_checked_before_document_processing(string tag) =>
        Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.Locator(tag, ReadOnlyMemory<byte>.Empty));

    [Fact]
    public void Identical_readback_is_only_a_byte_comparison() =>
        S06PublicationObjects.RequireIdentical("{\"a\":1}"u8.ToArray(), "{\"a\":1}"u8.ToArray());

    [Theory]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("empty-expected")]
    [InlineData("oversized-expected")]
    public void Missing_or_changed_readback_never_counts_as_idempotent_success(string mutation)
    {
        var expected = mutation == "empty-expected" ? Array.Empty<byte>() :
            mutation == "oversized-expected" ? new byte[4194305] : "{\"a\":1}"u8.ToArray();
        byte[]? actual = mutation == "missing" ? null :
            mutation == "different" ? "{\"a\":2}"u8.ToArray() : expected;
        var error = Assert.Throws<Cp6ReleaseContractException>(() => S06PublicationObjects.RequireIdentical(expected, actual));
        Assert.Equal("publication-readback-conflict", error.Code);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 2147483648, 1)]
    [InlineData(1, 1, 0)]
    public async Task Invalid_validation_selection_fails_before_any_remote_read_or_write(long run, long attempt, long artifact) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.PrepareAsync("v0.10.1-p10.1", run, attempt, artifact, "", null!, "", "", "", ""));

    [Fact]
    public async Task Invalid_commit_selection_cannot_start_a_child_or_write_a_locator() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.CommitAsync("v0.10.1-p10.1", 0, "", "", "", "", "", "", ""));

    [Fact]
    public async Task Bundle_step_rejects_missing_local_handoff_before_network() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06Publisher.StoreBundleAsync("v0.10.1-p10.1", "", "", null!, "", "", "", "", "", ""));

    [Theory]
    [InlineData("prepare")]
    [InlineData("bundle")]
    [InlineData("commit")]
    public async Task Precancelled_publication_never_touches_local_or_remote_storage(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => phase switch
        {
            "prepare" => S06Publisher.PrepareAsync("", 0, 0, 0, "", null!, "", "", "", "", cancellation.Token),
            "bundle" => S06Publisher.StoreBundleAsync("", "", "", null!, "", "", "", "", "", "", cancellation.Token),
            _ => S06Publisher.CommitAsync("", 0, "", "", "", "", "", "", "", cancellation.Token)
        });
    }
}
