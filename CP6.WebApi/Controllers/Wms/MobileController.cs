using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM300 — モバイル作業指示（RFハンディ）Web API</summary>
[ApiController]
[Route("api/wms/mobile")]
[Authorize]
public class MobileController : ControllerBase
{
    private readonly IMobileService _svc;
    public MobileController(IMobileService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>当該作業者の指示一覧。assignedTo 未指定時はログインユーザを既定にする。</summary>
    [HttpGet("task")]
    public async Task<IActionResult> GetTasks([FromQuery] MobileTaskQuery q)
    {
        if (string.IsNullOrWhiteSpace(q.AssignedTo)) q.AssignedTo = CurrentUser;
        return Ok(new { code = 0, message = "OK", data = await _svc.GetTasksAsync(q) });
    }

    [HttpGet("task/{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var t = await _svc.GetAsync(no);
        if (t == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = t });
    }

    [HttpPost("task")]
    public async Task<IActionResult> Create([FromBody] MobileTaskDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { mobileTaskNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("task/{no}/start")]
    public async Task<IActionResult> Start(string no)
    {
        try { await _svc.StartAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>バーコード 1 件を解決（ロケーション/製品/不明）。</summary>
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] MobileScanRequest req)
        => Ok(new { code = 0, message = "OK", data = await _svc.ScanAsync(req) });

    /// <summary>作業完了。MOVE は実在庫を動かす。</summary>
    [HttpPost("task/{no}/done")]
    public async Task<IActionResult> Done(string no, [FromBody] MobileCompleteRequest? req)
    {
        try
        {
            await _svc.CompleteAsync(no, req ?? new MobileCompleteRequest(), CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("task/{no}/cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try { await _svc.CancelAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
