using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/wms/stock-qc")]
[Authorize]
public class StockQcController : ControllerBase
{
    private readonly IStockQcService _service;

    public StockQcController(IStockQcService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>POST /api/wms/stock-qc/{stockId}/set</summary>
    [HttpPost("{stockId:guid}/set")]
    [RequirePermission("wms-stock-qc", "set")]
    public async Task<IActionResult> SetSingle(Guid stockId, [FromBody] SetStockQcRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewStatus))
            return BadRequest(new { code = 400, message = "NewStatus is required.", data = (object?)null });

        var stock = await _service.SetStockQcStatusAsync(stockId, req.NewStatus, req.Reason, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = new { stockId = stock.Id, qcStatus = stock.QcStatus } });
    }

    /// <summary>POST /api/wms/stock-qc/by-work-order/{workOrderNo}</summary>
    [HttpPost("by-work-order/{workOrderNo}")]
    [RequirePermission("wms-stock-qc", "set")]
    public async Task<IActionResult> MarkByWO(string workOrderNo, [FromBody] SetStockQcRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewStatus))
            return BadRequest(new { code = 400, message = "NewStatus is required.", data = (object?)null });

        var affected = await _service.MarkLinkedStockByWorkOrderAsync(workOrderNo, req.NewStatus, req.Reason, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = new { workOrderNo, affected } });
    }
}

public class SetStockQcRequest
{
    public string NewStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
