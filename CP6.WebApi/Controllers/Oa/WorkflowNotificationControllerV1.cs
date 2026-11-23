using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkflowNotificationControllerV1(WorkflowNotificationPreferencesV1 preferences, WorkflowNotificationInboxV1 inbox)
    : ControllerBase
{
    [HttpGet("notifications")]
    public Task<IActionResult> List(bool unreadOnly = false, int take = 20, string? asOf = null, string? cursor = null,
        CancellationToken ct = default) => Reply(() => inbox.ListAsync(unreadOnly, take, asOf, cursor, ct));

    [HttpGet("notifications/unread-count")]
    public Task<IActionResult> Count(CancellationToken ct) => Reply(async () => new { count = await inbox.UnreadCountAsync(ct) });

    [HttpPost("notifications/{id:guid}/read"), RequirePermission("oa-inbox","read")]
    public Task<IActionResult> Read(Guid id, CancellationToken ct) => Reply(async () => new { changed = await inbox.MarkReadAsync(id, ct) });

    public sealed record ReadThroughRequest(string Watermark);

    [HttpPost("notifications/read-through"), RequestSizeLimit(32768), RequirePermission("oa-inbox","read")]
    public Task<IActionResult> ReadThrough(ReadThroughRequest req, CancellationToken ct)
        => Reply(async () => new { changed = await inbox.ReadThroughAsync(req.Watermark, ct) });

    [HttpGet("notification-preferences")]
    public Task<IActionResult> GetPreferences(CancellationToken ct) => Reply(() => preferences.ReadAsync(ct));

    [HttpPut("notification-preferences"), RequestSizeLimit(NotifyMatrix.MaxPreferenceBytes), RequirePermission("oa-settings","edit")]
    public Task<IActionResult> SetPreferences(CancellationToken ct) => Reply(async () =>
    {
        using var bytes = new MemoryStream();
        await Request.Body.CopyToAsync(bytes, ct);
        return await preferences.SaveAsync(bytes.ToArray(), ct);
    });

    [HttpGet("notification-preferences/commands/{commandId}")]
    public Task<IActionResult> Original(string commandId, CancellationToken ct)
        => Reply(() => preferences.OriginalAsync(commandId, ct));

    [HttpGet("notification-deliveries")]
    public Task<IActionResult> Deliveries(CancellationToken ct) => Reply(() => inbox.DeliveriesAsync(ct));

    private async Task<IActionResult> Reply<T>(Func<Task<T>> read)
    {
        try
        {
            var value = await read();
            return value is null ? NotFound(new { code = "NOTIFICATION_COMMAND_NOT_FOUND" }) : Ok(new { code = 0, data = value });
        }
        catch (ScopeAuthorizationExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
        catch (CurrentPermissionUnavailableException) { return StatusCode(403, new { code = "ACTION_FORBIDDEN" }); }
        catch (Exception error) when (error is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        { return StatusCode(503, new { code = "NOTIFICATION_STORAGE_UNAVAILABLE" }); }
    }
}
