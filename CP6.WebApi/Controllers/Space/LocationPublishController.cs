using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// 库位发布、停用、采纳、事件查询 Web API（ch04）。
/// 路由前缀 /api/space；租户隔离由 TenantMiddleware + CP6Context 全局查询过滤自动施加。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class LocationPublishController : ControllerBase
{
    private readonly ILocationPublishService _svc;
    private readonly CP6Context _db;

    public LocationPublishController(ILocationPublishService svc, CP6Context db)
    {
        _svc = svc;
        _db = db;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    // ── POST /api/space/floor/{id}/publish ────────────────────────────────

    /// <summary>整层/库区发布（ch04 §4）。</summary>
    [HttpPost("floor/{id:guid}/publish")]
    public async Task<IActionResult> PublishFloor(Guid id, [FromBody] PublishFloorRequest? req)
    {
        try
        {
            var count = await _svc.PublishFloorAsync(id, req?.ZoneId, CurrentUser);
            return Ok2(new { published = count });
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { code = 400, message = e.Message });
        }
    }

    // ── PUT /api/space/location/{id}/deactivate ───────────────────────────

    /// <summary>停用已发布库位（ch04 §6）。</summary>
    [HttpPut("location/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        try
        {
            await _svc.DeactivateAsync(id, CurrentUser);
            return Ok2();
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { code = 400, message = e.Message });
        }
    }

    // ── POST /api/space/location/adopt ────────────────────────────────────

    /// <summary>存量采纳导入（ch04 §8.1）。</summary>
    [HttpPost("location/adopt")]
    public async Task<IActionResult> Adopt([FromBody] AdoptRequest req)
    {
        try
        {
            var items = req.Items.Select(i => (i.Code, (Dictionary<string, object?>?)i.Attrs));
            var (imported, skipped) = await _svc.AdoptAsync(items, CurrentUser);
            return Ok2(new { imported, skipped });
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { code = 400, message = e.Message });
        }
    }

    // ── GET /api/space/publish/events ─────────────────────────────────────

    /// <summary>列出 SPACE→WMS 集成事件（ch04 §2）。</summary>
    [HttpGet("publish/events")]
    public async Task<IActionResult> ListEvents([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var events = await _db.IntegrationEvents
            .Where(e => e.SourceModule == "SPACE")
            .OrderByDescending(e => e.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.HookName,
                e.SourceNo,
                e.TargetModule,
                e.Status,
                e.Attempts,
                e.CreateDate,
                e.LastError
            })
            .ToListAsync();
        return Ok2(events);
    }
}

/// <summary>发布整层请求体（zoneId 可选）。</summary>
public class PublishFloorRequest
{
    public Guid? ZoneId { get; set; }
}

/// <summary>采纳导入请求体。</summary>
public class AdoptRequest
{
    public List<AdoptItem> Items { get; set; } = new();
}

/// <summary>单条采纳记录。</summary>
public class AdoptItem
{
    public string Code { get; set; } = "";
    public Dictionary<string, object?>? Attrs { get; set; }
}
