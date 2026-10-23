using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM130 — クロスドッキング Web API</summary>
[ApiController]
[Route("api/wms/cross-dock")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CrossDockController : ControllerBase
{
    private readonly ICrossDockService _svc;
    public CrossDockController(ICrossDockService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    [RequirePermission("wms-cross-dock", "query")]
    public async Task<IActionResult> Search([FromQuery] CrossDockSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    [RequirePermission("wms-cross-dock", "query")]
    public async Task<IActionResult> Get(string no)
    {
        var o = await _svc.GetAsync(no);
        if (o == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = o });
    }

    [HttpGet("owner-v1")]
    [RequirePermission("wms-cross-dock", "query")]
    public async Task<IActionResult> SearchOwner([FromQuery] CrossDockSearchQuery q) =>
        Ok(new { code = 0, message = "OK", data = await _svc.SearchOwnerAsync(q) });

    [HttpGet("owner-v1/{no}")]
    [RequirePermission("wms-cross-dock", "query")]
    public async Task<IActionResult> GetOwner(string no)
    {
        var row = await _svc.GetOwnerAsync(no);
        return row == null ? NotFound(new { code = 404, message = "OBJECT_NOT_FOUND" }) : Ok(new { code = 0, message = "OK", data = row });
    }

    [HttpPost]
    [RequirePermission("wms-cross-dock", "add")]
    public async Task<IActionResult> Create([FromBody] CrossDockOrderDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { xdockNo = no } });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/execute")]
    [RequirePermission("wms-cross-dock", "execute")]
    public async Task<IActionResult> Execute(string no)
    {
        try { await _svc.ExecuteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-cross-dock", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try { await _svc.CancelAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
