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
    [HttpGet("export-payloads/context")]
    public Task<IActionResult> PayloadContext(CancellationToken ct)=>Run((o,s)=>o.PayloadContextAsync(s,ct));
    [HttpGet("export-payloads/policies/{id:guid}")]
    public Task<IActionResult> PayloadPolicy(Guid id,CancellationToken ct)=>Run((o,s)=>o.PayloadPolicyAsync(id,s,ct));
    [HttpPost("export-payloads/policies"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadPolicy(PayloadDispositionPolicyCommandV1 input,CancellationToken ct)=>Run((o,s)=>o.ConfigurePayloadPolicyAsync(input,s,ct));
    [HttpPost("export-payloads/preview"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadPreview(PayloadDispositionTargetV1 input,CancellationToken ct)=>Run((o,s)=>o.PayloadPreviewAsync(input,s,ct));
    [HttpPost("export-payloads/artifacts/{id:guid}/hold"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadHold(Guid id,PayloadDispositionHoldV1 input,CancellationToken ct)=>Run((o,s)=>o.SetPayloadHoldAsync(id,input,s,ct));
    [HttpGet("export-payloads/artifacts/{id:guid}/hold")]
    public Task<IActionResult> PayloadHold(Guid id,CancellationToken ct)=>Run((o,s)=>o.PayloadHoldAsync(id,s,ct));
    [HttpPost("export-payloads/plans"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadPlan(PayloadDispositionRequestV1 input,CancellationToken ct)=>Run((o,s)=>o.RequestPayloadDispositionAsync(input,s,ct));
    [HttpGet("export-payloads/plans/{id:guid}")]
    public Task<IActionResult> PayloadPlan(Guid id,CancellationToken ct)=>Run((o,s)=>o.PayloadPlanAsync(id,s,ct));
    [HttpPost("export-payloads/plans/{id:guid}/decisions"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadDecision(Guid id,PayloadDispositionDecisionV1 input,CancellationToken ct)=>Run((o,s)=>o.DecidePayloadDispositionAsync(id,input,s,ct));
    [HttpPost("export-payloads/plans/{id:guid}/execute"),RequestSizeLimit(65536)]
    public Task<IActionResult> PayloadExecute(Guid id,PayloadDispositionExecuteV1 input,CancellationToken ct)=>Run((o,s)=>o.ExecutePayloadDispositionAsync(id,input,s,ct));
    [HttpGet("export-payloads/operations/{id:guid}")]
    public Task<IActionResult> PayloadOperation(Guid id,CancellationToken ct)=>Run((o,s)=>o.PayloadOperationAsync(id,false,s,ct));
    [HttpPost("export-payloads/operations/{id:guid}/close")]
    public Task<IActionResult> PayloadClose(Guid id,CancellationToken ct)=>Run((o,s)=>o.PayloadOperationAsync(id,true,s,ct));
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
    [HttpGet("plans/{id:guid}/execution")]
    public Task<IActionResult> Execution(Guid id,CancellationToken ct)=>Run((o,s)=>o.ExecutionAsync(id,s,ct));
    [HttpPost("plans/{id:guid}/execute"),RequestSizeLimit(65536)]
    public Task<IActionResult> Execute(Guid id,PrivacyDispositionExecuteV1 input,CancellationToken ct)=>Run((o,s)=>o.ExecuteAsync(id,input,s,ct));
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,CancellationToken ct)=>Run((o,s)=>o.OperationAsync(id,true,s,ct));
    [HttpGet("restore/context")]
    public Task<IActionResult> RestoreContext(CancellationToken ct)=>Run((o,s)=>o.RestoreContextAsync(s,ct));
    [HttpGet("restore/candidates/{id:guid}/preview")]
    public Task<IActionResult> RestorePreview(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestorePreviewAsync(id,s,ct));
    [HttpPost("restore/plans"),RequestSizeLimit(65536)]
    public Task<IActionResult> RestorePlan(PrivacyRestorePlanRequestV1 input,CancellationToken ct)=>Run((o,s)=>o.RequestRestoreAsync(input,s,ct));
    [HttpGet("restore/plans/{id:guid}")]
    public Task<IActionResult> RestorePlan(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestorePlanAsync(id,s,ct));
    [HttpPost("restore/plans/{id:guid}/decisions"),RequestSizeLimit(65536)]
    public Task<IActionResult> RestoreDecision(Guid id,PrivacyDispositionDecisionV1 input,CancellationToken ct)=>Run((o,s)=>o.DecideRestoreAsync(id,input,s,ct));
    [HttpPost("restore/plans/{id:guid}/execute"),RequestSizeLimit(65536)]
    public Task<IActionResult> RestoreExecute(Guid id,PrivacyRestoreExecuteV1 input,CancellationToken ct)=>Run((o,s)=>o.ExecuteRestoreAsync(id,input,s,ct));
    [HttpGet("restore/metadata-operations/{id:guid}")]
    public Task<IActionResult> RestoreMetadataOperation(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestoreMetadataOperationAsync(id,false,s,ct));
    [HttpPost("restore/metadata-operations/{id:guid}/close")]
    public Task<IActionResult> CloseRestoreMetadata(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestoreMetadataOperationAsync(id,true,s,ct));
    [HttpGet("restore/operations/{id:guid}")]
    public Task<IActionResult> RestoreOperation(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestoreOperationAsync(id,"READ",s,ct));
    [HttpPost("restore/operations/{id:guid}/recover")]
    public Task<IActionResult> RecoverRestoreOperation(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestoreOperationAsync(id,"RECOVER",s,ct));
    [HttpPost("restore/operations/{id:guid}/close")]
    public Task<IActionResult> CloseRestoreOperation(Guid id,CancellationToken ct)=>Run((o,s)=>o.RestoreOperationAsync(id,"CLOSE",s,ct));
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
