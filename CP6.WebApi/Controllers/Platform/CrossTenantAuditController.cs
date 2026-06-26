using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// 跨租户安全审计查询（多租户合规 #5 块④ R10）。仅平台超管放行
/// （<see cref="RequirePlatformAdminAttribute"/> 三道闸），与 impersonation 彻底解耦——
/// 平台超管直接以本人身份带外查询全租户 <see cref="CP6.Entity.DomainModels.Sys.Sys_SecurityLog"/>。
/// <para>关键：<c>IgnoreQueryFilters()</c> 绕行级租户过滤（带外），故能横跨所有租户取证。
/// 过滤维度 <c>tenantCode</c> 对齐 <c>RequestTenantCode</c>（spec §5.4：平台审计行此字段填**目标租户码**）。</para>
/// <para>与租户内的 <see cref="CP6.WebApi.Controllers.Sys.SecurityLogController"/> 区分：后者按当前租户行级隔离（admin 仅见本租户），
/// 此处带外全租户（仅平台超管）。</para>
/// </summary>
[ApiController]
[Route("api/platform/audit")]
[Authorize]
[RequirePlatformAdmin]
public class CrossTenantAuditController : ControllerBase
{
    private readonly CP6Context _db;
    public CrossTenantAuditController(CP6Context db) => _db = db;

    /// <summary>带外跨租户分页查询。支持按目标租户码 / 事件类型 / 时间段筛选。</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? tenantCode,
        [FromQuery] int? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);                 // 防 page<=0 → 负数 Skip 抛异常
        pageSize = Math.Clamp(pageSize, 1, 200);  // 防一次拉全表

        var query = _db.Sys_SecurityLogs.IgnoreQueryFilters().AsQueryable();   // 带外：绕行级过滤，跨租户取证

        if (!string.IsNullOrWhiteSpace(tenantCode))
            query = query.Where(x => x.RequestTenantCode == tenantCode);       // 目标租户码（spec §5.4）
        if (eventType.HasValue)
            query = query.Where(x => x.EventType == eventType.Value);
        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.CreatedAt < to.Value.AddDays(1));       // to 含当日（右开到次日零点）

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.UserName,
                x.RequestTenantCode,
                x.EventType,
                x.Reason,
                x.ClientIp,
                x.UserAgent,
                x.CreatedAt
            })
            .ToListAsync();

        return Ok(new { rows, total });
    }
}
