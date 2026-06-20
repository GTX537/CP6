using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 预算报告 REST —— 财务 A5。/api/fin/budget/report。
/// 预算 vs 实际对比报告 + 凭证过账前预控预检。
/// </summary>
[ApiController]
[Route("api/fin/budget/report")]
[Authorize]
public class BudgetReportController : ControllerBase
{
    private readonly IBudgetReportService _svc;

    public BudgetReportController(IBudgetReportService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    [HttpGet("vs-actual")]
    [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> VsActual(
        [FromQuery] int fiscalYear,
        [FromQuery] Guid? versionId,
        [FromQuery] int from = 1,
        [FromQuery] int to = 12)
        => Ok2(await _svc.BuildVsActualAsync(fiscalYear, versionId, from, to));

    [HttpPost("pre-check")]
    [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> PreCheck([FromBody] JournalEntry entry)
        => Ok2(await _svc.PreCheckAsync(entry));
}
