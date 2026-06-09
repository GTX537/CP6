using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OperLogController : ControllerBase
{
    private readonly CP6Context _context;

    public OperLogController(CP6Context context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 20, string? keyword = null)
    {
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
    public async Task<IActionResult> Clear()
    {
        _context.Sys_OperLogs.RemoveRange(_context.Sys_OperLogs);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}
