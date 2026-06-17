using CP6.Core.Services.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>
/// 采购对账 REST —— 采购章08/09 完整性收口。/api/pur/reconcile。
/// PO↔GR↔AP 三方累计量核对 + 堵三个漏（虚开/超收·重复收货/外协吞料）：一眼看出每张 PO 卡在哪、有无钻空子嫌疑。
/// </summary>
[ApiController]
[Route("api/pur/reconcile")]
[Authorize]
public class PurReconcileController : ControllerBase
{
    private readonly IPurReconcileService _svc;
    public PurReconcileController(IPurReconcileService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>对某 PO 出三方对账表（逐行诊断 + 完整性异常汇总；外注并入防吞料对账）。</summary>
    [HttpGet("{poNo}")]
    public async Task<IActionResult> Reconcile(string poNo)
    {
        try { return Ok2(await _svc.ReconcilePoAsync(poNo)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
