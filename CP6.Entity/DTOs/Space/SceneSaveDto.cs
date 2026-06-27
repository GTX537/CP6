namespace CP6.Entity.DTOs.Space;

/// <summary>整层差量保存载荷（ch01 §G-1）。各集合只传变更集，null 表示无变更。</summary>
public class SceneSaveDto
{
    public List<RackDto>? Racks { get; set; }
    public List<AisleDto>? Aisles { get; set; }
    public List<ZoneDto>? Zones { get; set; }
    public List<MarkerDto>? Markers { get; set; }
    public List<SceneLocationSaveDto>? Locations { get; set; }
    public Deletes? Deletes { get; set; }
}

/// <summary>删除 Id 列表</summary>
public class Deletes
{
    public List<Guid>? Racks { get; set; }
    public List<Guid>? Aisles { get; set; }
    public List<Guid>? Zones { get; set; }
    public List<Guid>? Markers { get; set; }
}

/// <summary>库位差量保存 DTO（G-1 用，含 Id 以区分新建/更新）</summary>
public class SceneLocationSaveDto
{
    public Guid? Id { get; set; }
    public Guid RackId { get; set; }
    public int Col { get; set; }
    public int Level { get; set; }
    public int Depth { get; set; }
    public bool Placed { get; set; }
    public int Status { get; set; }
    public int CodeOrigin { get; set; }
    /// <summary>显示用尺寸描述（保存时忽略，由 RecalcRack 从货架 Cell 维度重算）</summary>
    public string? Size { get; set; }
}
