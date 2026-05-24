using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM090 — 棚卸（実地棚卸）Web API
/// </summary>
[ApiController]
[Route("api/wms/stock-take")]
[Authorize]
public class StockTakeController : ControllerBase
{
    private readonly IStockTakeService _svc;
    public StockTakeController(IStockTakeService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] StockTakeSearchQuery q)
    {
        var list = await _svc.SearchAsync(q);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _svc.GetAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>計画作成（スナップショット取得）</summary>
    [HttpPost("plan")]
    public async Task<IActionResult> CreatePlan([FromBody] StockTakePlanDto dto)
    {
        try
        {
            var no = await _svc.CreatePlanAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { stockTakeNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>カウント開始（status 0→1）</summary>
    [HttpPost("{no}/start-count")]
    public async Task<IActionResult> StartCount(string no)
    {
        try
        {
            await _svc.StartCountAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>カウント値一括更新</summary>
    [HttpPut("{no}/counts")]
    public async Task<IActionResult> UpdateCounts(string no, [FromBody] List<StockTakeCountInput> inputs)
    {
        try
        {
            await _svc.UpdateCountsAsync(no, inputs ?? new(), CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>差異確認移行（status 1→2/3）</summary>
    [HttpPost("{no}/submit")]
    public async Task<IActionResult> Submit(string no)
    {
        try
        {
            await _svc.SubmitForReviewAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>承認 + ADJ 反映（status →4 完了）</summary>
    [HttpPost("{no}/approve")]
    public async Task<IActionResult> Approve(string no)
    {
        try
        {
            await _svc.ApproveAndApplyAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>取消（status →9）</summary>
    [HttpPost("{no}/cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try
        {
            await _svc.CancelAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
