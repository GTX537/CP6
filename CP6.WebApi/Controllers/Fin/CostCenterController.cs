using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>成本中心查找端点（A5 D2 预算维度）。/api/fin/cost-center。只读 lookup，无 RequirePermission（镜像 GlAccountController GET）。</summary>
[ApiController]
[Route("api/fin/cost-center")]
[Authorize]
public class CostCenterController : ControllerBase
{
    private readonly CP6Context _db;
    public CostCenterController(CP6Context db) => _db = db;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    /// <summary>成本中心列表（includeInactive=true 含停用）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        var q = _db.CostCenters.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var data = await q.OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Name, c.Type, c.IsActive })
            .ToListAsync();
        return Ok2(data);
    }
}
