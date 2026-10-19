namespace CP6.Core.Services.CrmIdentity.Baselines;

public sealed record IdentityBaselineSourceV1(Guid TenantId, string Issuer, string Region,
    string ContractBundleSha256, string StorageIdentitySha256);
public sealed record IdentityRawSnapshotV1(string Contract, IdentityBaselineSourceV1 Source,
    string Boundary, IdentityVersion Item, string PayloadBase64);
public sealed record IdentityBaselinePageV1(string Contract, IdentityBaselineSourceV1 Source,
    string Boundary, long TotalCount, IdentityVersion[] Items, string? NextCursor);
public sealed record IdentityBaselineLimits(int MaximumItems = 1_000_000, long MaximumPayloadBytes = 268_435_456);
public sealed class IdentityBaselineException(string code) : Exception(code);

/// <summary>Verified complete C02 snapshot set. This is neither an authorization decision nor a received event.</summary>
public sealed class VerifiedIdentityBaseline
{
    public IdentityBaselineSourceV1 Source { get; }
    public string Boundary { get; }
    public IReadOnlyList<IdentityRawSnapshotV1> Items { get; }
    public int TotalCount => Items.Count;
    public string ContentSha256 { get; }
    public string Provenance => "SOURCE_SNAPSHOT";
    internal VerifiedIdentityBaseline(IdentityBaselineSourceV1 source, string boundary,
        IdentityRawSnapshotV1[] items, string contentSha256)
    { Source = source; Boundary = boundary; Items = Array.AsReadOnly(items.ToArray()); ContentSha256 = contentSha256; }
}
