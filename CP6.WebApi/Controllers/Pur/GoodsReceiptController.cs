using CP6.Core.Auth;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Sys;
using CP6.WebApi.Filters;
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
    public async Task<IActionResult> Confirm([FromBody] GrCreateDto dto, [FromHeader(Name = "Idempotency-Key")] Guid? operationId)
    {
        try { return Ok2(await _svc.ConfirmReceiveAsync(dto, CurrentUser, operationId)); }
        catch (OrderPriceCorrectionException e) { return StatusCode(e.Status, new { code = e.Status, message = e.Message }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("operations/scope")]
    public async Task<IActionResult> ConfirmationScope(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok2(await _svc.ConfirmationScopeAsync(ct)); }
        catch (OrderPriceCorrectionException e) { return StatusCode(e.Status, new { code = e.Status, message = e.Message }); }
        catch (CurrentPermissionUnavailableException)
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_FENCE_UNAVAILABLE" });
        }
    }

    [HttpGet("operations/{operationId:guid}")]
    public async Task<IActionResult> ReadConfirmation(Guid operationId, CancellationToken ct)
    {
        try
        {
            Response.Headers.CacheControl = "no-store";
            var receipt = await _svc.ReadConfirmationAsync(operationId, ct);
            return Ok2(new { operationId, state = receipt is null ? "UNKNOWN" : "COMMITTED", receipt });
        }
        catch (OrderPriceCorrectionException e) { return StatusCode(e.Status, new { code = e.Status, message = e.Message }); }
    }

    /// <summary>应用检收结果（仅检收基准；合格累加 PO 验收锚）。</summary>
    [HttpPost("{grNo}/apply-qc")]
    [RequirePermission("pur-gr", "qc")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> ApplyQc(string grNo)
    {
        try { return Ok2(await _svc.ApplyQcResultAsync(grNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
