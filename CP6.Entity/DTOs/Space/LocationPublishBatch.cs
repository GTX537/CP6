namespace CP6.Entity.DTOs.Space;

/// <summary>库位发布批次载荷（ch04 §3.1）。</summary>
public class LocationPublishBatch
{
    public string BatchNo { get; set; } = "";
    public Guid TenantId { get; set; }
    public string? PublishedBy { get; set; }
    public List<LocationPublishItem> Items { get; set; } = new();
}

/// <summary>单条库位发布记录。Op: UPSERT | DEACTIVATE。</summary>
public class LocationPublishItem
{
    /// <summary>操作类型：UPSERT（新建/更新）| DEACTIVATE（停用）</summary>
    public string Op { get; set; } = "UPSERT";
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    /// <summary>编码来源：1 引擎生成 / 2 采纳导入</summary>
    public int CodeOrigin { get; set; }
    public long Version { get; set; }
    /// <summary>仓库编码（v1.1 §3.4：发布 hook 投递前按 SiteCode↔WarehouseCd 映射填好；(WarehouseCd, LocationCode) 是跨系统 join 锚）</summary>
    public string? WarehouseCd { get; set; }
    public LocationPath Path { get; set; } = new();
    /// <summary>业务属性（仅 size 等，★绝不含 AbsX/Y/Z 几何坐标）</summary>
    public Dictionary<string, object?> Attrs { get; set; } = new();
}

/// <summary>变长路径（有巷道填 AisleCode，无巷道为 null）。</summary>
public class LocationPath
{
    public string? SiteCode { get; set; }
    public int FloorLevel { get; set; }
    public string? ZoneCode { get; set; }
    /// <summary>巷道编码（条件段，无巷道为 null）</summary>
    public string? AisleCode { get; set; }
    public string? RackCode { get; set; }
    public int Col { get; set; }
    public int Level { get; set; }
    public int Depth { get; set; }
}

/// <summary>WMS 消费结果。</summary>
public class WmsConsumeResult
{
    public bool Success { get; set; }
    public bool AllSkipped { get; set; }
    public List<WmsItemResult> Items { get; set; } = new();
}

/// <summary>单条库位消费结果。Status: UPSERTED | DEACTIVATED | SKIPPED | REJECTED。</summary>
public class WmsItemResult
{
    public Guid LocationId { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
}
