using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 货架（Space 章00 §4）。锚点角 (X,Y,Z) + 绕角偏航 RotationZ；Cols×Levels×DepthCount 个格位。
/// ZoneId 必填、AisleId 可选（条件父级）、FloorId 冗余 = Zone.FloorId。
/// </summary>
[Table("Space_Rack")]
public class Space_Rack : BaseBizEntity, IAuditable
{
    /// <summary>所属库区（必填）</summary>
    public Guid ZoneId { get; set; }

    /// <summary>所属巷道（可选，有巷道才挂）</summary>
    public Guid? AisleId { get; set; }

    /// <summary>所属楼层（冗余 = Zone.FloorId，便于按层查询）</summary>
    public Guid FloorId { get; set; }

    /// <summary>引用模板（可选）</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>货架编码（库区内唯一）</summary>
    [Required, MaxLength(50)]
    public string RackCode { get; set; } = string.Empty;

    /// <summary>锚点角 X（mm，floor 局部系）</summary>
    public int X { get; set; }

    /// <summary>锚点角 Y（mm）</summary>
    public int Y { get; set; }

    /// <summary>锚点角 Z（mm，v1 恒 0）</summary>
    public int Z { get; set; }

    /// <summary>偏航角（度），绕锚点角</summary>
    public double RotationZ { get; set; }

    /// <summary>列数</summary>
    public int Cols { get; set; }

    /// <summary>层数</summary>
    public int Levels { get; set; }

    /// <summary>深度格数（纵深）</summary>
    public int DepthCount { get; set; } = 1;

    /// <summary>单格宽 mm</summary>
    public int CellW { get; set; }

    /// <summary>单格高 mm</summary>
    public int CellH { get; set; }

    /// <summary>单格深 mm</summary>
    public int CellD { get; set; }

    /// <summary>是否启用</summary>
    public bool Enable { get; set; } = true;
}
