using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM900 — 帳票センター Web API</summary>
[ApiController]
[Route("api/wms/report-center")]
[Authorize]
public class ReportCenterController : ControllerBase
{
    private readonly IReportCenterService _svc;
    public ReportCenterController(IReportCenterService svc) => _svc = svc;

    [HttpGet("monthly-stock")]
    public async Task<IActionResult> MonthlyStock([FromQuery] string yearMonth, [FromQuery] string? warehouseCd)
    {
        try { return Ok(new { code = 0, message = "OK", data = await _svc.MonthlyStockReportAsync(yearMonth, warehouseCd) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("monthly-stock/csv")]
    public async Task<IActionResult> MonthlyStockCsv([FromQuery] string yearMonth, [FromQuery] string? warehouseCd)
    {
        var data = await _svc.MonthlyStockReportAsync(yearMonth, warehouseCd);
        return File(_svc.ExportCsv(data), "text/csv; charset=utf-8", $"monthly-stock-{yearMonth}.csv");
    }

    [HttpGet("abc-analysis")]
    public async Task<IActionResult> AbcAnalysis([FromQuery] int analysisDays = 90, [FromQuery] string? warehouseCd = null)
        => Ok(new { code = 0, message = "OK", data = await _svc.AbcAnalysisAsync(analysisDays, warehouseCd) });

    [HttpGet("abc-analysis/csv")]
    public async Task<IActionResult> AbcAnalysisCsv([FromQuery] int analysisDays = 90, [FromQuery] string? warehouseCd = null)
    {
        var data = await _svc.AbcAnalysisAsync(analysisDays, warehouseCd);
        return File(_svc.ExportCsv(data), "text/csv; charset=utf-8", $"abc-analysis-{analysisDays}d.csv");
    }

    [HttpGet("dead-stock")]
    public async Task<IActionResult> DeadStock([FromQuery] int idleDays = 90, [FromQuery] string? warehouseCd = null)
        => Ok(new { code = 0, message = "OK", data = await _svc.DeadStockAsync(idleDays, warehouseCd) });

    [HttpGet("dead-stock/csv")]
    public async Task<IActionResult> DeadStockCsv([FromQuery] int idleDays = 90, [FromQuery] string? warehouseCd = null)
    {
        var data = await _svc.DeadStockAsync(idleDays, warehouseCd);
        return File(_svc.ExportCsv(data), "text/csv; charset=utf-8", $"dead-stock-{idleDays}d.csv");
    }

    [HttpGet("inbound-history")]
    public async Task<IActionResult> InboundHistory([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] string? warehouseCd, [FromQuery] string? productCd)
        => Ok(new { code = 0, message = "OK", data = await _svc.InboundHistoryAsync(fromDate, toDate, warehouseCd, productCd) });

    [HttpGet("inbound-history/csv")]
    public async Task<IActionResult> InboundHistoryCsv([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] string? warehouseCd, [FromQuery] string? productCd)
    {
        var data = await _svc.InboundHistoryAsync(fromDate, toDate, warehouseCd, productCd);
        return File(_svc.ExportCsv(data), "text/csv; charset=utf-8", $"inbound-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
    }

    [HttpGet("outbound-history")]
    public async Task<IActionResult> OutboundHistory([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] string? warehouseCd, [FromQuery] string? productCd)
        => Ok(new { code = 0, message = "OK", data = await _svc.OutboundHistoryAsync(fromDate, toDate, warehouseCd, productCd) });

    [HttpGet("outbound-history/csv")]
    public async Task<IActionResult> OutboundHistoryCsv([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] string? warehouseCd, [FromQuery] string? productCd)
    {
        var data = await _svc.OutboundHistoryAsync(fromDate, toDate, warehouseCd, productCd);
        return File(_svc.ExportCsv(data), "text/csv; charset=utf-8", $"outbound-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
    }
}
