using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

[Table("Wf_FlowDefVersionDependency")]
public class Wf_FlowDefVersionDependency : BaseTenantEntity
{
    public Guid FlowDefVersionId { get; set; }

    [Required, MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string DependencyType { get; set; } = "SubFlow";

    public Guid TargetFlowDefVersionId { get; set; }
}
