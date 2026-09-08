using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// This authenticates one Locator only. It does not accept the referenced candidate graph.
public sealed class AuthenticatedLocator
{
    private AuthenticatedLocator(string releaseTag, string sha256, string signerKeyId, int policyVersion,
        DateTimeOffset createdAtUtc, ContentAddress subject, Cp6CandidateLocatorKeys discoveryKeys)
    {
        ReleaseTag = releaseTag;
        Sha256 = sha256;
        SignerKeyId = signerKeyId;
        PolicyVersion = policyVersion;
        CreatedAtUtc = createdAtUtc;
        Subject = subject;
        DiscoveryKeys = discoveryKeys;
    }

    public string ReleaseTag { get; }
    public string Sha256 { get; }
    public string SignerKeyId { get; }
    public int PolicyVersion { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public ContentAddress Subject { get; }
    public Cp6CandidateLocatorKeys DiscoveryKeys { get; }

    public static Task<AuthenticatedLocator> AuthenticateAsync(string expectedReleaseTag,
        ReadOnlyMemory<byte> locator, ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default) =>
        AuthenticateWithPolicyAsync(VerifierTrust.Load(), DateTimeOffset.UtcNow,
            expectedReleaseTag, locator, bundle, verifier, cancellationToken);

    // Internal separation permits deterministic policy regression with real ephemeral signatures.
    // The public entry point cannot accept caller-supplied policy, evaluation time or verification success.
    internal static async Task<AuthenticatedLocator> AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy policy,
        DateTimeOffset evaluationUtc, string expectedReleaseTag, ReadOnlyMemory<byte> locator,
        ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedReleaseTag.Length is < 1 or > 128 ||
            expectedReleaseTag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw Error("locator-tag");
        var discoveryKeys = Cp6CandidateLocatorKeys.ForPlatformTag(expectedReleaseTag);
        if (locator.Length is < 1 or > Cp6DeterministicJson.MaximumBytes ||
            bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("locator-size");
        var bytes = locator.ToArray();
        var bundleBytes = bundle.ToArray();
        var canonical = Cp6DeterministicJson.Canonicalize(bytes);
        if (!bytes.AsSpan().SequenceEqual(canonical)) throw Error("non-canonical-json");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var keyId = Selector(root, "signerKeyId");
        var time = Selector(root, "createdAtUtc");
        if (!root.TryGetProperty("trustPolicyVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var policyVersion) ||
            policyVersion < 1 || !DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var signedAtUtc)) throw Error("locator-selector");
        if (signedAtUtc > evaluationUtc) throw Error("locator-time");
        var key = policy.RequireKey(keyId, "candidate-locator", policyVersion, signedAtUtc, evaluationUtc,
            Cp6ReleaseValidationMode.Current);

        // Do not inspect or follow the subject or any document-chosen location before this succeeds.
        await verifier.VerifyAsync(bytes, bundleBytes, key, cancellationToken);
        var validated = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        if (validated.SubjectKind != "PlatformReleaseCandidate") throw Error("locator-lane");
        if (!string.Equals(root.GetProperty("releaseTag").GetString(), expectedReleaseTag, StringComparison.Ordinal))
            throw Error("locator-tag");
        var subject = ContentAddress.Parse(root.GetProperty("subject"));
        _ = policy.RequireStorageAuthority(ContentAddress.StorageAuthority);
        return new(expectedReleaseTag, validated.Sha256, keyId, policyVersion, signedAtUtc, subject, discoveryKeys);
    }

    private static string Selector(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw Error("locator-selector");
        return value.GetString()!;
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Locator authentication violates the pinned Platform discovery policy.");
}
