using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 通知中心 REST（OA Phase D-1 §通知中心 N-T6）。
/// /api/oa/notification — 列表/未读数/标记已读/全部已读。
/// </summary>
[ApiController]
[Route("api/oa/notification")]
[Authorize]
public class NotificationController : LocalizedControllerBase
{
    private readonly INotificationService _notif;
    private readonly ICurrentPermissionContext _ctx;

    public NotificationController(INotificationService notif, ICurrentPermissionContext ctx)
    {
        _notif = notif;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 通知列表 ──

    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] bool? unreadOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _notif.ListAsync(me, unreadOnly ?? false, page ?? 1, pageSize ?? 20));
    }

    // ── 未读数量 ──

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(new { count = await _notif.UnreadCountAsync(me) });
    }

    // ── 标记单条已读 ──

    [HttpPost("read")]
    [RequirePermission("oa-inbox", "read")]
    public async Task<IActionResult> Read([FromBody] IdReq r)
    {
        var me = await CurrentUserIdAsync();
        await _notif.MarkReadAsync(me, r.Id);
        return Ok2();
    }

    // ── 全部标记已读 ──

    [HttpPost("read-all")]
    [RequirePermission("oa-inbox", "read")]
    public async Task<IActionResult> ReadAll()
    {
        var me = await CurrentUserIdAsync();
        await _notif.MarkAllReadAsync(me);
        return Ok2();
    }

    public record IdReq(Guid Id);
}
