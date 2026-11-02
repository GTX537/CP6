using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Authorize, Route("api/menu/tree-v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MenuTreeAdministrationControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OriginalRequest([property: JsonRequired][Required] string OperationRef);
    [HttpGet]
    public Task<IActionResult> Current(CancellationToken ct) => Execute((owner, session) => owner.ReadAsync(session, ct));
    [HttpPost("preview"), RequestSizeLimit(1048576)]
    public Task<IActionResult> Preview(MenuTreeCommandV1 command, CancellationToken ct) => Execute((owner, session) => owner.PreviewAsync(command, session, ct));
    [HttpPost("commands"), RequestSizeLimit(1048576)]
    public Task<IActionResult> Command(MenuTreeCommandV1 command, CancellationToken ct) => Execute((owner, session) => owner.ExecuteAsync(command, session, ct));
    [HttpPost("display/preview"), RequestSizeLimit(1048576)]
    public Task<IActionResult> DisplayPreview(MenuDisplayCommandV1 command, CancellationToken ct) => Execute((owner, session) => owner.PreviewDisplayAsync(command, session, ct));
    [HttpPost("display/commands"), RequestSizeLimit(1048576)]
    public Task<IActionResult> DisplayCommand(MenuDisplayCommandV1 command, CancellationToken ct) => Execute((owner, session) => owner.ExecuteDisplayAsync(command, session, ct));
    [HttpPost("commands/read"), RequestSizeLimit(1048576)]
    public Task<IActionResult> Original(OriginalRequest request, CancellationToken ct) => Execute((owner, session) => owner.ReadOriginalAsync(request.OperationRef, session, ct));
    private async Task<IActionResult> Execute<T>(Func<MenuTreeAdministrationV1, CP6.Core.Services.Platform.TenantManagementSessionV1, Task<T>> action)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        try
        {
            var value = await action(ActivatorUtilities.CreateInstance<MenuTreeAdministrationV1>(services), TenantAdministrationHttpV1.Session(HttpContext, services));
            return value == null ? NotFound(new { code = "MENU_TREE_OPERATION_NOT_FOUND" }) : Ok(value);
        }
        catch (MenuTreeExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (PlatformAdminAuthorizationException e) { return StatusCode(403, new { code = e.Message }); }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (CurrentPermissionUnavailableException) { return StatusCode(503, new { code = "MENU_TREE_PERMISSION_UNAVAILABLE" }); }
    }
}
