using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>连接体（电梯/楼梯/坡道）：竖井，经 N 条 ConnectorStop 服务多层（Space P4）。</summary>
[Table("Space_Connector")]
public class Space_Connector : BaseBizEntity
{
    public Guid SiteId { get; set; }

    /// <summary>连接体编码（站内唯一）</summary>
    [Required, MaxLength(50)]
    public string ConnectorCode { get; set; } = string.Empty;

    /// <summary>类型 1=Elevator 2=Stairs 3=Ramp</summary>
    public int ConnectorType { get; set; } = 1;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
