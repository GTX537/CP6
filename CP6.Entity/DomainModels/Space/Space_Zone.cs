using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 库区（Space 章00 §4）。楼层内的功能分区（存储/收货/发货/分拣/通道）。
/// Polygon 存顶点 JSON（floor 局部系 mm）。
/// </summary>
[Table("Space_Zone")]
public class Space_Zone : BaseBizEntity
{
    /// <summary>所属楼层</summary>
    public Guid FloorId { get; set; }

    /// <summary>库区编码（楼层内唯一）</summary>
    [Required, MaxLength(50)]
    public string ZoneCode { get; set; } = string.Empty;

    /// <summary>库区名称</summary>
    [Required, MaxLength(100)]
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>库区类型：1存储 2收货 3发货 4分拣 5通道</summary>
    public int ZoneType { get; set; } = 1;

    /// <summary>多边形顶点 JSON [[x,y],...]（mm，floor 局部系）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Polygon { get; set; } = "[]";

    /// <summary>显示颜色（可选）</summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>是否启用</summary>
    public bool Enable { get; set; } = true;
}
