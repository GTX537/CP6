using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>会计期间 / 月结 REST —— 财务章02 §1/§3。/api/fin/period。</summary>
[ApiController]
[Route("api/fin/period")]
[Authorize]
public class PeriodController : ControllerBase
{
    private readonly IFiscalPeriodService _periods;
    private readonly IPeriodCloseService _close;
    public PeriodController(IFiscalPeriodService periods, IPeriodCloseService close)
    {
        _periods = periods;
        _close = close;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    /// <summary>期间列表（可按年）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? year) => Ok2(await _periods.ListAsync(year));

    /// <summary>结账前检查清单（不落库，仅返回可否结账）。</summary>
    [HttpGet("{id}/pre-close-check")]
    public async Task<IActionResult> PreCheck(Guid id) => Fin(await _close.PreCloseCheckAsync(id));

    /// <summary>结账（锁期）。</summary>
    [HttpPost("{id}/close")]
    [RequirePermission("fin-period", "close")]
    public async Task<IActionResult> Close(Guid id) => Fin(await _close.CloseAsync(id, CurrentUser));

    /// <summary>反结账（危险动作，限高权限）。</summary>
    [HttpPost("{id}/reopen")]
    [RequirePermission("fin-period", "reopen")]
    public async Task<IActionResult> Reopen(Guid id) => Fin(await _close.ReopenAsync(id, CurrentUser));

    /// <summary>年结（损益结转 3103→3104 + 锁年）。财年 12 期须全部结账。</summary>
    [HttpPost("year-close/{fiscalYear:int}")]
    [RequirePermission("fin-period", "year-close")]
    public async Task<IActionResult> YearClose(int fiscalYear) => Fin(await _close.YearCloseAsync(fiscalYear, CurrentUser));

    /// <summary>反年结（危险动作，红冲年结凭证 + 12 期回 Closed，限高权限）。</summary>
    [HttpPost("reopen-year/{fiscalYear:int}")]
    [RequirePermission("fin-period", "reopen-year")]
    public async Task<IActionResult> ReopenYear(int fiscalYear) => Fin(await _close.ReopenYearAsync(fiscalYear, CurrentUser));
}
