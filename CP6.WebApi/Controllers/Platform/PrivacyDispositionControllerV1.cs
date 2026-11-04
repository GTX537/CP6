using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Platform;
[ApiController,Authorize,Route("api/platform/privacy-dispositions-v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PrivacyDispositionControllerV1(IServiceScopeFactory scopes,ITenantContext tenant):ControllerBase
{
    [HttpGet("context")]
    public Task<IActionResult> Context(CancellationToken ct)=>Run((o,s)=>o.ContextAsync(s,ct));
    [HttpGet("policies/{id:guid}")]
    public Task<IActionResult> Policy(Guid id,CancellationToken ct)=>Run((o,s)=>o.PolicyAsync(id,s,ct));
    [HttpPost("policies"),RequestSizeLimit(65536)]
    public Task<IActionResult> Policy(PrivacyDispositionPolicyCommandV1 input,CancellationToken ct)=>Run((o,s)=>o.ConfigurePolicyAsync(input,s,ct));
    [HttpGet("holds/{targetTenant:guid}/{subject:guid}")]
    public Task<IActionResult> Hold(Guid targetTenant,Guid subject,CancellationToken ct)=>Run((o,s)=>o.HoldAsync(targetTenant,subject,s,ct));
    [HttpPost("holds"),RequestSizeLimit(65536)]
    public Task<IActionResult> Hold(PrivacyDispositionHoldCommandV1 input,CancellationToken ct)=>Run((o,s)=>o.SetHoldAsync(input,s,ct));
    [HttpPost("preview"),RequestSizeLimit(65536)]
    public Task<IActionResult> Preview(PrivacyDispositionTargetV1 input,CancellationToken ct)=>Run((o,s)=>o.PreviewAsync(input,s,ct));
    [HttpPost("plans"),RequestSizeLimit(65536)]
    public Task<IActionResult> Plan(PrivacyDispositionPlanCommandV1 input,CancellationToken ct)=>Run((o,s)=>o.RequestAsync(input,s,ct));
    [HttpGet("plans/{id:guid}")]
    public Task<IActionResult> Plan(Guid id,CancellationToken ct)=>Run((o,s)=>o.PlanAsync(id,s,ct));
    [HttpPost("plans/{id:guid}/decisions"),RequestSizeLimit(65536)]
    public Task<IActionResult> Decide(Guid id,PrivacyDispositionDecisionV1 input,CancellationToken ct)=>Run((o,s)=>o.DecideAsync(id,input,s,ct));
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Run((o,s)=>o.OperationAsync(id,false,s,ct));
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,CancellationToken ct)=>Run((o,s)=>o.OperationAsync(id,true,s,ct));
    private async Task<IActionResult> Run<T>(Func<PrivacyDispositionOwnerV1,TenantManagementSessionV1,Task<T>> action)
    {
        await using var scope=scopes.CreateAsyncScope();var services=scope.ServiceProvider;services.GetRequiredService<ITenantContext>().CurrentTenantId=tenant.CurrentTenantId;
        try{return Ok(await action(services.GetRequiredService<PrivacyDispositionOwnerV1>(),TenantAdministrationHttpV1.Session(HttpContext,services)));}
        catch(PrivacyDispositionExceptionV1 e){return StatusCode(e.Status,new{code=e.Message});}
        catch(PlatformAdminAuthorizationException e){return StatusCode(403,new{code=e.Message});}
        catch(IamAdministrationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message});}
        catch(CurrentPermissionUnavailableException){return StatusCode(503,new{code="PRIVACY_DISPOSITION_CURRENT_PERMISSION_UNAVAILABLE"});}
    }
}
