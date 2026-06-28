using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 代理授权管理 REST（Phase C）。/api/oa/delegate — 我能代理谁/我授出的委派/增删。
/// act-as 授权方向：Wf_FlowDelegate{GrantorId=委托人, DelegateId=代理人}。
/// </summary>
[ApiController]
[Route("api/oa/delegate")]
[Authorize]
public class DelegateController : LocalizedControllerBase
{
    private readonly IDelegateService _delegate;
    private readonly ICurrentPermissionContext _ctx;

    public DelegateController(IDelegateService @delegate, ICurrentPermissionContext ctx)
    {
        _delegate = @delegate;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 我能代理谁 / 谁能代理我 ──

    [HttpGet("my-grants")]
    public async Task<IActionResult> MyGrants()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _delegate.MyGrantsAsync(me));
    }

    // ── 我授出的委派（设置页）──

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _delegate.ListMyDelegatesAsync(me));
    }

    // ── 新增委派 ──

    public record AddDelegateReq(Guid DelegateId, DateTime ValidFrom, DateTime ValidTo, string? Scope, string? Remark);

    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] AddDelegateReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            var id = await _delegate.AddDelegateAsync(me, r.DelegateId, r.ValidFrom, r.ValidTo, r.Scope, r.Remark);
            return Ok2(new { id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 删除委派 ──

    public record RemoveDelegateReq(Guid Id);

    [HttpPost("remove")]
    public async Task<IActionResult> Remove([FromBody] RemoveDelegateReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            await _delegate.RemoveDelegateAsync(me, r.Id);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
