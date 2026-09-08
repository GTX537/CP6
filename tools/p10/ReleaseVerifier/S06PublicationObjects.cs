using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Pure public-byte preparation and comparison. None of these methods authenticates evidence or permits a write.
internal static class S06PublicationObjects
{
    internal const string LocatorKeyId = "sha256:9c0fd05b3159651cc2e9138555f32387988c6961889ee00211139e710f1febaa";

    internal static IReadOnlyList<S06PublicationObject> Create(AssembledPlatformCandidate candidate)
    {
        var bytes = candidate.CopyBytes();
        var objects = candidate.CopyObjects();
        var graph = EvidenceGraphInspection.Inspect(bytes, objects);
        var root = graph.Candidate;
        var references = root.GetProperty("evidence").EnumerateArray().Select(ContentAddress.Parse)
            .Concat(graph.Evidence.Values.Select(e => e.Payload))
            .Append(ContentAddress.Parse(root.GetProperty("releaseGateResult"))).ToArray();
        Require(references.Length == 23 && objects.Count == 23 &&
            references.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() == 23, "publication-object-set");
        var result = new List<S06PublicationObject>();
        foreach (var reference in references.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            Require(objects.TryGetValue(reference.Key, out var payload) &&
                payload.Length == reference.ByteLength &&
                Cp6DeterministicJson.Sha256Hex(payload.Span) == reference.Sha256, "publication-object-binding");
            result.Add(new(reference, payload.ToArray()));
        }
        result.Add(new(ContentAddress.Create(bytes, Cp6ReleaseMediaTypes.PlatformReleaseCandidate,
            "platform-release-candidate.v1.json"), bytes));
        return result.AsReadOnly();
    }

    internal static byte[] Locator(string releaseTag, ReadOnlyMemory<byte> candidateBytes)
    {
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        var bytes = candidateBytes.ToArray();
        _ = Cp6ReleaseValidator.ValidatePlatformCandidate(bytes);
        var root = GitHubApiJson.Parse(bytes);
        S06ReleaseIdentity.RequireCandidate(root);
        var created = Time(root, "createdAtUtc");
        var now = DateTimeOffset.UtcNow;
        Require(created <= now, "publication-locator-time");
        _ = VerifierTrust.Load().RequireKey(LocatorKeyId, "candidate-locator", 1, created, now,
            Cp6ReleaseValidationMode.Current);
        var locator = S06ArtifactAssembly.Control(Cp6ReleaseContractIds.CandidateLocator, new
        {
            releaseTag,
            subjectKind = "PlatformReleaseCandidate",
            subject = ContentAddress.Create(bytes, Cp6ReleaseMediaTypes.PlatformReleaseCandidate,
                "platform-release-candidate.v1.json").ToJson(),
            signerKeyId = LocatorKeyId,
            trustPolicyVersion = 1,
            createdAtUtc = Text(root, "createdAtUtc")
        });
        _ = Cp6ReleaseValidator.ValidateCandidateLocator(locator);
        return locator;
    }

    internal static void RequireIdentical(ReadOnlyMemory<byte> expected, byte[]? actual) =>
        Require(expected.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            actual is not null && expected.Span.SequenceEqual(actual), "publication-readback-conflict");
}

internal sealed record S06PublicationObject(ContentAddress Reference, ReadOnlyMemory<byte> Bytes);
