using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM240 — パレット管理 Web API</summary>
[ApiController]
[Route("api/wms/pallet")]
[Authorize]
public class PalletController : ControllerBase
{
    private readonly IPalletService _svc;
    public PalletController(IPalletService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] PalletSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost]
    [RequirePermission("wms-pallet", "add")]
    public async Task<IActionResult> Create([FromBody] PalletDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { palletNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-pallet", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] PalletDto dto)
    {
        try { await _svc.UpdateAsync(no, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/complete-building")]
    [RequirePermission("wms-pallet", "complete")]
    public async Task<IActionResult> CompleteBuilding(string no)
    {
        try { await _svc.CompleteBuildingAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/move-to-shipping")]
    [RequirePermission("wms-pallet", "move")]
    public async Task<IActionResult> MoveToShipping(string no, [FromBody] MoveRequest req)
    {
        try { await _svc.MoveToShippingWaitAsync(no, req.ToLocationCd, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/mark-shipped")]
    [RequirePermission("wms-pallet", "ship")]
    public async Task<IActionResult> MarkShipped(string no, [FromBody] ShipRequest req)
    {
        try { await _svc.MarkShippedAsync(no, req.OutboundNo, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{no}")]
    [RequirePermission("wms-pallet", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try { await _svc.DeleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class MoveRequest { public string ToLocationCd { get; set; } = string.Empty; }
    public class ShipRequest { public string OutboundNo { get; set; } = string.Empty; }
}
