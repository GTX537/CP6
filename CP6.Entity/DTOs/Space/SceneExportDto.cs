namespace CP6.Entity.DTOs.Space;

/// <summary>场景导出载荷（ch01 §G-3）。仅含几何数据，不含 TenantId/AbsXYZ/LocationCode/Status/RowVersion。</summary>
public class SceneExportDto
{
    public SpaceDataSourceDto Source { get; set; } = SpaceDataSourceDto.Runtime();
    public SceneExportMeta Meta { get; set; } = new();
    public List<ZoneExportDto> Zones { get; set; } = new();
    public List<AisleExportDto> Aisles { get; set; } = new();
    public List<RackExportDto> Racks { get; set; } = new();
}

/// <summary>楼层元数据（导出无 TenantId/Id/SiteId）</summary>
public class SceneExportMeta
{
    public string FloorCode { get; set; } = "";
    public string FloorName { get; set; } = "";
    public int Level { get; set; }
    public int Height { get; set; } = 6000;
    public string? UnderlayImage { get; set; }
    public double? UnderlayScale { get; set; }
    public int UnderlayOffsetX { get; set; }
    public int UnderlayOffsetY { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
}

/// <summary>库区几何导出（携带原始 Id 供导入 GUID 映射）</summary>
public class ZoneExportDto
{
    public Guid Id { get; set; }
    public string ZoneCode { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public int ZoneType { get; set; } = 1;
    public string Polygon { get; set; } = "[]";
    public string? Color { get; set; }
}

/// <summary>巷道几何导出</summary>
public class AisleExportDto
{
    public Guid Id { get; set; }
    public Guid ZoneId { get; set; }
    public string AisleCode { get; set; } = "";
    public string Polygon { get; set; } = "[]";
    public string Centerline { get; set; } = "[]";
}

/// <summary>货架几何导出（不含 AbsXYZ/RowVersion/TenantId）</summary>
public class RackExportDto
{
    public Guid Id { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? AisleId { get; set; }
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
}
