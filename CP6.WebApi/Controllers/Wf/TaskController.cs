using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>待办中心 REST（OA 章04）。/api/wf —— 我的待办 / 我的申请 / 撤回。用户取登录上下文。</summary>
[ApiController]
[Route("api/wf")]
[Authorize]
public class TaskController : ControllerBase
{
    private readonly ITaskCenterService _svc;
    private readonly ICurrentPermissionContext _ctx;

    public TaskController(ITaskCenterService svc, ICurrentPermissionContext ctx)
    {
        _svc = svc;
        _ctx = ctx;
    }

    private async Task<Guid> MeAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("my-todos")]
    public async Task<IActionResult> MyTodos() => Ok2(await _svc.MyTodosAsync(await MeAsync()));

    [HttpGet("my-applications")]
    public async Task<IActionResult> MyApplications() => Ok2(await _svc.MyApplicationsAsync(await MeAsync()));

    [HttpPost("flow/{id}/withdraw")]
    [RequirePermission("oa-inbox", "withdraw")]
    public async Task<IActionResult> Withdraw(Guid id)
    {
        try { await _svc.WithdrawAsync(id, await MeAsync()); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
