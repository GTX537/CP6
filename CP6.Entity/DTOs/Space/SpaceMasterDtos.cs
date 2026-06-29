namespace CP6.Entity.DTOs.Space;

/// <summary>站场（Site）CRUD DTO（ch00 §9）</summary>
public class SiteDto
{
    public Guid? Id { get; set; }
    public string SiteCode { get; set; } = "";
    public string SiteName { get; set; } = "";
    public string? Address { get; set; }
    public double? Lng { get; set; }
    public double? Lat { get; set; }
    public bool Enable { get; set; } = true;
}

/// <summary>楼层（Floor）CRUD DTO</summary>
public class FloorDto
{
    public Guid? Id { get; set; }
    public Guid SiteId { get; set; }
    public int Level { get; set; }
    public string FloorCode { get; set; } = "";
    public string FloorName { get; set; } = "";
    public int Height { get; set; } = 6000;
    public string? UnderlayImage { get; set; }
    public double? UnderlayScale { get; set; }
    public int UnderlayOffsetX { get; set; }
    public int UnderlayOffsetY { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
}

/// <summary>库区（Zone）CRUD DTO</summary>
public class ZoneDto
{
    public Guid? Id { get; set; }
    public Guid FloorId { get; set; }
    public string ZoneCode { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public int ZoneType { get; set; } = 1;      // 1存储2收货3发货4分拣5通道
    public string Polygon { get; set; } = "[]";  // 顶点 JSON [[x,y],...] mm
    public string? Color { get; set; }
    public bool Enable { get; set; } = true;
}

/// <summary>巷道（Aisle）CRUD DTO</summary>
public class AisleDto
{
    public Guid? Id { get; set; }
    public Guid ZoneId { get; set; }
    public string AisleCode { get; set; } = "";
    public string Polygon { get; set; } = "[]";
    public string Centerline { get; set; } = "[]";
}

/// <summary>货架（Rack）CRUD DTO（含 RowVersion 用于乐观并发）</summary>
public class RackDto
{
    public Guid? Id { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? AisleId { get; set; }
    public Guid? TemplateId { get; set; }
    public string RackCode { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public double RotationZ { get; set; }
    public int Cols { get; set; }
    public int Levels { get; set; }
    public int DepthCount { get; set; } = 1;
    public int CellW { get; set; }
    public int CellH { get; set; }
    public int CellD { get; set; }
    public bool Enable { get; set; } = true;
    public byte[]? RowVersion { get; set; }  // 乐观并发令牌（UpdateRack 需传回）
}
