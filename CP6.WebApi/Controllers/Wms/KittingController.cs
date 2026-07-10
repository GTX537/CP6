using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM140 — キッティング・組立 Web API</summary>
[ApiController]
[Route("api/wms/kit")]
[Authorize]
public class KittingController : ControllerBase
{
    private readonly IKittingService _svc;
    public KittingController(IKittingService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    // ─── マスタ ───
    [HttpGet("masters")]
    public async Task<IActionResult> SearchMasters([FromQuery] string? keyword)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchMastersAsync(keyword) });

    [HttpGet("masters/{kitSku}")]
    public async Task<IActionResult> GetMaster(string kitSku)
    {
        var dto = await _svc.GetMasterAsync(kitSku);
        if (dto == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpPost("masters")]
    [RequirePermission("wms-kitting", "add")]
    public async Task<IActionResult> CreateMaster([FromBody] KitMasterDto dto)
    {
        try { await _svc.CreateMasterAsync(dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("masters/{kitSku}")]
    [RequirePermission("wms-kitting", "edit")]
    public async Task<IActionResult> UpdateMaster(string kitSku, [FromBody] KitMasterDto dto)
    {
        try { await _svc.UpdateMasterAsync(kitSku, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("masters/{kitSku}")]
    [RequirePermission("wms-kitting", "del")]
    public async Task<IActionResult> DeleteMaster(string kitSku)
    {
        try { await _svc.DeleteMasterAsync(kitSku, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    // ─── 指示 ───
    [HttpGet("orders")]
    public async Task<IActionResult> SearchOrders([FromQuery] KitOrderSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchOrdersAsync(q) });

    [HttpGet("orders/{no}")]
    public async Task<IActionResult> GetOrder(string no)
    {
        var o = await _svc.GetOrderAsync(no);
        if (o == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = o });
    }

    [HttpPost("orders")]
    [RequirePermission("wms-kitting", "add")]
    public async Task<IActionResult> CreateOrder([FromBody] KitOrderDto dto)
    {
        try
        {
            var no = await _svc.CreateOrderAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { kitOrderNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("orders/{no}/execute")]
    [RequirePermission("wms-kitting", "execute")]
    public async Task<IActionResult> Execute(string no)
    {
        try { await _svc.ExecuteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("orders/{no}/cancel")]
    [RequirePermission("wms-kitting", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try { await _svc.CancelAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
