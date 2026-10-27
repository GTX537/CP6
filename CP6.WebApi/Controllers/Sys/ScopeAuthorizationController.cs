using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Authorize, Route("api/iam/scope-authorization")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ScopeAuthorizationController(ScopeAuthorizationAdministrationV1 owner) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Context(CancellationToken ct) => Run(async () => await owner.ContextAsync(ct));
    [HttpPost]
    public Task<IActionResult> Register(ScopeAuthorizationCommandV1 command, CancellationToken ct) => Run(async () => await owner.RegisterAsync(command, ct));
    [HttpGet("operations/{operation:guid}")]
    public Task<IActionResult> Operation(Guid operation, CancellationToken ct) => Run(async () => await owner.OperationAsync(operation, ct));
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "SCOPE_AUTHORITY_UNAVAILABLE" }); }
    }
}
