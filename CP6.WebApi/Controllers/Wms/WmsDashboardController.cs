using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM-DASH — WMS ダッシュボード Web API
/// </summary>
[ApiController]
[Route("api/wms/dashboard")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WmsDashboardController : ControllerBase
{
    private readonly IWmsDashboardService _svc;
    public WmsDashboardController(IWmsDashboardService svc) => _svc = svc;

    /// <summary>KPI カード集計</summary>
    [HttpGet("kpi")]
    [RequirePermission("wms-dashboard", "query")]
    public async Task<IActionResult> GetKpi()
    {
        var kpi = await _svc.GetKpiAsync();
        return Ok(new { code = 0, message = "OK", data = kpi });
    }

    /// <summary>入出庫トレンド（既定 30 日）</summary>
    [HttpGet("trend")]
    [RequirePermission("wms-dashboard", "query")]
    public async Task<IActionResult> GetTrend([FromQuery] int days = 30)
    {
        var data = await _svc.GetTransactionTrendAsync(days);
        return Ok(new { code = 0, message = "OK", data });
    }

    /// <summary>倉庫別在庫金額</summary>
    [HttpGet("warehouse-value")]
    [RequirePermission("wms-dashboard", "query")]
    public async Task<IActionResult> GetWarehouseValue()
    {
        var data = await _svc.GetWarehouseValueAsync();
        return Ok(new { code = 0, message = "OK", data });
    }

    /// <summary>アラート一覧</summary>
    [HttpGet("alerts")]
    [RequirePermission("wms-dashboard", "query")]
    public async Task<IActionResult> GetAlerts()
    {
        var data = await _svc.GetAlertsAsync();
        return Ok(new { code = 0, message = "OK", data });
    }
}
