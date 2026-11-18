using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.WebApi.Controllers.Erp;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[OrderOwnerResult]
[Route("api/[controller]")]
[Authorize]
public class OperLogController : ControllerBase
{
    private readonly CP6Context _context;
    private readonly OperationLogOwnerV1? _owner;
    private readonly AuditBrowseV1? _browse;
    private OperationLogOwnerV1 Owner => _owner ?? throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503);

    public OperLogController(CP6Context context, OperationLogOwnerV1? owner = null, AuditBrowseV1? browse = null)
    {
        _context = context;
        _owner = owner;
        _browse = browse;
    }

    [HttpGet]
    [RequirePermission("operlog", "query")]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 20, string? keyword = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? cursor = null)
    {
        if (_context.Database.IsNpgsql())
            return Ok(await (_browse ?? throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE",503))
                .ListAsync(AuditRetentionV1.Telemetry, new(Keyword: keyword, FromUtc: fromUtc, ToUtc: toUtc),
                    page, pageSize, cursor, HttpContext.RequestAborted));
        var query = _context.Sys_OperLogs.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(l => (l.UserName != null && l.UserName.Contains(keyword))
                || (l.RequestUrl != null && l.RequestUrl.Contains(keyword))
                || (l.Controller != null && l.Controller.Contains(keyword)));

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(l => l.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    /// <summary>
    /// 清空日志
    /// </summary>
    [HttpDelete]
    [RequirePermission("operlog", "delete")]
    public async Task<IActionResult> Clear([FromHeader(Name = "X-Audit-Plan-Id")] Guid? planId = null)
    {
        if (!_context.Database.IsNpgsql())
            throw new OrderPriceCorrectionException("AUDIT_RETENTION_AUTHORITY_UNAVAILABLE", 503);
        var result = await Owner.ClearAsync(planId, HttpContext.RequestAborted);
        return StatusCode(result.State == "COMPLETED" ? 200 : 409, new { count = result.Affected, receipt = result });
    }

    [HttpGet("legacy-clear")]
    [RequirePermission("operlog", "delete")]
    public async Task<IActionResult> ReadLegacyClear()
        => Ok(await Owner.ReadLegacyAsync(HttpContext.RequestAborted));

    [HttpGet("retention/policies/{key}")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> GetPolicy(string key)
        => Ok(await Owner.PolicyAsync(key, HttpContext.RequestAborted));

    [HttpPut("retention/policies/{key}")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> PublishPolicy(string key, AuditRetentionPolicyChangeV1 change)
        => Ok(await Owner.PublishPolicyAsync(key, change, HttpContext.RequestAborted));

    [HttpPost("retention/safeguards/{key}")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> Safeguard(string key, AuditSafeguardChangeV1 change)
        => Ok(new { version = await Owner.SafeguardAsync(key, change, HttpContext.RequestAborted) });

    [HttpPost("retention/plans/{id:guid}")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] string retentionClass)
        => Ok(await Owner.PreviewAsync(retentionClass, id, HttpContext.RequestAborted));

    [HttpGet("retention/plans/{id:guid}")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> ReadPlan(Guid id)
        => Ok(await Owner.ReadPlanAsync(id, HttpContext.RequestAborted));

    [HttpPost("retention/plans/{id:guid}/execute")]
    [RequirePermission("operlog", "retention-manage")]
    public async Task<IActionResult> ExecutePlan(Guid id)
        => Ok(await Owner.ExecutePlanAsync(id, HttpContext.RequestAborted));
}
