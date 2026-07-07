using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// Space 主数据 Web API（ch00 §9）。
/// 路由前缀 /api/space；租户隔离由 TenantMiddleware + CP6Context 全局查询过滤自动施加。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class SpaceMasterController : ControllerBase
{
    private readonly ISpaceMasterService _svc;
    public SpaceMasterController(ISpaceMasterService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    // ── Site ──────────────────────────────────────────────────────────────

    [HttpGet("site")]
    public async Task<IActionResult> ListSites() => Ok2(await _svc.ListSitesAsync());

    [HttpPost("site")]
    public async Task<IActionResult> CreateSite([FromBody] SiteDto d)
    {
        return Ok2(new { id = await _svc.CreateSiteAsync(d, CurrentUser) });
    }

    [HttpPut("site/{id:guid}")]
    public async Task<IActionResult> UpdateSite(Guid id, [FromBody] SiteDto d)
    {
        await _svc.UpdateSiteAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("site/{id:guid}")]
    public async Task<IActionResult> DeleteSite(Guid id)
    {
        await _svc.DeleteSiteAsync(id); return Ok2();
    }

    // ── Floor ─────────────────────────────────────────────────────────────

    [HttpGet("floor")]
    public async Task<IActionResult> ListFloors([FromQuery] Guid siteId) =>
        Ok2(await _svc.ListFloorsAsync(siteId));

    [HttpPost("floor")]
    public async Task<IActionResult> CreateFloor([FromBody] FloorDto d)
    {
        return Ok2(new { id = await _svc.CreateFloorAsync(d, CurrentUser) });
    }

    [HttpPut("floor/{id:guid}")]
    public async Task<IActionResult> UpdateFloor(Guid id, [FromBody] FloorDto d)
    {
        await _svc.UpdateFloorAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("floor/{id:guid}")]
    public async Task<IActionResult> DeleteFloor(Guid id)
    {
        await _svc.DeleteFloorAsync(id); return Ok2();
    }

    // ── Zone ──────────────────────────────────────────────────────────────

    [HttpGet("zone")]
    public async Task<IActionResult> ListZones([FromQuery] Guid floorId) =>
        Ok2(await _svc.ListZonesAsync(floorId));

    [HttpPost("zone")]
    public async Task<IActionResult> CreateZone([FromBody] ZoneDto d)
    {
        return Ok2(new { id = await _svc.CreateZoneAsync(d, CurrentUser) });
    }

    [HttpPut("zone/{id:guid}")]
    public async Task<IActionResult> UpdateZone(Guid id, [FromBody] ZoneDto d)
    {
        await _svc.UpdateZoneAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("zone/{id:guid}")]
    public async Task<IActionResult> DeleteZone(Guid id)
    {
        await _svc.DeleteZoneAsync(id); return Ok2();
    }

    // ── Aisle ─────────────────────────────────────────────────────────────

    [HttpGet("aisle")]
    public async Task<IActionResult> ListAisles([FromQuery] Guid zoneId) =>
        Ok2(await _svc.ListAislesAsync(zoneId));

    [HttpPost("aisle")]
    public async Task<IActionResult> CreateAisle([FromBody] AisleDto d)
    {
        return Ok2(new { id = await _svc.CreateAisleAsync(d, CurrentUser) });
    }

    [HttpPut("aisle/{id:guid}")]
    public async Task<IActionResult> UpdateAisle(Guid id, [FromBody] AisleDto d)
    {
        await _svc.UpdateAisleAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("aisle/{id:guid}")]
    public async Task<IActionResult> DeleteAisle(Guid id, [FromQuery] string? mode = null, [FromQuery] Guid? targetAisleId = null)
    {
        await _svc.DeleteAisleAsync(id, mode, targetAisleId, CurrentUser); return Ok2();
    }

    // ── Rack ──────────────────────────────────────────────────────────────

    [HttpGet("rack")]
    public async Task<IActionResult> ListRacks([FromQuery] Guid zoneId) =>
        Ok2(await _svc.ListRacksAsync(zoneId));

    [HttpPost("rack")]
    public async Task<IActionResult> CreateRack([FromBody] RackDto d)
    {
        return Ok2(new { id = await _svc.CreateRackAsync(d, CurrentUser) });
    }

    [HttpPut("rack/{id:guid}")]
    public async Task<IActionResult> UpdateRack(Guid id, [FromBody] RackDto d)
    {
        await _svc.UpdateRackAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("rack/{id:guid}")]
    public async Task<IActionResult> DeleteRack(Guid id, [FromQuery] string? mode = null, [FromQuery] Guid? targetRackId = null)
    {
        await _svc.DeleteRackAsync(id, mode, targetRackId, CurrentUser); return Ok2();
    }

    // ── 场景聚合 / 待绑定 / 库位列表 ────────────────────────────────────

    [HttpGet("floor/{id:guid}/scene")]
    public async Task<IActionResult> Scene(Guid id) =>
        Ok2(await _svc.GetSceneAsync(id));

    [HttpGet("location/unplaced")]
    public async Task<IActionResult> Unplaced([FromQuery] Guid floorId) =>
        Ok2(await _svc.GetUnplacedAsync(floorId));

    [HttpGet("location")]
    public async Task<IActionResult> Locations([FromQuery] Guid rackId) =>
        Ok2(await _svc.ListLocationsAsync(rackId));
}
