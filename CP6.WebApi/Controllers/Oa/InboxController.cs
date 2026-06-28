using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 电子表单信箱 REST（Phase B + C）。/api/oa/inbox — 未处理/在途/已处理/详情/批量办理/转交。
/// 当前用户取登录身份（防冒充），写动作经 L0 引擎，读取 L1 读模型三表。
/// act-as：读取头 X-Acting-As，经 IDelegateService 校验后以被代理人身份查询；写动作记实际执行人 + onBehalfOf。
/// </summary>
[ApiController]
[Route("api/oa/inbox")]
[Authorize]
public class InboxController : LocalizedControllerBase
{
    private readonly IInboxService _inbox;
    private readonly ICurrentPermissionContext _ctx;
    private readonly IDelegateService _delegate;
    private readonly IFlowEngine _engine;

    public InboxController(IInboxService inbox, ICurrentPermissionContext ctx,
        IDelegateService @delegate, IFlowEngine engine)
    {
        _inbox = inbox;
        _ctx = ctx;
        _delegate = @delegate;
        _engine = engine;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>
    /// 读请求头 X-Acting-As：非空且非本人 → 校验授权（E-WF-001）→ 返回被代理人；否则返回本人。
    /// </summary>
    private async Task<(Guid effective, Guid? onBehalfOf)> EffectiveAsync()
    {
        var me = (await _ctx.GetAsync()).UserId;
        var hdr = Request.Headers["X-Acting-As"].ToString();
        if (Guid.TryParse(hdr, out var x) && x != Guid.Empty && x != me)
        {
            await _delegate.AssertActiveGrantAsync(me, x);   // 失败抛 E-WF-001
            return (x, x);
        }
        return (me, null);
    }

    // ── 未处理 ──

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            return Ok2(await _inbox.PendingAsync(eff));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("pending-cc")]
    public async Task<IActionResult> PendingCc()
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            return Ok2(await _inbox.PendingCcAsync(eff));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 在途 ──

    [HttpGet("running")]
    public async Task<IActionResult> Running()
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            return Ok2(await _inbox.RunningAsync(eff));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 已处理 ──

    [HttpGet("done")]
    public async Task<IActionResult> Done([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string tab = "mine")
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            return Ok2(await _inbox.DoneAsync(eff, year, month, tab));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 统计 ──

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            return Ok2(await _inbox.StatsAsync(eff));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 详情 ──

    [HttpGet("detail/{instanceId:guid}")]
    public async Task<IActionResult> Detail(Guid instanceId)
    {
        try
        {
            // act-as 校验（即使 detail 不过滤 userId，仍须验头有效）
            await EffectiveAsync();
            var detail = await _inbox.DetailAsync(instanceId);
            return detail is null
                ? NotFound(new { code = 404, message = "E-WF-007" })
                : Ok2(detail);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 已读标记 ──

    [HttpPost("task/read")]
    public async Task<IActionResult> MarkTaskRead([FromBody] IdReq r)
    {
        var me = await CurrentUserIdAsync();
        await _inbox.MarkTaskReadAsync(me, r.Id);
        return Ok2(new { success = true });
    }

    [HttpPost("cc/read")]
    public async Task<IActionResult> MarkCcRead([FromBody] IdReq r)
    {
        var me = await CurrentUserIdAsync();
        await _inbox.MarkCcReadAsync(me, r.Id);
        return Ok2(new { success = true });
    }

    // ── 批量办理（act-as 版）──

    [HttpPost("batch")]
    public async Task<IActionResult> Batch([FromBody] BatchReq r)
    {
        try
        {
            var me = (await _ctx.GetAsync()).UserId;
            var (eff, onBehalf) = await EffectiveAsync();
            return Ok2(await _inbox.ActBatchAsAsync(me, onBehalf, r.TaskIds, r.Approve, r.Comment));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 转交 ──

    public record TransferReq(Guid TaskId, Guid ToUserId, string? Comment);

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferReq r)
    {
        try
        {
            var (eff, _) = await EffectiveAsync();   // 转出人=有效用户（act-as 时=被代理人）
            await _engine.TransferAsync(r.TaskId, eff, r.ToUserId, r.Comment);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 退回 ──

    public record SendBackReq(Guid TaskId, string Kind, string? NodeId, string? Comment);

    [HttpPost("sendback")]
    public async Task<IActionResult> SendBack([FromBody] SendBackReq r)
    {
        try
        {
            var (eff, _) = await EffectiveAsync();   // 退回人=有效用户（act-as 时=被代理人）
            await _engine.SendBackAsync(r.TaskId, eff, new SendBackTarget(r.Kind, r.NodeId), r.Comment);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record IdReq(Guid Id);
    public record BatchReq(List<Guid> TaskIds, bool Approve, string? Comment);
}
