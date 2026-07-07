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
    /// <summary>
    /// 删巷道（ch04 §7.1/§7.2）：其下有已发布库位时默认 Restrict（E-SPACE-402）；
    /// mode=deactivate → 逐个走停用同步 RPC 后删（路径A）；
    /// mode=rehome → 货架改挂 targetAisleId（可 null=脱巷道）+ re-publish 刷新 path 后删（路径B）。
    /// 无已发布库位时行为同旧版：SetNull 其下 Rack.AisleId → 删。
    /// </summary>
    Task DeleteAisleAsync(Guid id, string? mode = null, Guid? targetAisleId = null, string? user = null);

    // ── Rack ──────────────────────────────────────────────────────────────
    Task<Guid> CreateRackAsync(RackDto dto, string? user);
    Task UpdateRackAsync(Guid id, RackDto dto, string? user);   // 位姿/尺寸变更后触发几何重算
    Task<List<RackDto>> ListRacksAsync(Guid zoneId);
    /// <summary>
    /// 删货架（ch04 §7.1/§7.2）：有已发布库位默认 Restrict（E-SPACE-403）；
    /// mode=deactivate → 逐个停用后级联删（停用位可删，2026-07-06 拍板）；
    /// mode=rehome → 整架库位改挂 targetRackId（同规格换架：目标网格≥源且无自有库位）+ re-publish 后删源架。
    /// 无已发布库位 → 库位级联删 + 删货架（旧 E-SPACE-003 全拦废止）。
    /// </summary>
    Task DeleteRackAsync(Guid id, string? mode = null, Guid? targetRackId = null, string? user = null);

    // ── 场景聚合 / 待绑定 / 库位列表 ───────────────────────────────────────
    Task<SceneDto> GetSceneAsync(Guid floorId);                 // 仅含 Placed=true 库位
    Task<List<SceneLocationDto>> GetUnplacedAsync(Guid floorId); // Status=1 ∧ Placed=false（租户全量）
    Task<List<SceneLocationDto>> ListLocationsAsync(Guid rackId);
}
