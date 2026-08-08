using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

[Table("Wf_FormFlowBinding")]
public class Wf_FormFlowBinding : BaseTenantEntity, IAuditable
{
    public Guid FormDefId { get; set; }
    public Guid FlowDefId { get; set; }
    public bool Enable { get; set; } = true;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
