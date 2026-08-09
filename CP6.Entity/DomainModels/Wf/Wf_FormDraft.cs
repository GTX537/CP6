using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

public static class WfFormDraftStatus
{
    public const int Active = 0;
    public const int Submitted = 1;
}

[Table("Wf_FormDraft")]
public class Wf_FormDraft : BaseTenantEntity
{
    public Guid OwnerUserId { get; set; }
    public Guid FormDefId { get; set; }
    public Guid FormDefVersionId { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string DataJson { get; set; } = "{}";

    [MaxLength(200)]
    public string? Title { get; set; }

    public int Status { get; set; } = WfFormDraftStatus.Active;
    public Guid? SubmittedFormDataId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? LegacyFlowInstanceId { get; set; }
    public Guid? RebasedFromVersionId { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
