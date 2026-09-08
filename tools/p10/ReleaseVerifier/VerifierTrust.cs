using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class VerifierTrust
{
    private const string ExpectedSha256 = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";

    public static Cp6PinnedTrustPolicy Load()
    {
        using var stream = typeof(VerifierTrust).Assembly.GetManifestResourceStream("CP6.P10.pinned-trust-store.v1.json")
            ?? throw Error("trust-bootstrap-resource");
        using var reader = new BinaryReader(stream);
        return Parse(reader.ReadBytes(Cp6DeterministicJson.MaximumBytes + 1));
    }

    public static Cp6PinnedTrustPolicy Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("trust-bootstrap-size");
        if (!string.Equals(Cp6DeterministicJson.Sha256Hex(bytes), ExpectedSha256, StringComparison.Ordinal))
            throw Error("trust-bootstrap-hash");
        return Cp6PinnedTrustPolicy.Parse(bytes);
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Verifier trust does not match the independently compiled trust anchor.");
}
