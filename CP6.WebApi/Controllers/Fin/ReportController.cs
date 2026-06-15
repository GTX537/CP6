using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 财务报表 REST —— 财务章08。/api/fin/report。资产负债表(期末余额)/损益表(本期发生)从科目余额算，与总账一致。
/// 读端点仅 [Authorize]（菜单级 Sys_RoleMenu 已控可见性，与 D-2 约定一致）。
/// </summary>
[ApiController]
[Route("api/fin/report")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IBalanceSheetService _bs;
    private readonly IIncomeStatementService _is;

    public ReportController(IBalanceSheetService bs, IIncomeStatementService incomeStatement)
    {
        _bs = bs;
        _is = incomeStatement;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    /// <summary>资产负债表（指定期间期末余额）。</summary>
    [HttpGet("balance-sheet/{periodId}")]
    public async Task<IActionResult> BalanceSheet(Guid periodId)
    {
        try { return Ok2(await _bs.BuildAsync(periodId)); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    /// <summary>损益表（指定期间本期发生）。</summary>
    [HttpGet("income-statement/{periodId}")]
    public async Task<IActionResult> IncomeStatement(Guid periodId)
    {
        try { return Ok2(await _is.BuildAsync(periodId)); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }
}
