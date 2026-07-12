using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>
/// 流程 REST（OA 章03/04）。/api/wf —— 定义 CRUD + 起流程 + 办理 + 实例详情。
/// 发起人/办理人一律取登录用户（不接受客户端传 id，防冒充）。执行委托 IFlowEngine。
/// </summary>
[ApiController]
[Route("api/wf")]
[Authorize]
public class FlowController : LocalizedControllerBase
{
    private readonly IFlowEngine _engine;
    private readonly IFlowDefService _defSvc;
    private readonly ICurrentPermissionContext _ctx;

    public FlowController(IFlowEngine engine, IFlowDefService defSvc, ICurrentPermissionContext ctx)
    {
        _engine = engine;
        _defSvc = defSvc;
        _ctx = ctx;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 流程定义 ──

    [HttpPost("flow/def")]
    [RequirePermission("oa-designer", "edit")]
    public async Task<IActionResult> SaveDef([FromBody] FlowDefReq r)
    {
        try { return Ok2(new { id = await _defSvc.SaveDefAsync(r.FlowKey, r.FlowName, r.FormKey, r.SchemaJson, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("flow/def/{flowKey}")]
    public async Task<IActionResult> GetDef(string flowKey)
    {
        var def = await _defSvc.GetDefAsync(flowKey);
        return def is null ? NotFound(new { code = 404, message = Localizer["流程定义不存在"] }) : Ok2(def);
    }

    // ── 起流程 / 办理 ──

    [HttpPost("flow/submit")]
    [RequirePermission("oa-form-catalog", "submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitReq r)
    {
        try
        {
            var starter = await CurrentUserIdAsync();
            var id = await _engine.SubmitAsync(r.FlowKey, starter, r.VarsJson ?? "{}", r.BizType, r.BizId);
            return Ok2(new { instanceId = id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("task/{id}/act")]
    [RequirePermission("oa-inbox", "approve")]
    public async Task<IActionResult> Act(Guid id, [FromBody] ActReq r)
    {
        try
        {
            var actor = await CurrentUserIdAsync();
            await _engine.ActAsync(id, actor, r.Approve, r.Comment);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 实例详情（含审批痕迹）──

    [HttpGet("flow/instance/{id}")]
    public async Task<IActionResult> Instance(Guid id)
    {
        var detail = await _defSvc.GetInstanceDetailAsync(id);
        return detail is null ? NotFound(new { code = 404, message = Localizer["流程实例不存在"] }) : Ok2(detail);
    }

    public record FlowDefReq(string FlowKey, string FlowName, string FormKey, string SchemaJson);
    public record SubmitReq(string FlowKey, string? VarsJson, string? BizType, string? BizId);
    public record ActReq(bool Approve, string? Comment);
}
