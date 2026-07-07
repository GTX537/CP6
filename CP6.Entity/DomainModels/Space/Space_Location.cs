using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 库位（Space 章00 §4）。Id(GUID)=LocationId 稳定主键（D4），发布后冻结编码做 WMS join key。
/// LocationCode 草稿可空、发布后非空冻结；CodeOrigin 1引擎生成 / 2采纳导入。
/// Placed（= RackId != null，是否落位）正交于 Status（0草稿/1已发布/2停用）。
/// </summary>
[Table("Space_Location")]
public class Space_Location : BaseBizEntity, IAuditable
{
    /// <summary>所属货架（可空——采纳态 D7 未放置为空）</summary>
    public Guid? RackId { get; set; }

    /// <summary>所属楼层（冗余·可空）</summary>
    public Guid? FloorId { get; set; }

    /// <summary>库位编码（join key；草稿可空，发布后非空冻结）</summary>
    [MaxLength(100)]
    public string? LocationCode { get; set; }

    /// <summary>编码来源：1 引擎生成 / 2 采纳导入</summary>
    public int CodeOrigin { get; set; } = 1;

    /// <summary>列号（1..Cols）</summary>
    public int? Col { get; set; }

    /// <summary>层号（1..Levels）</summary>
    public int? Level { get; set; }

    /// <summary>深号（1..DepthCount）</summary>
    public int? Depth { get; set; }

    /// <summary>绝对坐标缓存 X（mm，章00 §6 重算）</summary>
    public int? AbsX { get; set; }

    /// <summary>绝对坐标缓存 Y（mm）</summary>
    public int? AbsY { get; set; }

    /// <summary>绝对坐标缓存 Z（mm）</summary>
    public int? AbsZ { get; set; }

    /// <summary>格位宽 mm</summary>
    public int? SizeW { get; set; }

    /// <summary>格位高 mm</summary>
    public int? SizeH { get; set; }

    /// <summary>格位深 mm</summary>
    public int? SizeD { get; set; }

    /// <summary>承重 kg（可选）</summary>
    public int? LoadLimit { get; set; }

    /// <summary>容量（可选）</summary>
    public int? Capacity { get; set; }

    /// <summary>容量单位：1托盘 2箱 3件 4体积L</summary>
    public int? CapacityUom { get; set; }

    /// <summary>是否落位（= RackId != null），正交于 Status</summary>
    public bool Placed { get; set; }

    /// <summary>状态：0草稿 1已发布 2停用</summary>
    public int Status { get; set; }

    /// <summary>发布版本号（按 LocationId 递增，章04 幂等判据）。
    /// [AuditIgnore]：批量发布/re-publish 逐位递增属机器 diff（波4 终审裁决 2026-07-07），
    /// 不入字段审计——Status/LocationCode 等人工可感知变更仍审计。</summary>
    [AuditIgnore]
    public long Version { get; set; }
}
