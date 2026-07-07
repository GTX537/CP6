using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 楼层（Space 章00 §4）。每 Floor 是一个独立局部坐标系（mm / Z-up / 原点 OriginX/Y）。
/// Building 简化：Site → Floor 直连（无独立楼栋层）。
/// </summary>
[Table("Space_Floor")]
public class Space_Floor : BaseBizEntity, IAuditable
{
    /// <summary>所属站点</summary>
    public Guid SiteId { get; set; }

    /// <summary>楼层序号（如 1/2/-1）</summary>
    public int Level { get; set; }

    /// <summary>楼层编码（站点内唯一）</summary>
    [Required, MaxLength(50)]
    public string FloorCode { get; set; } = string.Empty;

    /// <summary>楼层名称</summary>
    [Required, MaxLength(100)]
    public string FloorName { get; set; } = string.Empty;

    /// <summary>层高 mm</summary>
    public int Height { get; set; } = 6000;

    /// <summary>底图（CAD/平面图）资源路径，用于描图标定</summary>
    [MaxLength(500)]
    public string? UnderlayImage { get; set; }

    /// <summary>底图比例 mm/px（描图对齐必需）</summary>
    public double? UnderlayScale { get; set; }

    /// <summary>底图偏移 X（px）</summary>
    public int UnderlayOffsetX { get; set; }

    /// <summary>底图偏移 Y（px）</summary>
    public int UnderlayOffsetY { get; set; }

    /// <summary>局部坐标系原点 X（mm）</summary>
    public int OriginX { get; set; }

    /// <summary>局部坐标系原点 Y（mm）</summary>
    public int OriginY { get; set; }
}
