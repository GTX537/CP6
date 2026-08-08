using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

[Table("Wf_FormDefVersion")]
public class Wf_FormDefVersion : BaseTenantEntity
{
    public Guid FormDefId { get; set; }
    public int Version { get; set; }
    public int Status { get; set; } = WfDefinitionVersionStatus.Draft;

    [Required, MaxLength(200)]
    public string FormNameSnapshot { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string SchemaJson { get; set; } = "{}";

    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedBy { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
