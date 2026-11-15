using System.Data.Common;
using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>会计期间 / 月结 REST —— 财务章02 §1/§3。/api/fin/period。</summary>
[ApiController]
[Route("api/fin/period")]
[Authorize]
public class PeriodController : ControllerBase
{
    private readonly IFiscalPeriodService _periods;
    private readonly IPeriodCloseService _close;
    private readonly IamAdministrationV1 _authority;
    public PeriodController(IFiscalPeriodService periods, IPeriodCloseService close, IamAdministrationV1 authority)
    {
        _periods = periods;
        _close = close;
        _authority = authority;
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
    public Task<IActionResult> Close(Guid id) => WriteAsync("close", () => _close.CloseAsync(id, CurrentUser));

    /// <summary>反结账（危险动作，限高权限）。</summary>
    [HttpPost("{id}/reopen")]
    [RequirePermission("fin-period", "reopen")]
    public Task<IActionResult> Reopen(Guid id) => WriteAsync("reopen", () => _close.ReopenAsync(id, CurrentUser));

    /// <summary>年结（损益结转 3103→3104 + 锁年）。财年 12 期须全部结账。</summary>
    [HttpPost("year-close/{fiscalYear:int}")]
    [RequirePermission("fin-period", "year-close")]
    public Task<IActionResult> YearClose(int fiscalYear) => WriteAsync("year-close", () => _close.YearCloseAsync(fiscalYear, CurrentUser));

    /// <summary>反年结（危险动作，红冲年结凭证 + 12 期回 Closed，限高权限）。</summary>
    [HttpPost("reopen-year/{fiscalYear:int}")]
    [RequirePermission("fin-period", "reopen-year")]
    public Task<IActionResult> ReopenYear(int fiscalYear) => WriteAsync("reopen-year", () => _close.ReopenYearAsync(fiscalYear, CurrentUser));

    private async Task<IActionResult> WriteAsync(string action, Func<Task<FinResult>> apply)
    {
        if (!_authority.IsPostgreSql)
            return Fin(await apply());

        try
        {
            return Fin(await IamAdministrationHttpV1.ExecuteWriteAsync(HttpContext, _authority, $"fin-period:{action}",
                async (_, _) =>
                {
                    var result = await apply();
                    // Period transitions can save vouchers and counters before a later business rejection.
                    if (!result.Ok) throw new PeriodWriteRejected(result);
                    return result;
                }));
        }
        catch (PeriodWriteRejected error)
        {
            return Fin(error.Result);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            return StatusCode(error.Status, new { code = error.Status, message = error.Message });
        }
        catch (Exception error) when (error is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_FENCE_UNAVAILABLE" });
        }
    }

    private sealed class PeriodWriteRejected(FinResult result) : Exception(result.Code)
    {
        public FinResult Result { get; } = result;
    }
}
