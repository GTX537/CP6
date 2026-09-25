namespace CP6.Entity.DomainModels.Plm;

public sealed class PlmTechnicalManifest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EngineeringCandidateId { get; set; }
    public int CandidateVersion { get; set; }
    public string ProductCd { get; set; } = "";
    public string SchemaVersion { get; set; } = "plm-technical-manifest-v1";
    public byte[] CanonicalBytes { get; set; } = [];
    public string ManifestDigest { get; set; } = "";
    public int ItemCount { get; set; }
    public Guid CaptureRequestId { get; set; }
    public string CaptureInputHash { get; set; } = "";
    public DateTime CapturedAtUtc { get; set; }
    public string CapturedBy { get; set; } = "";
}
