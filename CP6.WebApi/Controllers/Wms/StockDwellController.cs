using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/wms/stock-dwell")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
public class StockDwellController : ControllerBase
{
    private readonly IStockDwellService _svc;

    public StockDwellController(IStockDwellService svc)
    {
        _svc = svc;
    }

    [HttpPost("observation-v1")]
    [RequirePermission("wms-stock-dwell", "view")]
    public async Task<IActionResult> Observation([FromBody] StockDwellQuery query, CancellationToken ct)
        => Ok(new { code = 0, message = "OK", data = await _svc.GetObservationAsync(query, ct) });

    [HttpPost("summary")]
    [RequirePermission("wms-stock-dwell", "view")]
    public async Task<IActionResult> Summary([FromBody] StockDwellQuery? query, CancellationToken ct)
    {
        var data = await _svc.GetSummaryAsync(query ?? new StockDwellQuery(), ct);
        return Ok(new { code = 0, message = "OK", data });
    }
}
