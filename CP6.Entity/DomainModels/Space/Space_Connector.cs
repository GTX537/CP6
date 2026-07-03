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

    /// <summary>登乘/门周期固定成本（秒）。竖直边一次性计（Space P5）。</summary>
    public int WaitSec { get; set; }

    /// <summary>每跨一层的行程成本（秒），按两 stop 的 Level 差乘（Space P5）。</summary>
    public int TravelSecPerFloor { get; set; }
}
