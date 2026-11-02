using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
[Route("api/platform/tenant/{id:guid}/management-v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantManagementControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OriginalRequest([property: JsonRequired][Required] string OperationRef);
    [HttpGet]
    public Task<IActionResult> Current(Guid id, CancellationToken ct)
        => Execute((service, session) => service.ReadManagementAsync(id, session, ct), ct);
    [HttpPost("commands"), RequestSizeLimit(131072)]
    public Task<IActionResult> Command(Guid id, TenantManagementCommandV1 command, CancellationToken ct)
        => Execute((service, session) => service.ExecuteManagementAsync(id, command, session, ct), ct);
    [HttpPost("commands/read"), RequestSizeLimit(131072)]
    public Task<IActionResult> Original(Guid id, OriginalRequest request, CancellationToken ct)
        => Execute((service, session) => service.ReadManagementOperationAsync(id, request.OperationRef, session, ct), ct);
    private async Task<IActionResult> Execute<T>(Func<ITenantAdminService, TenantManagementSessionV1, Task<T>> action, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        try
        {
            var session = new TenantManagementSessionV1(token => IamAdministrationHttpV1.CurrentActor(HttpContext, services, token), async token =>
            {
                // These claims were validated by CurrentActor. Database-backed authority is
                // held by the Owner gate; blacklist/clock remain external changing conditions.
                var expires = long.Parse(User.FindFirst("exp")!.Value, CultureInfo.InvariantCulture);
                if (expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    || await services.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(User.FindFirst("jti")!.Value))
                    throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                token.ThrowIfCancellationRequested();
            });
            var value = await action(services.GetRequiredService<ITenantAdminService>(), session);
            return value == null ? NotFound(new { code = "TENANT_NOT_FOUND" }) : Ok(value);
        }
        catch (TenantManagementExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (PlatformAdminAuthorizationException e) { return StatusCode(403, new { code = e.Message }); }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (InvalidOperationException e) when (e.Message == "E-WF-028") { return BadRequest(new { code = e.Message }); }
    }
}
