using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>场景差量保存 + D7 绑码服务契约（ch01 §G-1/I-1）</summary>
public interface ISceneService
{
    /// <summary>
    /// 整层差量 upsert（G-1）。
    /// 按 Id 存在与否 upsert zones/aisles/racks/markers/locations；
    /// 删除走护栏（Rack 有库位→E-SPACE-003；Aisle SetNull）；
    /// 位姿/尺寸变更触发 RecalcRackLocationsAsync；乐观并发冲突→E-SPACE-009。
    /// </summary>
    /// <returns>GUID 映射表（通常空，保留扩展；前后端自发 UUID 一致故无需映射）</returns>
    Task<Dictionary<Guid, Guid>> SaveSceneAsync(Guid floorId, SceneSaveDto dto, string? user);

    /// <summary>
    /// D7 反向建模绑码（I-1）。
    /// 校验 Status==1 &amp;&amp; !Placed &amp;&amp; CodeOrigin==2（否则 E-SPACE-004）；
    /// 回填 RackId/FloorId/Col/Level/Depth/AbsXYZ/SizeWHD/Placed=true；不动 LocationCode/Id/Status/Version。
    /// </summary>
    Task BindCodesAsync(Guid rackId, IEnumerable<(Guid locId, int col, int level, int depth)> pairs, string? user);
}
