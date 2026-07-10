using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM050/070 — 出庫指示（材料 + 出荷 共有）Web API
/// </summary>
[ApiController]
[Route("api/wms/outbound-order")]
[Authorize]
public class OutboundOrderController : ControllerBase
{
    private readonly IOutboundService _svc;
    public OutboundOrderController(IOutboundService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] OutboundOrderSearchQuery q)
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
    [RequirePermission("wms-outbound-order", "add")]
    public async Task<IActionResult> Create([FromBody] OutboundOrderDto dto)
    {
        try
        {
            var no = await _svc.CreateOrderAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { outboundNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-outbound-order", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] OutboundOrderDto dto)
    {
        try
        {
            await _svc.UpdateOrderAsync(no, dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{no}")]
    [RequirePermission("wms-outbound-order", "del")]
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
    [RequirePermission("wms-outbound-order", "confirm")]
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
    [RequirePermission("wms-outbound-order", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try
        {
            await _svc.CancelOrderAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>引当実行（FIFO + 期限優先）</summary>
    [HttpPost("{no}/allocate")]
    [RequirePermission("wms-outbound-order", "allocate")]
    public async Task<IActionResult> Allocate(string no)
    {
        try
        {
            await _svc.AllocateAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex)  { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>ピッキング開始（Allocated → Picking）</summary>
    [HttpPost("{no}/start-picking")]
    [RequirePermission("wms-outbound-order", "pick")]
    public async Task<IActionResult> StartPicking(string no)
    {
        try
        {
            await _svc.StartPickingAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>出庫確定（OUT + 出荷区分なら梱包採番）</summary>
    [HttpPost("{no}/ship")]
    [RequirePermission("wms-outbound-order", "ship")]
    public async Task<IActionResult> Ship(string no, [FromBody] ShipRequest req)
    {
        try
        {
            var pkg = await _svc.ShipAsync(no, req ?? new ShipRequest(), CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { packageNo = pkg } });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex)  { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    // ───────── 自動展開 ─────────

    /// <summary>MES 製造指図 → 材料出庫指示を自動生成</summary>
    [HttpPost("from-work-order/{workOrderNo}")]
    [RequirePermission("wms-outbound-order", "add")]
    public async Task<IActionResult> FromWorkOrder(string workOrderNo)
    {
        try
        {
            var no = await _svc.CreateFromWorkOrderAsync(workOrderNo, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { outboundNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>PA 受注 → 出荷指示を自動生成</summary>
    [HttpPost("from-order/{webOrderNo}")]
    [RequirePermission("wms-outbound-order", "add")]
    public async Task<IActionResult> FromOrder(string webOrderNo)
    {
        try
        {
            var no = await _svc.CreateFromOrderAsync(webOrderNo, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { outboundNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
