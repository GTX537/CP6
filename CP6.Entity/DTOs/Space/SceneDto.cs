namespace CP6.Entity.DTOs.Space;

/// <summary>
/// /floor/{id}/scene 场景聚合响应（ch00 §9.1）。
/// 前端 05 Three.js Viewer 一次拉全楼层几何：Zone/Aisle/Rack + 已放置库位 + Marker。
/// </summary>
public class SceneDto
{
    public Guid FloorId { get; set; }
    public List<ZoneDto> Zones { get; set; } = new();
    public List<AisleDto> Aisles { get; set; } = new();
    public List<RackDto> Racks { get; set; } = new();
    /// <summary>仅含 Placed=true 的库位（未放置的走 /location/unplaced）</summary>
    public List<SceneLocationDto> Locations { get; set; } = new();
    public List<MarkerDto> Markers { get; set; } = new();
}

/// <summary>库位在 Scene 中的轻量视图（含绝对坐标缓存，不含业务详情）</summary>
public class SceneLocationDto
{
    public Guid Id { get; set; }
    public Guid RackId { get; set; }
    public string? LocationCode { get; set; }
    public int Col { get; set; }
    public int Level { get; set; }
    public int Depth { get; set; }
    public int AbsX { get; set; }
    public int AbsY { get; set; }
    public int AbsZ { get; set; }
    public int SizeW { get; set; }
    public int SizeH { get; set; }
    public int SizeD { get; set; }
    public int Status { get; set; }  // 0草稿1已发布2停用
}

/// <summary>地图标注（Marker）视图 DTO</summary>
public class MarkerDto
{
    public Guid? Id { get; set; }
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int MarkerType { get; set; } = 1;  // 1文字2图标3区域提示
    public string Text { get; set; } = "";
    public Guid? RefRackId { get; set; }
}
