using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 预计流程 REST（Phase B）。/api/oa/forecast — 发起前预览后续审批路径。
/// 给发起方提供"还差几步/谁审"的全路径预计，不产生实例。
/// </summary>
[ApiController]
[Route("api/oa/forecast")]
[Authorize]
public class ForecastController : LocalizedControllerBase
{
    private readonly IForecastService _forecast;
    private readonly ICurrentPermissionContext _ctx;

    public ForecastController(IForecastService forecast, ICurrentPermissionContext ctx)
    {
        _forecast = forecast;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 发起前预览 ──

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            var result = await _forecast.ForecastAsync(r.FlowKey, r.VarsJson ?? "{}", me, fromNodeId: null);
            return Ok2(result);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record PreviewReq(string FlowKey, string? VarsJson);
}
