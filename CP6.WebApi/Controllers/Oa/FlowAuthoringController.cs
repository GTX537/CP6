using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FlowAuthoringController(FlowAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet("definitions/FLOW/entry")]
    public Task<IActionResult> Entry(CancellationToken ct) => Run(async () => await owner.EntryAsync(ct));
    [HttpGet("definitions/FLOW")]
    public Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ListAsync(page, ct));
    [HttpGet("definitions/FLOW/{key}/draft")]
    public Task<IActionResult> Read(string key, CancellationToken ct) => Run(async () => await owner.ReadAsync(key, ct));
    [HttpPut("definitions/FLOW/{key}/draft"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Save(string key, CancellationToken ct) => Run(async () =>
    {
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + count > 1024 * 1024) throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT", 400);
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return await owner.SaveAsync(key, bytes.ToArray(), ct);
    });
    [HttpGet("commands/{commandId}")]
    public Task<IActionResult> Original(string commandId, [FromQuery] string operation, [FromQuery] string resource, CancellationToken ct) => Run(async () =>
    {
        if (operation != FlowAuthoringOwnerV2.Operation || string.IsNullOrWhiteSpace(resource) || resource.Length > 100)
            throw new ScopeAuthorizationExceptionV1("COMMAND_SCOPE_INVALID", 400);
        return await owner.OriginalAsync(resource, commandId, ct);
    });
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e)
        {
            return StatusCode(e.Status, new { code = e.Message, messageKey = e.Message, fieldErrors = Array.Empty<object>(),
                retryMode = e.Status == 503 ? "READ_ORIGINAL" : e.Status == 409 ? "REFRESH_AND_NEW_INTENT" : "NONE" });
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE", retryMode = "READ_ORIGINAL" }); }
    }
}

[ApiController, Authorize, Route("api/iam/workflow-authoring-policy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FlowAuthoringPolicyController(FlowAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Read(CancellationToken ct) => Run(async () => await owner.PolicyAsync(ct));
    [HttpPost]
    public Task<IActionResult> Save(FlowAuthoringPolicyCommandV2 command, CancellationToken ct) => Run(async () => await owner.ConfigureAsync(command, ct));
    [HttpGet("operations/{operation:guid}")]
    public Task<IActionResult> Original(Guid operation, CancellationToken ct) => Run(async () => await owner.PolicyOriginalAsync(operation, ct));
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE" }); }
    }
}
