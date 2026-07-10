using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM030 — 入庫予定 Web API
/// </summary>
[ApiController]
[Route("api/wms/inbound-order")]
[Authorize]
public class InboundOrderController : ControllerBase
{
    private readonly IInboundService _svc;
    public InboundOrderController(IInboundService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] InboundOrderSearchQuery q)
    {
        var list = await _svc.SearchOrdersAsync(q);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _svc.GetOrderAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpPost]
    [RequirePermission("wms-inbound-order", "add")]
    public async Task<IActionResult> Create([FromBody] InboundOrderDto dto)
    {
        try
        {
            var no = await _svc.CreateOrderAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { inboundNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-inbound-order", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] InboundOrderDto dto)
    {
        try
        {
            await _svc.UpdateOrderAsync(no, dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{no}")]
    [RequirePermission("wms-inbound-order", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try
        {
            await _svc.DeleteOrderAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/confirm")]
    [RequirePermission("wms-inbound-order", "confirm")]
    public async Task<IActionResult> Confirm(string no)
    {
        try
        {
            await _svc.ConfirmOrderAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-inbound-order", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try
        {
            await _svc.CancelOrderAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
