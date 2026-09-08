using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// These are rejection-boundary tests only. The fixture's unsigned envelopes cannot authenticate a candidate.
// Full positive orchestration must pass against the real protected S06 run and OCI artifact.
public sealed class S06PayloadProofTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, EvidenceGraphFixture.PublicSource);
    private static string Digest => "sha256:" + new string('a', 64);

    [Theory]
    [InlineData("missing", "s06-proof-set")]
    [InlineData("extra", "s06-proof-set")]
    [InlineData("empty", "s06-proof-size")]
    [InlineData("oversize", "s06-proof-size")]
    [InlineData("publication", "s06-proof-baseline")]
    [InlineData("provenance", "s06-proof-baseline")]
    [InlineData("nuget-trust", "s06-proof-baseline")]
    [InlineData("trust", "s06-proof-baseline")]
    public async Task Incomplete_or_unpinned_inputs_fail_before_external_proof_work(string mutation, string code)
    {
        var payloads = Payloads();
        if (mutation == "missing") payloads.Remove("ImageScan");
        if (mutation == "extra") payloads.Add("Optional", "{}"u8.ToArray());
        if (mutation == "empty") payloads["ImageScan"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversize") payloads["ImageScan"] = new byte[4194305];
        if (mutation == "publication") payloads["FormalPackagePublication"] = "{}"u8.ToArray();
        if (mutation == "provenance") payloads["PackageProvenance"] = "{}"u8.ToArray();
        if (mutation == "nuget-trust") payloads["NuGetTrustPolicy"] = "{}"u8.ToArray();
        if (mutation == "trust") payloads["TrustPolicy"] = "{}"u8.ToArray();
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(payloads));
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_structurally_consistent_assembled_artifact_does_not_authenticate_unsigned_evidence()
    {
        var payloads = Payloads();
        var artifact = S06ArtifactAssembly.Create(Producer, Digest, payloads);
        Assert.False(artifact.EvidenceAuthenticated);
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(payloads));
        Assert.StartsWith("s06-attestation-", error.Code, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task The_future_cannot_be_selected_as_the_evidence_cutoff() =>
        Assert.Equal("s06-proof-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PayloadProof.VerifyAsync(Producer, Digest, Payloads(), DateTimeOffset.UtcNow.AddDays(1),
                new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task A_publication_job_cannot_claim_to_be_the_validation_producer() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PayloadProof.VerifyAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                Digest, Payloads(), DateTimeOffset.UtcNow, new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task A_precancelled_proof_performs_no_external_reads()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06PayloadProof.VerifyAsync(Producer, Digest, Payloads(), DateTimeOffset.UtcNow,
                new("missing-cosign"), "not-a-token", "not-a-token", cancellation.Token));
    }

    private static Task<S06PayloadProof> Verify(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> payloads) =>
        S06PayloadProof.VerifyAsync(Producer, Digest, payloads, DateTimeOffset.UtcNow,
            new("missing-cosign"), "not-a-token", "not-a-token");

    private static Dictionary<string, ReadOnlyMemory<byte>> Payloads()
    {
        var fixture = EvidenceGraphFixture.Build();
        return EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects).Evidence
            .ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
    }
}
