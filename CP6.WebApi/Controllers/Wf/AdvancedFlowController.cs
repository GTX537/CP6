using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>
/// 高级流程动作 REST（OA 章07）。/api/wf/advanced —— 退回 / 加签 / 委派维护。
/// 发起人/操作人取登录用户（不接受客户端传 id）。
/// </summary>
[ApiController]
[Route("api/wf/advanced")]
[Authorize]
public class AdvancedFlowController : LocalizedControllerBase
{
    private readonly IFlowEngine _engine;
    private readonly ICurrentPermissionContext _ctx;

    public AdvancedFlowController(IFlowEngine engine, ICurrentPermissionContext ctx)
    {
        _engine = engine;
        _ctx = ctx;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>退回到目标节点（章07 §2）。</summary>
    [HttpPost("sendback")]
    public async Task<IActionResult> SendBack([FromBody] SendBackReq r)
    {
        try
        {
            var actor = (await _ctx.GetAsync()).UserId;
            await _engine.SendBackAsync(r.TaskId, actor, r.TargetNodeId, r.Comment);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>加签（章07 §3）。source: before(前) / after(后)。</summary>
    [HttpPost("addsign")]
    public async Task<IActionResult> AddSign([FromBody] AddSignReq r)
    {
        try
        {
            var actor = (await _ctx.GetAsync()).UserId;
            var id = await _engine.AddSignAsync(r.TaskId, actor, r.AddSigneeId, r.Source, r.Comment);
            return Ok2(new { taskId = id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>登记委派（章07 §5）。委托人=登录用户。</summary>
    [HttpPost("delegate")]
    public async Task<IActionResult> Delegate([FromBody] DelegateReq r)
    {
        try
        {
            var grantor = (await _ctx.GetAsync()).UserId;
            var id = await _engine.SetDelegateAsync(grantor, r.DelegateId, r.ValidFrom, r.ValidTo, r.Scope, r.Remark);
            return Ok2(new { delegateId = id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}

public class SendBackReq
{
    public Guid TaskId { get; set; }
    public string TargetNodeId { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public class AddSignReq
{
    public Guid TaskId { get; set; }
    public Guid AddSigneeId { get; set; }
    public string Source { get; set; } = "after";
    public string? Comment { get; set; }
}

public class DelegateReq
{
    public Guid DelegateId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public string? Scope { get; set; }
    public string? Remark { get; set; }
}
