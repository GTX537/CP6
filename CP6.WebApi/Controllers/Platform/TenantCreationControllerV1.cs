using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

[ApiController, Authorize]
[Route("api/platform/tenant/creation-v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantCreationControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OriginalRequest([property: JsonRequired][Required] string OperationRef);
    [HttpGet("template")]
    public Task<IActionResult> Template(CancellationToken ct)
        => Execute((service, session) => service.ReadCreationTemplateAsync(session, ct));
    [HttpPost("commands"), RequestSizeLimit(131072)]
    public Task<IActionResult> Command(TenantCreationCommandV1 command, CancellationToken ct)
        => Execute((service, session) => service.CreateVersionedAsync(command, session, ct));
    [HttpPost("commands/read"), RequestSizeLimit(131072)]
    public Task<IActionResult> Original(OriginalRequest request, CancellationToken ct)
        => Execute((service, session) => service.ReadCreationOperationAsync(request.OperationRef, session, ct));

    private async Task<IActionResult> Execute<T>(Func<ITenantAdminService, TenantManagementSessionV1, Task<T>> action)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        try
        {
            var value = await action(services.GetRequiredService<ITenantAdminService>(), TenantAdministrationHttpV1.Session(HttpContext, services));
            return value == null ? NotFound(new { code = "TENANT_OPERATION_NOT_FOUND" }) : Ok(value);
        }
        catch (TenantManagementExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (PlatformAdminAuthorizationException e) { return StatusCode(403, new { code = e.Message }); }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
    }
}
