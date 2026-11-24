using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Route("api/oa/notification"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationController(INotificationService notifications, ICurrentPermissionContext actor,
    WorkflowNotificationInboxV1 observed) : LocalizedControllerBase
{
    [HttpGet("list")]
    public Task<IActionResult> List(bool? unreadOnly, int? page, int? pageSize, CancellationToken ct)
        => Reply(async () =>
        {
            var take = Math.Clamp(pageSize ?? 20, 1, 100);
            var number = Math.Clamp(page ?? 1, 1, int.MaxValue / take);
            if (!observed.IsAvailable)
            {
                var rows = await notifications.ListAsync(await CurrentUser(), unreadOnly ?? false, number, take);
                return Success(rows.Select(x => x with { Title = NotifyMatrix.Summary(x.Type), Body = NotifyMatrix.Summary(x.Type),
                    TaskId = null, InstanceId = null, FlowKey = null }).ToArray());
            }
            var result = await observed.ListAsync(unreadOnly ?? false, take, null, null, ct, (number - 1) * take);
            return Ok(new { code = 0, message = "OK", data = result.Items, watermark = result.Watermark });
        });

    [HttpGet("unread-count")]
    public Task<IActionResult> UnreadCount(CancellationToken ct)
        => Reply(async () => Success(new { count = observed.IsAvailable ? await observed.UnreadCountAsync(ct)
            : await notifications.UnreadCountAsync(await CurrentUser()) }));

    [HttpPost("read"), RequirePermission("oa-inbox", "read")]
    public Task<IActionResult> Read([FromBody] IdReq input, CancellationToken ct)
        => Reply(async () =>
        {
            if (observed.IsAvailable) await observed.MarkReadAsync(input.Id, ct);
            else await notifications.MarkReadAsync(await CurrentUser(), input.Id);
            return Success();
        });

    [HttpPost("read-all"), RequestSizeLimit(32768), RequirePermission("oa-inbox", "read")]
    public Task<IActionResult> ReadAll([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReadAllReq? input, CancellationToken ct)
        => Reply(async () =>
        {
            if (!observed.IsAvailable)
                throw new ScopeAuthorizationExceptionV1("NOTIFICATION_SCOPE_UNAVAILABLE");
            await observed.ReadThroughAsync(input?.Watermark, ct);
            return Success();
        });

    public record IdReq(Guid Id);
    public record ReadAllReq(string? Watermark);

    private async Task<Guid> CurrentUser()
    {
        if (User.HasClaim(x => x.Type == "impersonator_id"))
            throw new ScopeAuthorizationExceptionV1("REAUTH_REQUIRED", 401);
        return (await actor.GetAsync()).UserId;
    }

    private IActionResult Success(object? data = null) => Ok(new { code = 0, message = "OK", data });

    private async Task<IActionResult> Reply(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (ScopeAuthorizationExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
        catch (CurrentPermissionUnavailableException) { return StatusCode(403, new { code = "ACTION_FORBIDDEN" }); }
        catch (Exception error) when (error is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        { return StatusCode(503, new { code = "NOTIFICATION_STORAGE_UNAVAILABLE" }); }
    }
}
