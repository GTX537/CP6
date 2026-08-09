using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Oa;
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
    private readonly IInboxService _inbox;
    private readonly IDelegateService _delegates;

    public FlowController(IFlowEngine engine, IFlowDefService defSvc, ICurrentPermissionContext ctx,
        IInboxService inbox, IDelegateService delegates)
    {
        _engine = engine;
        _defSvc = defSvc;
        _ctx = ctx;
        _inbox = inbox;
        _delegates = delegates;
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
        try
        {
            var draft = await _defSvc.SaveDraftAsync(r.FlowKey, r.FlowName, r.FormKey, r.SchemaJson, null, CurrentUser);
            return Ok2(new { id = draft.DefinitionId, versionId = draft.VersionId, status = "draft" });
        }
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
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone,
            new { code = 410, message = "Use an authoritative SFS or business submission endpoint." });
    }

    [HttpPost("task/{id}/act")]
    [RequirePermission("oa-inbox", "approve")]
    public async Task<IActionResult> Act(Guid id, [FromBody] ActReq r)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone,
            new { code = 410, message = "Use /api/oa/tasks/{taskId}/decision." });
    }

    // ── 实例详情（含审批痕迹）──

    [HttpGet("flow/instance/{id}")]
    public async Task<IActionResult> Instance(Guid id)
    {
        try
        {
            var actual = await CurrentUserIdAsync();
            var effective = actual;
            var header = Request.Headers["X-Acting-As"].ToString();
            if (Guid.TryParse(header, out var actingAs) && actingAs != Guid.Empty && actingAs != actual)
            {
                await _delegates.AssertActiveGrantAsync(actual, actingAs);
                effective = actingAs;
            }
            var detail = await _inbox.DetailAsync(actual, effective, id);
            return detail is null ? NotFound(new { code = 404, message = "E-WF-007" }) : Ok2(detail);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "E-WF-043", message = "E-WF-043" });
        }
    }

    public record FlowDefReq(string FlowKey, string FlowName, string? FormKey, string SchemaJson);
    public record SubmitReq(string FlowKey, string? VarsJson, string? BizType, string? BizId);
    public record ActReq(bool Approve, string? Comment);
}
