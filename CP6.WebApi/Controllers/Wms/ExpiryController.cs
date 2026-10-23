using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM170 — 賞味期限・FEFO 管理</summary>
[ApiController]
[Route("api/wms/expiry")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
public class ExpiryController : ControllerBase
{
    private readonly IExpiryService _svc;
    public ExpiryController(IExpiryService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>期限切迫 Stock 一覧（既定 30 日以内）</summary>
    [HttpPost("observation-v1")]
    [RequirePermission("wms-expiry", "view")]
    public async Task<IActionResult> Observation([FromBody] CP6.Entity.DTOs.Wms.ExpiryObservationQueryV1 query, CancellationToken ct)
        => Ok(new { code = 0, message = "OK", data = await _svc.GetObservationAsync(query, ct) });

    [HttpGet("expiring")]
    [RequirePermission("wms-expiry", "view")]
    public async Task<IActionResult> Expiring([FromQuery] int days = 30, [FromQuery] string? warehouseCd = null)
    {
        var list = await _svc.GetExpiringStocksAsync(days, warehouseCd);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    /// <summary>一括廃棄（指定 Stock を ADJ -PhysicalQty で除去）</summary>
    [HttpPost("dispose")]
    [RequirePermission("wms-expiry", "dispose")]
    public async Task<IActionResult> Dispose([FromBody] DisposeRequest req)
    {
        try
        {
            var n = await _svc.DisposeAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { disposed = n } });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
