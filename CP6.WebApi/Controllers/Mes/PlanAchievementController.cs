using CP6.Core.Services.Mes;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>
/// 生産計画達成率レポート（Gap 3.3）。
/// </summary>
[ApiController]
[Route("api/mes/plan-achievement")]
[Authorize]
public class PlanAchievementController : ControllerBase
{
    private readonly IPlanAchievementService _service;

    public PlanAchievementController(IPlanAchievementService service)
    {
        _service = service;
    }

    [HttpPost("summary")]
    public async Task<IActionResult> Summary([FromBody] PlanAchievementQuery? query, CancellationToken ct)
    {
        var data = await _service.GetSummaryAsync(query ?? new PlanAchievementQuery(), ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    [HttpPost("export-csv")]
    public async Task<IActionResult> ExportCsv([FromBody] PlanAchievementQuery? query, CancellationToken ct)
    {
        var bytes = await _service.ExportCsvAsync(query ?? new PlanAchievementQuery(), ct);
        return File(bytes, "text/csv; charset=utf-8", "plan-achievement.csv");
    }
}
