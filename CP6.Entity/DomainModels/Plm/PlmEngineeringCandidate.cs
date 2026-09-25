namespace CP6.Entity.DomainModels.Plm;

public sealed class PlmEngineeringCandidate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid IterationCandidateId { get; set; }
    public string CandidateKey { get; set; } = "";
    public string ProductCd { get; set; } = "";
    public int CandidateVersion { get; set; } = 1;
    public Guid? CurrentManifestId { get; set; }
    public string Status { get; set; } = "DRAFT";
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
}
