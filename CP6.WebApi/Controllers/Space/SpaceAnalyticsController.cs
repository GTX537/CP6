using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public sealed class SpaceAnalyticsController : ControllerBase
{
    private readonly ISpaceAnalyticsService _service;
    private readonly CP6Context _db;

    public SpaceAnalyticsController(ISpaceAnalyticsService service, CP6Context db)
    {
        _service = service;
        _db = db;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string message = "OK") =>
        Ok(new { code = 0, message, data });

    [HttpGet("site/{siteId:guid}/analytics/config")]
    public async Task<IActionResult> GetConfig(Guid siteId, CancellationToken ct)
    {
        if (!await SiteExistsAsync(siteId, ct)) return NotFound(new { code = 404, message = "E-SPACE-001" });
        return Ok2(await _service.GetConfigAsync(ct));
    }

    [HttpPut("site/{siteId:guid}/analytics/config")]
    [RequirePermission("space-control-tower", "manage")]
    public async Task<IActionResult> UpdateConfig(
        Guid siteId, [FromBody] SpaceAnalyticsConfigUpdate request, CancellationToken ct)
    {
        if (!await SiteExistsAsync(siteId, ct)) return NotFound(new { code = 404, message = "E-SPACE-001" });
        try
        {
            return Ok2(await _service.UpdateConfigAsync(request, CurrentUser, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpPost("site/{siteId:guid}/analytics/abc/rebuild")]
    [RequirePermission("space-control-tower", "manage")]
    public async Task<IActionResult> RebuildAbc(Guid siteId, CancellationToken ct) =>
        Ok2(await _service.RebuildAbcAsync(siteId, "manual", CurrentUser, ct));

    [HttpGet("floor/{floorId:guid}/analytics/utilization")]
    public async Task<IActionResult> Utilization(Guid floorId, CancellationToken ct) =>
        Ok2(await _service.GetUtilizationAsync(floorId, ct));

    [HttpGet("floor/{floorId:guid}/analytics/storage-types")]
    public async Task<IActionResult> StorageTypes(Guid floorId, CancellationToken ct) =>
        Ok2(await _service.GetStorageTypesAsync(floorId, ct));

    [HttpGet("floor/{floorId:guid}/analytics/abc")]
    public async Task<IActionResult> Abc(
        Guid floorId, [FromQuery] bool? includeProducts, CancellationToken ct) =>
        Ok2(await _service.GetAbcAsync(floorId, ct, includeProducts ?? true));

    [HttpGet("site/{siteId:guid}/control-tower")]
    [RequirePermission("space-control-tower", "view")]
    public async Task<IActionResult> ControlTower(Guid siteId, CancellationToken ct) =>
        Ok2(await _service.GetControlTowerAsync(siteId, ct));

    private Task<bool> SiteExistsAsync(Guid siteId, CancellationToken ct) =>
        _db.Space_Sites.AsNoTracking().AnyAsync(x => x.Id == siteId && !x.IsDeleted, ct);
}
