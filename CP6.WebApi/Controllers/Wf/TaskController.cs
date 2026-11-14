using System.Data.Common;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>待办中心 REST（OA 章04）。/api/wf —— 我的待办 / 我的申请 / 撤回。用户取登录上下文。</summary>
[ApiController]
[Route("api/wf")]
[Authorize]
public class TaskController : ControllerBase
{
    private const string WithdrawAction = "withdraw";
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
    [RequirePermission("oa-inbox", WithdrawAction)]
    public async Task<IActionResult> Withdraw(Guid id)
    {
        var authority = HttpContext.RequestServices.GetRequiredService<IamAdministrationV1>();
        if (!authority.IsPostgreSql)
        {
            try { await _svc.WithdrawAsync(id, await MeAsync()); return Ok2(); }
            catch (InvalidOperationException e) { return Err(e); }
        }

        try
        {
            await IamAdministrationHttpV1.ExecuteWriteAsync(HttpContext, authority, $"oa-inbox:{WithdrawAction}",
                async (current, ct) =>
                {
                    await _svc.SaveWithdrawalAsync(id, current.UserId, ct);
                    return true;
                });
        }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Status, message = e.Message }); }
        catch (Exception e) when (e is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_FENCE_UNAVAILABLE" });
        }
        catch (InvalidOperationException e) { return Err(e); }

        // The parent may run callbacks or external tasks. Resume from the committed work item.
        try { await _svc.ResumeAfterWithdrawalAsync(); }
        catch (InvalidOperationException e) { return Err(e); }
        return Ok2();
    }
}
