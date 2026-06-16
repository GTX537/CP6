using CP6.Core.Auth;
using CP6.Core.Services.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>收货 REST —— 采购章03。/api/pur/gr。双基准收货 + 检收应用（回写 PO 三累计锚）。</summary>
[ApiController]
[Route("api/pur/gr")]
[Authorize]
public class GoodsReceiptController : ControllerBase
{
    private readonly IGoodsReceiptService _svc;
    public GoodsReceiptController(IGoodsReceiptService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? poNo, [FromQuery] string? supplierId)
        => Ok2(await _svc.ListAsync(poNo, supplierId));

    [HttpGet("{grNo}")]
    public async Task<IActionResult> Get(string grNo)
    {
        var gr = await _svc.GetAsync(grNo);
        return gr == null ? Err(new InvalidOperationException("E-PUR-035")) : Ok2(gr);
    }

    /// <summary>确认收货（着荷即验收 / 检收转待检）。</summary>
    [HttpPost]
    [RequirePermission("pur-gr", "add")]
    public async Task<IActionResult> Confirm([FromBody] GrCreateDto dto)
    {
        try { return Ok2(await _svc.ConfirmReceiveAsync(dto, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>应用检收结果（仅检收基准；合格累加 PO 验收锚）。</summary>
    [HttpPost("{grNo}/apply-qc")]
    [RequirePermission("pur-gr", "qc")]
    public async Task<IActionResult> ApplyQc(string grNo)
    {
        try { return Ok2(await _svc.ApplyQcResultAsync(grNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
