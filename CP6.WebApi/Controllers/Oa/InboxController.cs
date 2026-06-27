using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 电子表单信箱 REST（Phase B）。/api/oa/inbox — 未处理/在途/已处理/详情/批量办理。
/// 当前用户取登录身份（防冒充），写动作经 L0 引擎，读取 L1 读模型三表。
/// </summary>
[ApiController]
[Route("api/oa/inbox")]
[Authorize]
public class InboxController : LocalizedControllerBase
{
    private readonly IInboxService _inbox;
    private readonly ICurrentPermissionContext _ctx;

    public InboxController(IInboxService inbox, ICurrentPermissionContext ctx)
    {
        _inbox = inbox;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 未处理 ──

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _inbox.PendingAsync(me));
    }

    [HttpGet("pending-cc")]
    public async Task<IActionResult> PendingCc()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _inbox.PendingCcAsync(me));
    }

    // ── 在途 ──

    [HttpGet("running")]
    public async Task<IActionResult> Running()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _inbox.RunningAsync(me));
    }

    // ── 已处理 ──

    [HttpGet("done")]
    public async Task<IActionResult> Done([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string tab = "mine")
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _inbox.DoneAsync(me, year, month, tab));
    }

    // ── 统计 ──

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _inbox.StatsAsync(me));
    }

    // ── 详情 ──

    [HttpGet("detail/{instanceId:guid}")]
    public async Task<IActionResult> Detail(Guid instanceId)
    {
        var detail = await _inbox.DetailAsync(instanceId);
        return detail is null
            ? NotFound(new { code = 404, message = "E-WF-007" })
            : Ok2(detail);
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

    // ── 批量办理 ──

    [HttpPost("batch")]
    public async Task<IActionResult> Batch([FromBody] BatchReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            var result = await _inbox.ActBatchAsync(me, r.TaskIds, r.Approve, r.Comment);
            return Ok2(result);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record IdReq(Guid Id);
    public record BatchReq(List<Guid> TaskIds, bool Approve, string? Comment);
}
