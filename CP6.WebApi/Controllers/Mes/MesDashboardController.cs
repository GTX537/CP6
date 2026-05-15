using CP6.Core.Services.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>MSBBME090 — MES ダッシュボード Web API</summary>
[ApiController]
[Route("api/mes/dashboard")]
[Authorize]
public class MesDashboardController : ControllerBase
{
    private readonly IMesDashboardService _service;
    public MesDashboardController(IMesDashboardService service) => _service = service;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var s = await _service.GetSummaryAsync();
        return Ok(new { code = 0, message = "OK", data = s });
    }

    [HttpGet("process-progress")]
    public async Task<IActionResult> ProcessProgress()
    {
        var rows = await _service.GetProcessProgressAsync();
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpGet("delay-alerts")]
    public async Task<IActionResult> DelayAlerts([FromQuery] int top = 50)
    {
        var rows = await _service.GetDelayAlertsAsync(top);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpGet("daily-trend")]
    public async Task<IActionResult> DailyTrend([FromQuery] int days = 30)
    {
        var rows = await _service.GetDailyTrendAsync(days);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpGet("defect-top5")]
    public async Task<IActionResult> DefectTop5()
    {
        var rows = await _service.GetDefectTop5Async();
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpGet("recent-completed")]
    public async Task<IActionResult> RecentCompleted([FromQuery] int top = 10)
    {
        var rows = await _service.GetRecentCompletedAsync(top);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpGet("machine-heatmap")]
    public async Task<IActionResult> MachineHeatmap([FromQuery] int days = 7)
    {
        var rows = await _service.GetMachineHeatmapAsync(days);
        return Ok(new { code = 0, message = "OK", data = rows });
    }
}
