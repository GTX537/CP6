namespace CP6.Entity.DomainModels.Plm;

public sealed class PlmTechnicalManifestItem
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ManifestId { get; set; }
    public string ItemType { get; set; } = "";
    public string SourceOwnerType { get; set; } = "";
    public string IdentityKey { get; set; } = "";
    public string ProductCd { get; set; } = "";
    public string? ProcessCd { get; set; }
    public string? MaterialCd { get; set; }
    public string? TaskCd { get; set; }
    public int? SemanticSortOrder { get; set; }
    public byte[] ItemCanonicalBytes { get; set; } = [];
    public string ItemDigest { get; set; } = "";
    public Guid SourceOwnerId { get; set; }
    public byte[]? SourceRowVersion { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string CapturedBy { get; set; } = "";
}
