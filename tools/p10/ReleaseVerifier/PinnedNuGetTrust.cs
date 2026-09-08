using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class PinnedNuGetTrust
{
    public static Cp6PinnedNuGetTrustPolicy Load() =>
        Parse(Resource("CP6.P10.pinned-nuget-trust-store.v1.json", Cp6DeterministicJson.MaximumBytes),
            Resource("CP6.P10.formal-author.cer", 65536));

    public static Cp6PinnedNuGetTrustPolicy Parse(ReadOnlySpan<byte> policy, ReadOnlySpan<byte> certificate)
    {
        Require(policy.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            certificate.Length is > 0 and <= 65536, "nuget-trust-size");
        Require(Cp6DeterministicJson.Sha256Hex(policy) == S06ReleaseIdentity.NuGetTrustHash, "nuget-trust-hash");
        Require(Cp6DeterministicJson.Sha256Hex(certificate) == S06ReleaseIdentity.Signer, "nuget-certificate-hash");
        return Cp6PinnedNuGetTrustPolicy.Parse(policy, new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
        {
            ["certificates/" + S06ReleaseIdentity.Signer + ".cer"] = certificate.ToArray()
        });
    }

    private static byte[] Resource(string name, int maximumBytes)
    {
        using var stream = typeof(PinnedNuGetTrust).Assembly.GetManifestResourceStream(name)
            ?? throw Error("nuget-trust-resource");
        using var reader = new BinaryReader(stream);
        return reader.ReadBytes(maximumBytes + 1);
    }

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Formal NuGet verification violates the independently pinned policy.");
}
