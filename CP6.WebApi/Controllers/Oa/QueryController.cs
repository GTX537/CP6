using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 表单查询 REST（Phase C）。/api/oa/query — 跨流程全文检索 / 多条件筛选。
/// </summary>
[ApiController]
[Route("api/oa/query")]
[Authorize]
public class QueryController : LocalizedControllerBase
{
    private readonly IInboxService _inbox;
    private readonly CP6.Core.Services.Sys.ICurrentPermissionContext _context;
    private readonly IDelegateService _delegates;

    public QueryController(IInboxService inbox, CP6.Core.Services.Sys.ICurrentPermissionContext context,
        IDelegateService delegates)
    {
        _inbox = inbox;
        _context = context;
        _delegates = delegates;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 多条件查询（FormQueryFilter 直接接收）──

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] FormQueryFilter filter)
    {
        try
        {
            var actual = (await _context.GetAsync()).UserId;
            var effective = actual;
            var header = Request.Headers["X-Acting-As"].ToString();
            if (Guid.TryParse(header, out var actingAs) && actingAs != Guid.Empty && actingAs != actual)
            {
                await _delegates.AssertActiveGrantAsync(actual, actingAs);
                effective = actingAs;
            }
            return Ok2(await _inbox.QueryAsync(effective, filter));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
