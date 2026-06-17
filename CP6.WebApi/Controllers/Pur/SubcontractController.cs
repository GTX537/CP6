using CP6.Core.Services.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>
/// 外注加工 REST —— 采购章07。/api/pur/subcontract。
/// 外注闭环：外注 PO(Type=2 成品行=加工费) → 登记/发支給材(IssuedQty 防吞料) → 收成品成本核算(加工费+料并入) → 防吞料对账。
/// </summary>
[ApiController]
[Route("api/pur/subcontract")]
[Authorize]
public class SubcontractController : ControllerBase
{
    private readonly ISubcontractService _svc;
    public SubcontractController(ISubcontractService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>列出外注 PO（Type=2，含成品行），供外注台选单。</summary>
    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders() => Ok2(await _svc.ListOrdersAsync());

    /// <summary>取某外注 PO（可指定行）的支給材清单。</summary>
    [HttpGet("{poNo}/consign")]
    public async Task<IActionResult> GetConsign(string poNo, [FromQuery] int? lineNo)
        => Ok2(await _svc.GetConsignAsync(poNo, lineNo));

    /// <summary>登记外注成品行的支給材（按成品 BOM 应发料，校验外注 Type=2，幂等 upsert）。</summary>
    [HttpPost("{poNo}/{lineNo:int}/consign")]
    public async Task<IActionResult> AddConsign(string poNo, int lineNo, [FromBody] List<ConsignMaterialDto> items)
    {
        try { return Ok2(await _svc.AddConsignAsync(poNo, lineNo, items ?? new(), CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>发支給材（委托 WMS 出库 Purpose=subcontract，累加 IssuedQty；body 为空→一次发齐剩余，否则分批）。</summary>
    [HttpPost("{poNo}/{lineNo:int}/issue")]
    public async Task<IActionResult> Issue(string poNo, int lineNo, [FromBody] List<ConsignIssueDto>? issuances)
    {
        try { return Ok2(await _svc.IssueConsignAsync(poNo, lineNo, issuances, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>收成品成本核算（加工费 + 支給材成本并入，接财务 06）。</summary>
    [HttpPost("{poNo}/{lineNo:int}/finished-cost")]
    public async Task<IActionResult> FinishedCost(string poNo, int lineNo, [FromBody] FinishedCostRequest req)
    {
        try { return Ok2(await _svc.CalcFinishedCostAsync(poNo, lineNo, req.FinishedQty, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>防吞料对账（实发 vs 成品反推应耗，超损耗容差→挂起核查）。</summary>
    [HttpPost("{poNo}/{lineNo:int}/reconcile")]
    public async Task<IActionResult> Reconcile(string poNo, int lineNo, [FromBody] ReconcileRequest req)
    {
        try { return Ok2(await _svc.ReconcileConsignAsync(poNo, lineNo, req.FinishedQty, req.TolerancePct, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}

/// <summary>成品成本核算请求体。</summary>
public class FinishedCostRequest
{
    /// <summary>收成品数量</summary>
    public decimal FinishedQty { get; set; }
}

/// <summary>防吞料对账请求体。</summary>
public class ReconcileRequest
{
    /// <summary>实收成品数量（反推应耗的基数）</summary>
    public decimal FinishedQty { get; set; }

    /// <summary>损耗容差（占应耗比例，默认 5%）</summary>
    public decimal TolerancePct { get; set; } = 0.05m;
}
