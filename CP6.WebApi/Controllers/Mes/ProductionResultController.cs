using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>
/// MSBBME040 / 050 — 製造実績 入力 / 一覧 Web API
/// </summary>
[ApiController]
[Route("api/mes/production-results")]
[Authorize]
public class ProductionResultController : ControllerBase
{
    private readonly IProductionResultService _service;

    public ProductionResultController(IProductionResultService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>ME050 — 一覧検索 GET /api/mes/production-results</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] ProductionResultSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>ME040 — 指図 + 全工程の現在状況取得 GET /api/mes/production-results/work-order/{no}</summary>
    [HttpGet("work-order/{no}")]
    public async Task<IActionResult> GetWorkOrderSummary(string no)
    {
        var dto = await _service.GetWorkOrderSummaryAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "ME-MSG-040" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>ME040 — 工程開始 POST /api/mes/production-results/start</summary>
    [HttpPost("start")]
    [RequirePermission("mes-production-result", "start")]
    public Task<IActionResult> Start([FromBody] ProductionResultRequest req) => Run(req, _service.StartAsync);

    /// <summary>ME040 — 工程中断 POST /api/mes/production-results/suspend</summary>
    [HttpPost("suspend")]
    [RequirePermission("mes-production-result", "suspend")]
    public Task<IActionResult> Suspend([FromBody] ProductionResultRequest req) => Run(req, _service.SuspendAsync);

    /// <summary>ME040 — 中断解除 POST /api/mes/production-results/resume</summary>
    [HttpPost("resume")]
    [RequirePermission("mes-production-result", "suspend")]
    public Task<IActionResult> Resume([FromBody] ProductionResultRequest req) => Run(req, _service.ResumeAsync);

    /// <summary>ME040 — 工程完了 POST /api/mes/production-results/complete</summary>
    [HttpPost("complete")]
    [RequirePermission("mes-production-result", "complete")]
    public Task<IActionResult> Complete([FromBody] ProductionResultRequest req) => Run(req, _service.CompleteAsync);

    /// <summary>ME040 — 数量報告 POST /api/mes/production-results</summary>
    [HttpPost]
    [RequirePermission("mes-production-result", "report")]
    public Task<IActionResult> Report([FromBody] ProductionResultRequest req) => Run(req, _service.ReportAsync);

    private async Task<IActionResult> Run(ProductionResultRequest req, Func<ProductionResultRequest, string?, Task<string>> action)
    {
        try
        {
            var resultNo = await action(req, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { resultNo } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }
}
