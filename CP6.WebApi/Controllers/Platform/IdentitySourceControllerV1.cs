using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

[ApiController, Authorize, Route("api/platform/tenant/{id:guid}/identity-source-v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IdentitySourceControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Current(Guid id, CancellationToken ct)
        => Execute((owner, session) => owner.ReadAsync(id, session, ct));
    [HttpPost("commands"), RequestSizeLimit(131072)]
    public Task<IActionResult> Publish(Guid id, IdentitySourceCommandV1 command, CancellationToken ct)
        => Execute((owner, session) => owner.PublishAsync(id, command, session, ct));
    [HttpPost("commands/read"), RequestSizeLimit(131072)]
    public Task<IActionResult> Original(Guid id, TenantManagementControllerV1.OriginalRequest command, CancellationToken ct)
        => Execute((owner, session) => owner.OriginalAsync(id, command.OperationRef, session, ct));
    private async Task<IActionResult> Execute(Func<IdentitySourceAdministrationV1, TenantManagementSessionV1, Task<byte[]>> action)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        try
        {
            var runtime = services.GetService<CrmIdentityRuntime>();
            if (runtime is null) return StatusCode(503, new { code = "C02_SOURCE_NOT_CONFIGURED" });
            var registry = services.GetService<InquiryServicePrincipalRegistryV1>();
            var required = registry is { IdentityPublicationEnabled: true }
                ? registry.Sources.GroupBy(x => x.Tenant).ToDictionary(x => x.Key, x => x.Select(s => s.Subject).ToArray()) : null;
            var owner = new IdentitySourceAdministrationV1(services.GetRequiredService<CP6Context>(), runtime, services.GetRequiredService<IHttpContextAccessor>(), required);
            return File(await action(owner, TenantAdministrationHttpV1.Session(HttpContext, services)), "application/json");
        }
        catch (TenantManagementExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (PlatformAdminAuthorizationException e) { return StatusCode(403, new { code = e.Message }); }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (IdentityReadException e) { return Conflict(new { code = e.Message }); }
    }
}
