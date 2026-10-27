using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/app/v2")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApprovalBindingController(ApprovalBindingOwnerV2 owner) : ControllerBase
{
    [HttpGet("capabilities")]
    public Task<IActionResult> Capabilities(CancellationToken ct) => Run(async () => await owner.CapabilitiesAsync(ct));
    [HttpGet("binding-options")]
    public Task<IActionResult> Options(CancellationToken ct) => Run(async () => await owner.OptionsAsync(ct));
    [HttpGet("bindings")]
    public Task<IActionResult> List(string? cursor, CancellationToken ct) => Run(async () => await owner.ListAsync(cursor, ct));
    [HttpGet("bindings/{id:guid}/revisions")]
    public Task<IActionResult> History(Guid id, string? cursor, CancellationToken ct) => Run(async () => await owner.HistoryAsync(id, cursor, ct));
    [HttpGet("bindings/{id:guid}/timelines/{timeline:guid}")]
    public Task<IActionResult> Timeline(Guid id, Guid timeline, CancellationToken ct) => Run(async () => await owner.TimelineAsync(id, timeline, ct));
    [HttpGet("commands/{id:guid}")]
    public Task<IActionResult> Original(Guid id, CancellationToken ct) => Run(async () => await owner.CommandAsync(id, ct));
    [HttpPost("bindings"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Create(CancellationToken ct) => Run(async () => await owner.SaveAsync(null, null, await Raw(ct), ct), 201);
    [HttpPost("bindings/{id:guid}/revisions"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Revise(Guid id, CancellationToken ct) => Run(async () => await owner.SaveAsync(id, Request.Headers.IfMatch.FirstOrDefault(), await Raw(ct), ct), 201);
    [HttpPost("bindings/{id:guid}/disable"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Disable(Guid id, CancellationToken ct) => Run(async () => await owner.DisableAsync(id, Request.Headers.IfMatch.FirstOrDefault(), await Raw(ct), ct));
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > 1024 * 1024) throw new ScopeAuthorizationExceptionV1("BINDING_SHAPE_INVALID", 422);
            await body.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return body.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object>> action, int status = 200)
    {
        try { return new ContentResult { StatusCode = status, ContentType = "application/json; charset=utf-8", Content = ApprovalBindingOwnerV2.Serialize(await action()) }; }
        catch (ScopeAuthorizationExceptionV1 e) { return Failure(e.Status, e.Message); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException) { return Failure(503, "AUTHORITY_UNAVAILABLE"); }
    }
    private IActionResult Failure(int status, string code) => StatusCode(status, new { code, messageKey = code, correlationId = Guid.NewGuid(),
        retryable = status is 429 or 503 or 504, fieldErrors = Array.Empty<object>(), currentRowVersion = (string?)null, resultQueryRef = (string?)null });
}
