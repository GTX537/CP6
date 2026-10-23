using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM160 — ロット追溯・回収管理 Web API</summary>
[ApiController]
[Route("api/wms/lot-trace")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LotTraceController : ControllerBase
{
    private readonly ILotTraceService _svc;
    public LotTraceController(ILotTraceService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>順追溯：指定ロット → 出荷した顧客一覧</summary>
    [HttpGet("forward")]
    [RequirePermission("wms-lot-trace", "query")]
    public async Task<IActionResult> Forward([FromQuery] string productCd, [FromQuery] string lotNo)
    {
        if (string.IsNullOrWhiteSpace(productCd) || string.IsNullOrWhiteSpace(lotNo))
            return BadRequest(new { code = 400, message = "productCd and lotNo are required" });
        var r = await _svc.TraceForwardAsync(productCd, lotNo);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>逆追溯：指定ロット → 使用した仕入元</summary>
    [HttpGet("backward")]
    [RequirePermission("wms-lot-trace", "query")]
    public async Task<IActionResult> Backward([FromQuery] string productCd, [FromQuery] string lotNo)
    {
        if (string.IsNullOrWhiteSpace(productCd) || string.IsNullOrWhiteSpace(lotNo))
            return BadRequest(new { code = 400, message = "productCd and lotNo are required" });
        var r = await _svc.TraceBackwardAsync(productCd, lotNo);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>在庫サマリ（複数倉庫合計、リコールフラグ）</summary>
    [HttpGet("summary")]
    [RequirePermission("wms-lot-trace", "query")]
    public async Task<IActionResult> Summary([FromQuery] string productCd, [FromQuery] string lotNo)
    {
        var r = await _svc.GetStockSummaryAsync(productCd, lotNo);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>リコールフラグ ON / OFF</summary>
    [HttpPost("recall")]
    [RequirePermission("wms-lot-trace", "recall")]
    public async Task<IActionResult> Recall([FromBody] RecallRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ProductCd) || string.IsNullOrWhiteSpace(req.LotNo))
            return BadRequest(new { code = 400, message = "productCd and lotNo are required" });
        var n = await _svc.SetRecallFlagAsync(req.ProductCd, req.LotNo, req.Flag, CurrentUser);
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { affectedStocks = n } });
    }

    [HttpGet("owner-v1")]
    [RequirePermission("wms-lot-trace", "query")]
    public async Task<IActionResult> ReadOwner([FromQuery] string productCd, [FromQuery] string? lotNo = "", [FromQuery] string direction = "FORWARD") =>
        Ok(new { code = 0, message = "OK", data = await _svc.ReadOwnerAsync(productCd, lotNo ?? "", direction) });

    [HttpPost("owner-v1/recall")]
    [RequirePermission("wms-lot-trace", "recall")]
    public async Task<IActionResult> RecallOwner([FromBody] CP6.Entity.DTOs.Wms.LotRecallCommandV1 command) =>
        Ok(new { code = 0, message = "OK", data = await _svc.RecallOwnerAsync(command) });

    public class RecallRequest
    {
        public string ProductCd { get; set; } = string.Empty;
        public string LotNo { get; set; } = string.Empty;
        public bool Flag { get; set; } = true;
    }
}
