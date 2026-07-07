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
        try { return Ok2(new { id = await _svc.CreateSiteAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("site/{id:guid}")]
    public async Task<IActionResult> UpdateSite(Guid id, [FromBody] SiteDto d)
    {
        try { await _svc.UpdateSiteAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("site/{id:guid}")]
    public async Task<IActionResult> DeleteSite(Guid id)
    {
        try { await _svc.DeleteSiteAsync(id); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    // ── Floor ─────────────────────────────────────────────────────────────

    [HttpGet("floor")]
    public async Task<IActionResult> ListFloors([FromQuery] Guid siteId) =>
        Ok2(await _svc.ListFloorsAsync(siteId));

    [HttpPost("floor")]
    public async Task<IActionResult> CreateFloor([FromBody] FloorDto d)
    {
        try { return Ok2(new { id = await _svc.CreateFloorAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("floor/{id:guid}")]
    public async Task<IActionResult> UpdateFloor(Guid id, [FromBody] FloorDto d)
    {
        try { await _svc.UpdateFloorAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("floor/{id:guid}")]
    public async Task<IActionResult> DeleteFloor(Guid id)
    {
        try { await _svc.DeleteFloorAsync(id); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    // ── Zone ──────────────────────────────────────────────────────────────

    [HttpGet("zone")]
    public async Task<IActionResult> ListZones([FromQuery] Guid floorId) =>
        Ok2(await _svc.ListZonesAsync(floorId));

    [HttpPost("zone")]
    public async Task<IActionResult> CreateZone([FromBody] ZoneDto d)
    {
        try { return Ok2(new { id = await _svc.CreateZoneAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("zone/{id:guid}")]
    public async Task<IActionResult> UpdateZone(Guid id, [FromBody] ZoneDto d)
    {
        try { await _svc.UpdateZoneAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("zone/{id:guid}")]
    public async Task<IActionResult> DeleteZone(Guid id)
    {
        try { await _svc.DeleteZoneAsync(id); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    // ── Aisle ─────────────────────────────────────────────────────────────

    [HttpGet("aisle")]
    public async Task<IActionResult> ListAisles([FromQuery] Guid zoneId) =>
        Ok2(await _svc.ListAislesAsync(zoneId));

    [HttpPost("aisle")]
    public async Task<IActionResult> CreateAisle([FromBody] AisleDto d)
    {
        try { return Ok2(new { id = await _svc.CreateAisleAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("aisle/{id:guid}")]
    public async Task<IActionResult> UpdateAisle(Guid id, [FromBody] AisleDto d)
    {
        try { await _svc.UpdateAisleAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("aisle/{id:guid}")]
    public async Task<IActionResult> DeleteAisle(Guid id, [FromQuery] string? mode = null, [FromQuery] Guid? targetAisleId = null)
    {
        try { await _svc.DeleteAisleAsync(id, mode, targetAisleId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    // ── Rack ──────────────────────────────────────────────────────────────

    [HttpGet("rack")]
    public async Task<IActionResult> ListRacks([FromQuery] Guid zoneId) =>
        Ok2(await _svc.ListRacksAsync(zoneId));

    [HttpPost("rack")]
    public async Task<IActionResult> CreateRack([FromBody] RackDto d)
    {
        try { return Ok2(new { id = await _svc.CreateRackAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("rack/{id:guid}")]
    public async Task<IActionResult> UpdateRack(Guid id, [FromBody] RackDto d)
    {
        try { await _svc.UpdateRackAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("rack/{id:guid}")]
    public async Task<IActionResult> DeleteRack(Guid id, [FromQuery] string? mode = null, [FromQuery] Guid? targetRackId = null)
    {
        try { await _svc.DeleteRackAsync(id, mode, targetRackId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
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
