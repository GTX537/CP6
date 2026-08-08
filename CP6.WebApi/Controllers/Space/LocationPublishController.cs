using CP6.Core.Auth;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// 库位发布、停用、采纳、事件查询 Web API（ch04）。
/// 路由前缀 /api/space；租户隔离由 TenantMiddleware + CP6Context 全局查询过滤自动施加。
/// 权限约定：变更端点使用业务精确权限，事件读取使用 space:audit:read。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class LocationPublishController : ControllerBase
{
    private readonly ILocationPublishService _svc;
    private readonly ISpaceAuditQueryService _auditQuery;
    private readonly ISpaceAuditWriter _auditWriter;

    public LocationPublishController(
        ILocationPublishService svc,
        ISpaceAuditQueryService auditQuery,
        ISpaceAuditWriter auditWriter)
    {
        _svc = svc;
        _auditQuery = auditQuery;
        _auditWriter = auditWriter;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    // ── POST /api/space/floor/{id}/publish ────────────────────────────────

    /// <summary>整层/库区发布（ch04 §4）。</summary>
    [HttpPost("floor/{id:guid}/publish")]
    [RequirePermission("space-publish", "publish")]
    public async Task<IActionResult> PublishFloor(Guid id, [FromBody] PublishFloorRequest? req)
    {
        try
        {
            var count = await _svc.PublishFloorAsync(id, req?.ZoneId, CurrentUser);
            return Ok2(new { published = count });
        }
        catch (DbUpdateConcurrencyException)
        {
            // ch04 §11 E-SPACE-009：RowVersion 乐观并发冲突 → 409（此前落到 500）
            return Conflict(new { code = 409, message = "E-SPACE-009: 数据已被他人修改，请刷新重试" });
        }
    }

    // ── PUT /api/space/location/{id}/deactivate ───────────────────────────

    /// <summary>停用已发布库位（ch04 §6）。</summary>
    [HttpPut("location/{id:guid}/deactivate")]
    [RequirePermission("space-publish", "deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        try
        {
            await _svc.DeactivateAsync(id, CurrentUser);
            return Ok2();
        }
        catch (DbUpdateConcurrencyException)
        {
            // ch04 §11 E-SPACE-009：RowVersion 乐观并发冲突 → 409（此前落到 500）
            return Conflict(new { code = 409, message = "E-SPACE-009: 数据已被他人修改，请刷新重试" });
        }
    }

    // ── POST /api/space/location/adopt ────────────────────────────────────

    /// <summary>存量采纳导入（ch04 §8.1）。</summary>
    [HttpPost("location/adopt")]
    [RequirePermission("space-publish", "adopt")]
    public async Task<IActionResult> Adopt([FromBody] AdoptRequest req)
    {
        try
        {
            var items = req.Items.Select(i => (i.Code, (Dictionary<string, object?>?)i.Attrs));
            var (imported, skipped) = await _svc.AdoptAsync(items, CurrentUser);
            return Ok2(new { imported, skipped });
        }
        catch (DbUpdateConcurrencyException)
        {
            // ch04 §11 E-SPACE-009：RowVersion 乐观并发冲突 → 409（此前落到 500）
            return Conflict(new { code = 409, message = "E-SPACE-009: 数据已被他人修改，请刷新重试" });
        }
    }

    // ── GET /api/space/publish/events ─────────────────────────────────────

    /// <summary>列出 SPACE→WMS 集成事件（ch04 §2）。</summary>
    [HttpGet("publish/events")]
    [RequirePermission("space-audit", "read")]
    public async Task<IActionResult> ListEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var data = await _auditQuery.GetPublishEventsAsync(
            page,
            pageSize,
            ct);
        try
        {
            await _auditWriter.TryAppendAsync(
                new SpaceAuditEventInput(
                    "space.integration-event.read",
                    "IntegrationEvent",
                    null,
                    SpaceAuditOutcome.Succeeded,
                    Evidence: new SpaceAuditEvidence(
                        PermissionCode: "space-audit:read",
                        AuthorizationResult: "Allowed",
                        ItemCount: data.Count),
                    ClientType: "Web"),
                ct);
        }
        catch
        {
            // Authorized read results are already safe DTOs. Audit storage
            // failure must not turn this compatibility route into an outage.
        }

        return Ok2(data);
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
