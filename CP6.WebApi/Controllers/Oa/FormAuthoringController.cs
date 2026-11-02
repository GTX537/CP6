using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2/definitions/FORM")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormAuthoringController(FormAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet("entry")]
    public Task<IActionResult> Entry(CancellationToken ct) => Run(async () => await owner.EntryAsync(ct));
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ListAsync(page, ct));
    [HttpGet("{key}/draft")]
    public Task<IActionResult> Read(string key, CancellationToken ct) => Run(async () => await owner.ReadAsync(key, ct));
    [HttpPut("{key}/draft"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Save(string key, CancellationToken ct) => Run(async () => await owner.SaveAsync(key, await Raw(ct), ct));
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + count > 1024 * 1024) throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT", 400);
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return bytes.ToArray();
    }
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

[ApiController, Authorize, Route("api/iam/form-authoring-policy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormAuthoringPolicyController(FormAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Read(CancellationToken ct) => Run(async () => await owner.PolicyAsync(ct));
    [HttpPost]
    public Task<IActionResult> Save(FormAuthoringPolicyCommandV2 command, CancellationToken ct) => Run(async () => await owner.ConfigureAsync(command, ct));
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
