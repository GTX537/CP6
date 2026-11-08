using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Platform;

[ApiController,Authorize,Route("api/platform/privacy-tenant-exports-v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TenantPrivacyExportControllerV1(IServiceScopeFactory scopes,ITenantContext tenant):ControllerBase
{
    [HttpGet("context")]
    public Task<IActionResult> Context(CancellationToken ct)=>Execute((o,s)=>o.TenantContextAsync(s,ct));
    [HttpPost("commands"),RequestSizeLimit(65536)]
    public Task<IActionResult> Generate(TenantPrivacyExportCommandV1 command,CancellationToken ct)=>Execute((o,s)=>o.GenerateTenantAsync(command,s,ct));
    [HttpGet("operations/{operation:guid}")]
    public Task<IActionResult> Original(Guid operation,CancellationToken ct)=>Execute((o,s)=>o.TenantOperationAsync(operation,false,s,ct));
    [HttpPost("operations/{operation:guid}/close")]
    public Task<IActionResult> Close(Guid operation,CancellationToken ct)=>Execute((o,s)=>o.TenantOperationAsync(operation,true,s,ct));
    [HttpGet("artifacts/{artifact:guid}")]
    public Task<IActionResult> Artifact(Guid artifact,CancellationToken ct)=>Execute((o,s)=>o.TenantArtifactAsync(artifact,s,ct));
    [HttpPost("artifacts/{artifact:guid}/revoke")]
    public Task<IActionResult> Revoke(Guid artifact,CancellationToken ct)=>Execute((o,s)=>o.RevokeTenantAsync(artifact,s,ct));
    [HttpGet("artifacts/{artifact:guid}/download")]
    public Task<IActionResult> Download(Guid artifact,CancellationToken ct)=>Execute((o,s)=>o.DownloadTenantAsync(artifact,s,ct));
    private async Task<IActionResult> Execute<T>(Func<PrivacyExportOwnerV1,TenantManagementSessionV1,Task<T>> action)
    {
        await using var scope=scopes.CreateAsyncScope();var services=scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId=tenant.CurrentTenantId;
        try
        {
            var value=await action(services.GetRequiredService<PrivacyExportOwnerV1>(),TenantAdministrationHttpV1.Session(HttpContext,services));
            if(value is PrivacyExportDownloadV1 download)return File(download.Bytes,"application/json",$"tenant-export-{download.ArtifactId:D}.json",enableRangeProcessing:false);
            return Ok(value);
        }
        catch(Npgsql.PostgresException e)when(e.SqlState is "42P01" or "3F000"){return StatusCode(503,new{code="PRIVACY_TENANT_STORAGE_UNAVAILABLE"});}
        catch(PrivacyExportExceptionV1 e){return StatusCode(e.Status,new{code=e.Message});}
        catch(PlatformAdminAuthorizationException e){return StatusCode(403,new{code=e.Message});}
        catch(IamAdministrationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message});}
        catch(CurrentPermissionUnavailableException){return StatusCode(503,new{code="PRIVACY_CURRENT_PERMISSION_UNAVAILABLE"});}
        catch(InvalidOperationException e)when(e.Message=="E-SEC-032"){return NotFound(new{code="PRIVACY_TENANT_TARGET_NOT_FOUND"});}
    }
}
