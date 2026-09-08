using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

internal enum R2DiscoveryPart { Locator = 1, Bundle = 2 }

// No arbitrary URI, bucket or key constructor. Authentication sequencing belongs to the graph reader.
internal sealed class R2ObjectTarget
{
    private R2ObjectTarget(string key, string mediaType, ContentAddress? reference)
    {
        Key = key;
        MediaType = mediaType;
        Reference = reference;
    }

    internal string Key { get; }
    internal string MediaType { get; }
    internal ContentAddress? Reference { get; }

    internal static R2ObjectTarget Discovery(string releaseTag, R2DiscoveryPart part)
    {
        if (releaseTag.Length is < 1 or > 128 || releaseTag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw R2WirePolicy.Error("r2-discovery-tag");
        var keys = Cp6CandidateLocatorKeys.ForPlatformTag(releaseTag);
        return part switch
        {
            R2DiscoveryPart.Locator => new(keys.LocatorKey, Cp6ReleaseMediaTypes.CandidateLocator, null),
            R2DiscoveryPart.Bundle => new(keys.BundleKey, Cp6ReleaseMediaTypes.SigstoreBundle, null),
            _ => throw R2WirePolicy.Error("r2-discovery-part")
        };
    }

    internal static R2ObjectTarget Addressed(ContentAddress reference) => new(reference.Key, reference.MediaType, reference);
}
