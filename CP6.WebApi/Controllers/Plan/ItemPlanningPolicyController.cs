using CP6.Core.Auth;
using CP6.Core.Services.Plan;
using CP6.Entity.DomainModels.Plan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plan;

/// <summary>计划主数据 REST —— spec §3.2。/api/plan/item-policy。品目计划策略维护。</summary>
[ApiController]
[Route("api/plan/item-policy")]
[Authorize]
public class ItemPlanningPolicyController : ControllerBase
{
    private readonly IItemPlanningPolicyService _svc;
    public ItemPlanningPolicyController(IItemPlanningPolicyService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? keyword)
        => Ok2(await _svc.ListAsync(keyword));

    [HttpGet("{itemCd}")]
    public async Task<IActionResult> Get(string itemCd)
        => Ok2(await _svc.GetPolicyAsync(itemCd));

    [HttpPost]
    [RequirePermission("plan-item-policy", "add")]
    public async Task<IActionResult> Upsert([FromBody] Plan_ItemPlanningPolicy dto)
    {
        try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{itemCd}")]
    [RequirePermission("plan-item-policy", "delete")]
    public async Task<IActionResult> Delete(string itemCd)
    {
        try { await _svc.DeleteAsync(itemCd, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
