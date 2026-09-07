using System.Text;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class ContractInspectionTests
{
    private const string TrustHash = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";

    [Fact]
    public void Public_trust_bytes_match_the_pinned_hash_and_formal_contract()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
        var result = ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash);
        Assert.Equal(TrustHash, result.Sha256);
        Assert.False(result.CandidateAccepted);
        Assert.Null(result.Deployable);
    }

    [Fact]
    public void Changed_bytes_fail_the_independent_expected_hash()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
        bytes[0] = (byte)' ';
        Assert.Equal("input-hash", Assert.Throws<Cp6ReleaseContractException>(() =>
            ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash)).Code);
    }

    [Fact]
    public void Matching_hash_does_not_authorize_an_unsupported_contract()
    {
        var bytes = "{}"u8.ToArray();
        Assert.Equal("inspection-schema", Assert.Throws<Cp6ReleaseContractException>(() =>
            ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.SystemManifest,
                Cp6DeterministicJson.Sha256Hex(bytes))).Code);
    }

    [Fact]
    public void Canonicalization_rejects_duplicate_members()
    {
        Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}")));
    }

    [Fact]
    public void Empty_or_oversize_inputs_fail_before_contract_parsing()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[Cp6DeterministicJson.MaximumBytes + 1] })
            Assert.Equal("input-size", Assert.Throws<Cp6ReleaseContractException>(() =>
                ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash)).Code);
    }
}
