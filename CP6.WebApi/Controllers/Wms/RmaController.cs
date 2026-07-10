using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM150 — RMA 返品管理 Web API</summary>
[ApiController]
[Route("api/wms/rma")]
[Authorize]
public class RmaController : ControllerBase
{
    private readonly IRmaService _svc;
    public RmaController(IRmaService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] RmaSearchQuery q)
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

    [HttpPost]
    [RequirePermission("wms-rma", "add")]
    public async Task<IActionResult> Create([FromBody] RmaDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { rmaNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/receive")]
    [RequirePermission("wms-rma", "receive")]
    public async Task<IActionResult> Receive(string no)
    {
        try
        {
            await _svc.ReceiveAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/start-inspection")]
    [RequirePermission("wms-rma", "inspect")]
    public async Task<IActionResult> StartInspection(string no)
    {
        try
        {
            await _svc.StartInspectionAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/judge")]
    [RequirePermission("wms-rma", "judge")]
    public async Task<IActionResult> Judge(string no, [FromBody] List<RmaDispositionInput> inputs)
    {
        try
        {
            await _svc.JudgeAndDisposeAsync(no, inputs, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/close")]
    [RequirePermission("wms-rma", "close")]
    public async Task<IActionResult> Close(string no)
    {
        try
        {
            await _svc.CloseAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-rma", "cancel")]
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
