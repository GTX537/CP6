using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>MSBBME010 — 生産計画ボード Web API</summary>
[ApiController]
[Route("api/mes/planning-board")]
[Authorize]
public class PlanningBoardController : ControllerBase
{
    private readonly IPlanningBoardService _service;
    public PlanningBoardController(IPlanningBoardService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>ガントチャート用 Bar 一覧</summary>
    [HttpGet]
    public async Task<IActionResult> GetBars([FromQuery] PlanningBoardQuery query)
    {
        var bars = await _service.GetBarsAsync(query);
        return Ok(new { code = 0, message = "OK", data = bars });
    }

    /// <summary>KPI サマリ</summary>
    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi([FromQuery] PlanningBoardQuery query)
    {
        var kpi = await _service.GetKpiAsync(query);
        return Ok(new { code = 0, message = "OK", data = kpi });
    }

    /// <summary>ドラッグ後 日時更新</summary>
    [HttpPut("reschedule")]
    public async Task<IActionResult> Reschedule([FromBody] RescheduleRequest req)
    {
        try
        {
            await _service.RescheduleAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>自動配置（PA050 ManufOrderPrio + 納期で並び替え）</summary>
    [HttpPost("auto-arrange")]
    public async Task<IActionResult> AutoArrange([FromBody] AutoArrangeRequest req)
    {
        var changed = await _service.AutoArrangeAsync(req, CurrentUser);
        return Ok(new { code = 0, message = "ME-MSG-041", data = new { changed } });
    }
}
