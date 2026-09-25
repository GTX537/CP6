namespace CP6.Entity.DomainModels.Plm;

public sealed class PlmIterationCandidate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IterationKey { get; set; } = "";
    public int IterationVersion { get; set; }
    public string? ReviewPackageDigest { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
}
