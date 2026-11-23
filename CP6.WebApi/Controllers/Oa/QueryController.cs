using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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

    private async Task<(Guid Actual, Guid Effective)> QueryActor()
    {
        var actual = (await _context.GetAsync()).UserId;
        var header = Request.Headers["X-Acting-As"].ToString();
        if (!Guid.TryParse(header, out var actingAs) || actingAs == Guid.Empty || actingAs == actual)
            return (actual, actual);
        await _delegates.AssertActiveGrantAsync(actual, actingAs);
        return (actual, actingAs);
    }

    // ── 多条件查询（FormQueryFilter 直接接收）──

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] FormQueryFilter filter)
    {
        try
        {
            var actor = await QueryActor();
            return Ok2(await _inbox.QueryAsync(actor.Effective, filter));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("history/context")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> HistoryContext(CancellationToken ct)
    {
        try
        {
            await QueryActor();
            return Ok2(await _inbox.LegacyHistoryContextAsync(ct));
        }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("history/search")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> HistorySearch([FromBody] LegacyFormHistoryFilter filter,
        [FromServices] IDataProtectionProvider protection, CancellationToken ct)
    {
        try
        {
            var actor = await QueryActor();
            return Ok2(await _inbox.LegacyHistoryAsync(actor.Actual, actor.Effective, filter, protection, ct));
        }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
