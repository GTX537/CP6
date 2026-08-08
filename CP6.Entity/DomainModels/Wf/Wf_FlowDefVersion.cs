using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

public static class WfDefinitionVersionStatus
{
    public const int Draft = 0;
    public const int Published = 1;
}

[Table("Wf_FlowDefVersion")]
public class Wf_FlowDefVersion : BaseTenantEntity
{
    public Guid FlowDefId { get; set; }
    public int Version { get; set; }
    public int Status { get; set; } = WfDefinitionVersionStatus.Draft;

    [Required, MaxLength(200)]
    public string FlowNameSnapshot { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string SchemaJson { get; set; } = "{}";

    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedBy { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
