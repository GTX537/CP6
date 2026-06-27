using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>场景导入导出服务契约（ch01 §G-3）</summary>
public interface ISceneIoService
{
    /// <summary>导出楼层几何（不含 TenantId/AbsXYZ/LocationCode/Status/RowVersion）</summary>
    Task<SceneExportDto> ExportAsync(Guid floorId);

    /// <summary>
    /// 导入场景到指定站点（G-3）。
    /// 建新 GUID 映射表重连 Site/Floor/Zone/Aisle/Rack 父子；租户由 EF 盖章自动注入；
    /// 库位用 LocationGeometryService.ComputeAbs 按货架参数全枚举重建（Status=0/CodeOrigin=1/Placed=true/LocationCode=null）。
    /// </summary>
    /// <returns>新楼层 Id</returns>
    Task<Guid> ImportAsync(Guid siteId, SceneExportDto dto, string? user);
}
