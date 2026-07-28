using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM110 — スロッティング最適化 Web API</summary>
[ApiController]
[Route("api/wms/slotting")]
[Authorize]
public class SlottingController : ControllerBase
{
    private readonly ISlottingService _svc;
    public SlottingController(ISlottingService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? warehouseCd, [FromQuery] int? status)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(warehouseCd, status) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>分析実行 → SlottingPlan 生成</summary>
    [HttpPost("analyze")]
    [RequirePermission("wms-slotting", "analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest req)
    {
        try
        {
            var no = await _svc.AnalyzeAsync(req.WarehouseCd, req.AnalysisDays, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { slottingPlanNo = no } });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/approve")]
    [RequirePermission("wms-slotting", "approve")]
    public async Task<IActionResult> Approve(string no)
    {
        try
        {
            var generated = await _svc.ApproveAsync(no, CurrentUser);
            return Ok(new
            {
                code = 0,
                message = "WM-MSG-071",
                data = new { generated }
            });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-slotting", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try { await _svc.CancelAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class AnalyzeRequest
    {
        public string WarehouseCd { get; set; } = string.Empty;
        public int AnalysisDays { get; set; } = 90;
    }
}
