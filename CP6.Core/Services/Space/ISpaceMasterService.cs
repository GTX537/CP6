using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>
/// Space 主数据服务接口（ch00 §9）。
/// 覆盖 Site / Floor / Zone / Aisle / Rack 的 CRUD + 删除护栏 + 场景聚合 + 待绑定列表。
/// </summary>
public interface ISpaceMasterService
{
    // ── Site ──────────────────────────────────────────────────────────────
    Task<Guid> CreateSiteAsync(SiteDto dto, string? user);
    Task UpdateSiteAsync(Guid id, SiteDto dto, string? user);
    Task<List<SiteDto>> ListSitesAsync();
    Task DeleteSiteAsync(Guid id);                // 护栏：有 Floor 子 → E-SPACE-007

    // ── Floor ─────────────────────────────────────────────────────────────
    Task<Guid> CreateFloorAsync(FloorDto dto, string? user);
    Task UpdateFloorAsync(Guid id, FloorDto dto, string? user);
    Task<List<FloorDto>> ListFloorsAsync(Guid siteId);
    Task DeleteFloorAsync(Guid id);               // 护栏：有 Zone/Marker 子 → E-SPACE-007

    // ── Zone ──────────────────────────────────────────────────────────────
    Task<Guid> CreateZoneAsync(ZoneDto dto, string? user);
    Task UpdateZoneAsync(Guid id, ZoneDto dto, string? user);
    Task<List<ZoneDto>> ListZonesAsync(Guid floorId);
    Task DeleteZoneAsync(Guid id);                // 护栏：有 Aisle/Rack 子 → E-SPACE-007

    // ── Aisle ─────────────────────────────────────────────────────────────
    Task<Guid> CreateAisleAsync(AisleDto dto, string? user);
    Task UpdateAisleAsync(Guid id, AisleDto dto, string? user);
    Task<List<AisleDto>> ListAislesAsync(Guid zoneId);
    Task DeleteAisleAsync(Guid id);               // SetNull: 其下 Rack.AisleId 置 null → 再删

    // ── Rack ──────────────────────────────────────────────────────────────
    Task<Guid> CreateRackAsync(RackDto dto, string? user);
    Task UpdateRackAsync(Guid id, RackDto dto, string? user);   // 位姿/尺寸变更后触发几何重算
    Task<List<RackDto>> ListRacksAsync(Guid zoneId);
    Task DeleteRackAsync(Guid id);                // 护栏：有库位 → E-SPACE-003

    // ── 场景聚合 / 待绑定 / 库位列表 ───────────────────────────────────────
    Task<SceneDto> GetSceneAsync(Guid floorId);                 // 仅含 Placed=true 库位
    Task<List<SceneLocationDto>> GetUnplacedAsync(Guid floorId); // Status=1 ∧ Placed=false（租户全量）
    Task<List<SceneLocationDto>> ListLocationsAsync(Guid rackId);
}
