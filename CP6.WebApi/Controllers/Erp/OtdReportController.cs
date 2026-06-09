using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController]
[Route("api/otd-report")]
[Authorize]
public class OtdReportController : ControllerBase
{
    private readonly IOtdReportService _service;

    public OtdReportController(IOtdReportService service)
    {
        _service = service;
    }

    [HttpPost("summary")]
    public async Task<IActionResult> Summary([FromBody] OtdReportQuery? query, CancellationToken ct)
    {
        var data = await _service.GetSummaryAsync(query ?? new OtdReportQuery(), ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    [HttpPost("export-csv")]
    public async Task<IActionResult> ExportCsv([FromBody] OtdReportQuery? query, CancellationToken ct)
    {
        var bytes = await _service.ExportCsvAsync(query ?? new OtdReportQuery(), ct);
        return File(bytes, "text/csv; charset=utf-8", "otd-report.csv");
    }
}
