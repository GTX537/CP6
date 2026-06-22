using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 安全审计日志查询（S 类认证加固 T8）。Sys_SecurityLog 为多租户实体，全局查询过滤自动按当前租户隔离
/// （admin 仅见本租户安全事件）。支持按事件类型 / 用户名 / 时间段筛选 + 分页。
/// </summary>
[ApiController]
[Route("api/sys/security-log")]
[Authorize]
public class SecurityLogController : ControllerBase
{
    private readonly CP6Context _db;
    public SecurityLogController(CP6Context db) => _db = db;

    /// <summary>分页查询安全日志。资源键 sys-security-log（MenuKey 由 RoutePath 派生），操作点 query。</summary>
    [HttpGet]
    [RequirePermission("sys-security-log", "query")]
    public async Task<IActionResult> GetList(
        [FromQuery] int? eventType,
        [FromQuery] string? userName,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _db.Sys_SecurityLogs.AsQueryable();

        if (eventType.HasValue)
            query = query.Where(x => x.EventType == eventType.Value);
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(x => x.UserName != null && x.UserName.Contains(userName));
        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.CreatedAt < to.Value.AddDays(1));   // to 含当日（右开到次日零点）

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
