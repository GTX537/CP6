namespace CP6.Entity.DomainModels.Plm;

public sealed class PlmEngineeringBaseline
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EngineeringCandidateId { get; set; }
    public int CandidateVersion { get; set; }
    public Guid ManifestId { get; set; }
    public string ManifestDigest { get; set; } = "";
    public Guid FreezeRequestId { get; set; }
    public string FreezeInputHash { get; set; } = "";
    public string Status { get; set; } = "FROZEN";
    public DateTime FrozenAtUtc { get; set; }
    public string FrozenBy { get; set; } = "";
    public DateTime? SupersededAtUtc { get; set; }
    public string? SupersededBy { get; set; }
    public string? SupersedeReason { get; set; }
    public Guid? SupersedeRequestId { get; set; }
    public string? SupersedeInputHash { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
