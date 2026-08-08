using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa")]
[Authorize]
public sealed class DraftController : ControllerBase
{
    private readonly IDraftService _drafts;
    private readonly ICurrentPermissionContext _permission;

    public DraftController(IDraftService drafts, ICurrentPermissionContext permission)
    {
        _drafts = drafts;
        _permission = permission;
    }

    [HttpPost("forms/{formKey}/drafts")]
    [RequirePermission("oa-form-catalog", "add")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Create(
        string formKey, [FromBody] CreateDraftRequest request, CancellationToken ct) =>
        await Execute(() => _drafts.CreateAsync(UserId(), formKey, request.Data, request.Title, ct));

    [HttpGet("drafts")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        await Execute(() => _drafts.ListAsync(UserId(), page, pageSize, ct));

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<IActionResult> Get(Guid draftId, CancellationToken ct) =>
        await Execute(() => _drafts.GetAsync(UserId(), draftId, ct));

    [HttpPut("drafts/{draftId:guid}")]
    [RequirePermission("oa-form-catalog", "edit")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Update(
        Guid draftId, [FromBody] UpdateDraftRequest request, CancellationToken ct) =>
        await Execute(() => _drafts.UpdateAsync(
            UserId(), draftId, request.Data, request.Title, Decode(request.RowVersion), ct));

    [HttpPost("drafts/{draftId:guid}/rebase")]
    [RequirePermission("oa-form-catalog", "edit")]
    public async Task<IActionResult> Rebase(
        Guid draftId, [FromBody] RebaseDraftRequest request, CancellationToken ct) =>
        await Execute(() => _drafts.RebaseAsync(UserId(), draftId, request.TargetVersion,
            request.ConfirmRemovedValues, Decode(request.RowVersion), ct));

    [HttpPost("drafts/{draftId:guid}/submit")]
    [RequirePermission("oa-form-catalog", "submit")]
    public async Task<IActionResult> Submit(
        Guid draftId, [FromBody] SubmitDraftRequest request, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keys) ||
            string.IsNullOrWhiteSpace(keys.FirstOrDefault()))
            return BadRequest(new { code = "E-WF-044", message = "E-WF-044" });
        return await Execute(() => _drafts.SubmitAsync(
            UserId(), draftId, keys.First()!, Decode(request.RowVersion), ct));
    }

    [HttpDelete("drafts/{draftId:guid}")]
    [RequirePermission("oa-form-catalog", "del")]
    public async Task<IActionResult> Delete(Guid draftId, CancellationToken ct) =>
        await Execute(async () =>
        {
            await _drafts.DeleteAsync(UserId(), draftId, ct);
            return true;
        });

    private Guid UserId() => _permission.GetAsync().GetAwaiter().GetResult().UserId;

    private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(new { code = 0, message = "OK", data = await action() }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DraftRebaseConfirmationException ex)
        {
            return Conflict(new { code = "E-WF-048", message = "E-WF-048", removedFields = ex.RemovedFields });
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Split(':')[0];
            var status = code is "E-WF-040" or "E-WF-041" or "E-WF-048" or "E-WF-044" ? 409 : 400;
            return StatusCode(status, new { code, message = ex.Message });
        }
    }

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("E-WF-041"); }
    }

    public sealed record CreateDraftRequest(JsonElement Data, string? Title);
    public sealed record UpdateDraftRequest(JsonElement Data, string? Title, string? RowVersion);
    public sealed record RebaseDraftRequest(int TargetVersion, bool ConfirmRemovedValues, string? RowVersion);
    public sealed record SubmitDraftRequest(string? RowVersion);
}
