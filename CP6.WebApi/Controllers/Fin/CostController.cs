using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 成本核算 REST —— 财务章06。/api/fin/cost。按工单归集料工费(料吃MES真实消耗)→完工结转WIP→FG。
/// 功能权限（D-2 约定）：变更端点贴 [RequirePermission("fin-cost", …)]；读端点仅 [Authorize]。
/// </summary>
[ApiController]
[Route("api/fin/cost")]
[Authorize]
public class CostController : ControllerBase
{
    private readonly ICostCollectService _collect;
    private readonly ICostSettleService _settle;

    public CostController(ICostCollectService collect, ICostSettleService settle)
    {
        _collect = collect;
        _settle = settle;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    /// <summary>成本单列表（可按状态）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CostSheetStatus? status)
        => Ok2(await _collect.ListAsync(status));

    /// <summary>取某工单成本单（含明细）。</summary>
    [HttpGet("{workOrderNo}")]
    public async Task<IActionResult> Get(string workOrderNo)
    {
        var cs = await _collect.GetByWorkOrderAsync(workOrderNo);
        return cs == null ? BadRequest(new { code = 400, message = "E-FIN-403" }) : Ok2(cs);
    }

    /// <summary>FG 完工单位成本（喂 AR 成本结转切真实）。</summary>
    [HttpGet("{workOrderNo}/fg-unit-cost")]
    public async Task<IActionResult> FgUnitCost(string workOrderNo)
        => Ok2(new { workOrderNo, fgUnitCost = await _settle.FgUnitCostAsync(workOrderNo) });

    /// <summary>归集料工费（料自动吃 MES 真实消耗×BOM单价，工费传标准估算）。</summary>
    [HttpPost("{workOrderNo}/collect")]
    [RequirePermission("fin-cost", "collect")]
    public async Task<IActionResult> Collect(string workOrderNo, [FromBody] CollectReq req)
        => Fin(await _collect.CollectAsync(workOrderNo, req.LaborStd, req.OverheadStd, CurrentUser));

    /// <summary>完工结转（料工费→WIP→FG 凭证，置 Settled）。</summary>
    [HttpPost("{workOrderNo}/settle")]
    [RequirePermission("fin-cost", "settle")]
    public async Task<IActionResult> Settle(string workOrderNo)
        => Fin(await _settle.SettleAsync(workOrderNo, CurrentUser));

    public record CollectReq(decimal LaborStd, decimal OverheadStd);
}
