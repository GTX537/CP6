using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM040 — 入庫実績 Web API
/// </summary>
[ApiController]
[Route("api/wms/inbound-receipt")]
[Authorize]
public class InboundReceiptController : ControllerBase
{
    private readonly IInboundService _svc;
    public InboundReceiptController(IInboundService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] InboundReceiptSearchQuery q)
    {
        var list = await _svc.SearchReceiptsAsync(q);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _svc.GetReceiptAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>受入登録（同時に確定 + 在庫反映）</summary>
    [HttpPost]
    public async Task<IActionResult> Confirm([FromBody] InboundReceiptDto dto)
    {
        try
        {
            var no = await _svc.ConfirmReceiptAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { receiptNo = no } });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex)  { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (ArgumentException ex)          { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
