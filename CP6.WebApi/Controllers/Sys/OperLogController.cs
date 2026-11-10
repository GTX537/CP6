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
    private OperationLogOwnerV1 Owner => _owner ?? throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503);

    public OperLogController(CP6Context context, OperationLogOwnerV1? owner = null)
    {
        _context = context;
        _owner = owner;
    }

    [HttpGet]
    [RequirePermission("operlog", "query")]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 20, string? keyword = null)
    {
        if (_context.Database.IsNpgsql())
            return Ok(await Owner.ListAsync(page, pageSize, keyword, HttpContext.RequestAborted));
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
    public async Task<IActionResult> Clear()
    {
        if (_context.Database.IsNpgsql())
            return Ok(new { count = await Owner.ClearAsync(HttpContext.RequestAborted) });
        _context.Sys_OperLogs.RemoveRange(_context.Sys_OperLogs);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}
