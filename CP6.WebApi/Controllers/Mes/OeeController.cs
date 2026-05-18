using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>MES Phase 4 — OEE 分析 Web API</summary>
[ApiController]
[Route("api/mes/oee")]
[Authorize]
public class OeeController : ControllerBase
{
    private readonly IOeeService _service;
    public OeeController(IOeeService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] OeeSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>本日リアルタイム OEE（途中計算、保存しない）</summary>
    [HttpGet("today")]
    public async Task<IActionResult> Today([FromQuery] string? machineCd)
    {
        var r = await _service.CalculateTodayAsync(machineCd);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>過去 N 日 OEE 推移（折線用、設備別）</summary>
    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] int days = 30, [FromQuery] string? machineCd = null)
    {
        var r = await _service.GetTrendAsync(days, machineCd);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>指定日 再計算（保存）</summary>
    [HttpPost("recalculate")]
    public async Task<IActionResult> Recalculate([FromBody] OeeRecalcRequest req)
    {
        var n = await _service.RecalculateAsync(req, CurrentUser);
        return Ok(new { code = 0, message = "ME-MSG-041", data = new { recalculated = n } });
    }
}
