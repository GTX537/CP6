using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM320 — 配送業者連携 Web API</summary>
[ApiController]
[Route("api/wms/carrier")]
[Authorize]
public class CarrierController : ControllerBase
{
    private readonly ICarrierService _svc;
    public CarrierController(ICarrierService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] CarrierSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var s = await _svc.GetAsync(no);
        if (s == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = s });
    }

    [HttpPost]
    [RequirePermission("wms-carrier", "add")]
    public async Task<IActionResult> Create([FromBody] CarrierShipmentDto dto)
    {
        try
        {
            var no = await _svc.CreateShipmentAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { shipmentNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/event")]
    [RequirePermission("wms-carrier", "event")]
    public async Task<IActionResult> AddEvent(string no, [FromBody] CarrierEventDto evt)
    {
        try { await _svc.AddEventAsync(no, evt, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/pickup")]
    [RequirePermission("wms-carrier", "event")]
    public async Task<IActionResult> PickUp(string no)
    {
        try { await _svc.MarkPickedUpAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/in-transit")]
    [RequirePermission("wms-carrier", "event")]
    public async Task<IActionResult> InTransit(string no)
    {
        try { await _svc.MarkInTransitAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/delivered")]
    [RequirePermission("wms-carrier", "event")]
    public async Task<IActionResult> Delivered(string no)
    {
        try { await _svc.MarkDeliveredAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/fail")]
    [RequirePermission("wms-carrier", "event")]
    public async Task<IActionResult> Fail(string no, [FromBody] FailReq req)
    {
        try { await _svc.MarkFailedAsync(no, req.Reason, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class FailReq { public string Reason { get; set; } = string.Empty; }
}
