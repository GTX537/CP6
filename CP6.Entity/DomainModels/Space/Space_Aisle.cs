using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 巷道（Space 章00 §4，条件父级）。Rack.ZoneId 必填、AisleId 可选——有巷道才挂。
/// Centerline 存中心线节点 JSON，章08 拣货路径消费。
/// </summary>
[Table("Space_Aisle")]
public class Space_Aisle : BaseBizEntity, IAuditable
{
    /// <summary>所属库区</summary>
    public Guid ZoneId { get; set; }

    /// <summary>巷道编码（库区内唯一）</summary>
    [Required, MaxLength(50)]
    public string AisleCode { get; set; } = string.Empty;

    /// <summary>巷道面顶点 JSON</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Polygon { get; set; } = "[]";

    /// <summary>中心线节点 JSON（章08 拣货路径图消费）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Centerline { get; set; } = "[]";
}
