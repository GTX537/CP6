namespace CP6.Entity.DTOs.Space;

/// <summary>
/// 按编码定位结果（ch06 §8，相机 flyTo 用）。未放置（Placed=false）时无几何，前端提示 W-SPACE-601。
/// </summary>
public class LocateResult
{
    public Guid LocationId { get; set; }
    public Guid? FloorId { get; set; }
    public string LocationCode { get; set; } = "";
    public int AbsX { get; set; }
    public int AbsY { get; set; }
    public int AbsZ { get; set; }
    public bool Placed { get; set; }
    public int Status { get; set; }  // 0草稿1已发布2停用
}

/// <summary>编码前缀搜索候选项（ch06 §6 模糊定位）。</summary>
public class LocationSearchItem
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public Guid? FloorId { get; set; }
    public bool Placed { get; set; }
    public int Status { get; set; }
}

/// <summary>库位详情（信息卡，含变长层级路径）。复用 <see cref="LocationPath"/>（与发布载荷同形）。</summary>
public class LocationDetail
{
    public Guid LocationId { get; set; }
    public string? LocationCode { get; set; }
    public LocationPath Path { get; set; } = new();
    public int Status { get; set; }
    public int CodeOrigin { get; set; }
    public int AbsX { get; set; }
    public int AbsY { get; set; }
    public int AbsZ { get; set; }
}
