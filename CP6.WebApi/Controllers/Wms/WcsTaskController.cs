using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM310 — WCS タスク Web API</summary>
[ApiController]
[Route("api/wms/wcs-task")]
[Authorize]
public class WcsTaskController : ControllerBase
{
    private readonly IWcsService _svc;
    public WcsTaskController(IWcsService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] WcsTaskSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var t = await _svc.GetAsync(no);
        if (t == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = t });
    }

    [HttpPost]
    [RequirePermission("wms-wcs-task", "add")]
    public async Task<IActionResult> Create([FromBody] WcsTaskDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { taskNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/dispatch")]
    [RequirePermission("wms-wcs-task", "dispatch")]
    public async Task<IActionResult> Dispatch(string no, [FromBody] DispatchReq req)
    {
        try { await _svc.DispatchAsync(no, req.DeviceCd, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/start")]
    [RequirePermission("wms-wcs-task", "start")]
    public async Task<IActionResult> Start(string no)
    {
        try { await _svc.StartAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/complete")]
    [RequirePermission("wms-wcs-task", "complete")]
    public async Task<IActionResult> Complete(string no)
    {
        try { await _svc.CompleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/fail")]
    [RequirePermission("wms-wcs-task", "fail")]
    public async Task<IActionResult> Fail(string no, [FromBody] FailReq req)
    {
        try { await _svc.FailAsync(no, req.ErrorMessage, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{no}")]
    [RequirePermission("wms-wcs-task", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try { await _svc.DeleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class DispatchReq { public string DeviceCd { get; set; } = string.Empty; }
    public class FailReq { public string ErrorMessage { get; set; } = string.Empty; }
}
