using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Rejection boundaries only. Positive acceptance needs a real signed R2 Locator plus completed protected workflows.
public sealed class S06CandidateVerifierTests
{
    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    [InlineData("v0.10.1/other")]
    [InlineData("")]
    public async Task Normal_verification_rejects_unsafe_discovery_before_any_external_read(string tag) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => VerifiedPlatformCandidate.VerifyAsync(
            tag, "not-an-access-id", "not-a-secret", new("missing-cosign"), "not-a-token", "not-a-token"));

    [Fact]
    public async Task Normal_verification_has_no_anonymous_credential_fallback() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => VerifiedPlatformCandidate.VerifyAsync(
            "v0.10.1", "", "", new("missing-cosign"), "not-a-token", "not-a-token"));

    [Fact]
    public async Task Structural_inspection_cannot_substitute_for_the_real_payload_and_completed_producer_proofs()
    {
        var graph = Graph();
        Assert.False(graph.CandidateAccepted);
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06CandidateEvidence.VerifyAsync(graph, DateTimeOffset.UtcNow, new("missing-cosign"), "not-a-token", "not-a-token"));
        Assert.StartsWith("s06-attestation-", error.Code, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_future_publication_start_does_not_authorize_evidence_verification() =>
        Assert.Equal("s06-candidate-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06CandidateEvidence.VerifyAsync(Graph(), DateTimeOffset.UtcNow.AddDays(1),
                new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task Precancelled_normal_verification_performs_no_external_read()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifiedPlatformCandidate.VerifyAsync(
            "v0.10.1", "", "", new("missing-cosign"), "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_evidence_verification_performs_no_external_read()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06CandidateEvidence.VerifyAsync(
            Graph(), DateTimeOffset.UtcNow, new("missing-cosign"), "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_publication_confirmation_does_not_capture_or_predict_a_publisher()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06PublicationConfirmation.VerifyAsync(
            null!, new("missing-cosign"), "", "", cancellation.Token));
    }

    private static InspectedEvidenceGraph Graph()
    {
        var fixture = EvidenceGraphFixture.Build();
        return EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
    }

    private static CancellationTokenSource Cancelled()
    {
        var result = new CancellationTokenSource();
        result.Cancel();
        return result;
    }
}
