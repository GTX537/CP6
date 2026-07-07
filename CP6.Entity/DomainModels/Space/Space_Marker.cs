using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 标注（Space 章00 §4 / 章02 打点）。楼层上的文字/图标/区域提示。
/// RefRackId 可锚到货架（随架移动），删货架 SetNull。
/// </summary>
[Table("Space_Marker")]
public class Space_Marker : BaseBizEntity, IAuditable
{
    /// <summary>所属楼层</summary>
    public Guid FloorId { get; set; }

    /// <summary>位置 X（mm）</summary>
    public int X { get; set; }

    /// <summary>位置 Y（mm）</summary>
    public int Y { get; set; }

    /// <summary>位置 Z（mm）</summary>
    public int Z { get; set; }

    /// <summary>标注类型：1文字 2图标 3区域提示</summary>
    public int MarkerType { get; set; } = 1;

    /// <summary>标注文本</summary>
    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    /// <summary>锚定的货架（随架移动，删货架 SetNull）</summary>
    public Guid? RefRackId { get; set; }
}
