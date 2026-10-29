using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Controllers.Wms;

// This is the external formal transport boundary; internal calls use the Owner's typed contract.
public sealed class RmaFormalResultAttribute : ActionFilterAttribute
{
    public RmaFormalResultAttribute() { Order = -3000; }
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
        var correlation = http.Request.Headers["X-Correlation-Id"].ToString();
        var queryPath = http.Request.Path.Value?.Contains("/authorization",StringComparison.Ordinal) == true
            ? "/api/wms/rma/v1/authorization/commands/query" : http.Request.Path.StartsWithSegments("/api/wms/rma/v1/delivery-evidence")
            ? "/api/wms/rma/v1/delivery-evidence/commands/query" : http.Request.Path.StartsWithSegments("/api/wms/rma/v1/policies")
            ? "/api/wms/rma/v1/policies/commands/query" : "/api/wms/rma/v1/commands/query";
        if (!context.ModelState.IsValid || string.IsNullOrWhiteSpace(correlation) || correlation.Length > 200
            || !http.Request.Headers.CacheControl.ToString().Split(',').Any(x => x.Trim().Equals("no-store", StringComparison.OrdinalIgnoreCase)))
        { context.Result = Problem(400, "VALIDATION_FAILED", correlation, http.TraceIdentifier, queryPath); return; }
        var result = await next();
        if (result.Exception is OrderPriceCorrectionException error)
        { result.Result = Problem(error.Status, error.Message, correlation, http.TraceIdentifier, queryPath); result.ExceptionHandled = true; }
        else if (result.Exception is ScopeAuthorizationExceptionV1 scope)
        { result.Result = Problem(scope.Status, scope.Message, correlation, http.TraceIdentifier, queryPath); result.ExceptionHandled = true; }
    }
    private static ObjectResult Problem(int status, string code, string correlation, string trace, string queryPath)
    {
        var unknown = code == "OPERATIONS_OUTCOME_UNKNOWN";
        return new(new { status, code, title = code, detail = code, correlationId = string.IsNullOrWhiteSpace(correlation) || correlation.Length > 200 ? trace : correlation,
            retryable = false, commitKnowledge = unknown ? "UNKNOWN" : "NOT_ACCEPTED", bindingCreated = false, workCreated = false,
            terminalReceiptCreated = false, recovery = new { action = unknown ? "QUERY_ORIGINAL" : status == 412 ? "REFRESH_HEAD" : "NONE",
                queryPath = unknown ? queryPath : null, originalCommandRef = (object?)null,
                originalEffectKey = (string?)null, ownerQueryIdentity = (object?)null }, fieldErrors = Array.Empty<object>() }) { StatusCode = status };
    }
}

[ApiController, Authorize, Route("api/wms/rma/v1"), RmaFormalResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RmaFormalController(RmaFormalOwnerV1 owner) : ControllerBase
{
    [HttpGet("applications/{applicationId:guid}/authorization/context")]
    public async Task<IActionResult> AuthorizationContext(Guid applicationId, bool includeEntitlements, CancellationToken ct) => Ok(await owner.AuthorizationContextAsync(applicationId,ct,includeEntitlements));
    [HttpGet("applications/{applicationId:guid}/authorization")]
    public async Task<IActionResult> Authorization(Guid applicationId, int? version, CancellationToken ct) => new JsonResult(await owner.ReadAuthorizationAsync(applicationId,version,ct));
    [HttpPost("applications/{applicationId:guid}/authorization/decide"),RequestSizeLimit(524288)]
    public async Task<IActionResult> Decide(Guid applicationId, RmaAuthorizationDecideV1 input, CancellationToken ct) => Ok(await owner.DecideAuthorizationAsync(applicationId,Command(),input,ct));
    [HttpPost("applications/{applicationId:guid}/authorization/revoke"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Revoke(Guid applicationId, RmaAuthorizationRevokeV1 input, CancellationToken ct) => Ok(await owner.RevokeAuthorizationAsync(applicationId,Command(),input,ct));
    [HttpPost("authorization/commands/query"),RequestSizeLimit(65536)]
    public async Task<IActionResult> AuthorizationCommand(RmaAuthorizationQueryV1 input, CancellationToken ct) => Ok(await owner.QueryAuthorizationCommandAsync(input,ct));
    [HttpPost("delivery-evidence/record"), RequestSizeLimit(131072)]
    public async Task<IActionResult> RecordEvidence(RmaDeliveryEvidenceRecordV1 input, CancellationToken ct) => Ok(await owner.RecordDeliveryEvidenceAsync(Command(), input, ct));
    [HttpPost("delivery-evidence/withdrawals"), RequestSizeLimit(65536)]
    public async Task<IActionResult> WithdrawEvidence(RmaDeliveryEvidenceWithdrawV1 input, CancellationToken ct) => Ok(await owner.WithdrawDeliveryEvidenceAsync(Command(), input, ct));
    [HttpPost("delivery-evidence/search"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Evidence(RmaDeliveryEvidenceQueryV1 query, CancellationToken ct) => Ok(await owner.DeliveryEvidenceAsync(query, ct));
    [HttpPost("delivery-evidence/files/search"), RequestSizeLimit(65536)]
    public async Task<IActionResult> EvidenceFiles(RmaEvidenceFileQueryV1 query, CancellationToken ct) => Ok(await owner.DeliveryEvidenceFilesAsync(query, ct));
    [HttpPost("delivery-evidence/commands/query"), RequestSizeLimit(65536)]
    public async Task<IActionResult> EvidenceCommand(RmaPolicyCommandQueryV1 query, CancellationToken ct) => Ok(await owner.QueryDeliveryEvidenceCommandAsync(query, ct));
    [HttpPost("policies/read"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Policy(RmaPolicyReadV1 query, CancellationToken ct) => Ok(await owner.ReadPolicyAsync(query, ct));
    [HttpPost("policies/publish"), RequestSizeLimit(65536)]
    public async Task<IActionResult> PublishPolicy(RmaPolicyPublishV1 input, CancellationToken ct) => Ok(await owner.PublishPolicyAsync(Command(), input, ct));
    [HttpPost("policies/commands/query"), RequestSizeLimit(65536)]
    public async Task<IActionResult> PolicyCommand(RmaPolicyCommandQueryV1 query, CancellationToken ct) => Ok(await owner.QueryPolicyCommandAsync(query, ct));
    private Guid Command() => Guid.TryParse(Request.Headers["Idempotency-Key"], out var id) && id != Guid.Empty
        ? id : throw new OrderPriceCorrectionException("VALIDATION_FAILED", 400);
    private IActionResult Application(RmaFormalApplicationV1 value, int status = 200)
    { Response.Headers.ETag = '"' + value.ApplicationRef.Digest + '"'; return StatusCode(status, value); }
    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct) => Ok(await owner.ContextAsync(ct));
    [HttpPost("original-fulfilment-options/search"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Sources(RmaFulfilmentQueryV1 query, CancellationToken ct) => Ok(await owner.SourcesAsync(query, ct));
    [HttpPost("applications"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Create(RmaFormalCreateV1 command, CancellationToken ct) => Application(await owner.CreateAsync(Command(), command, ct), 201);
    [HttpGet("applications")]
    public async Task<IActionResult> List([FromQuery] RmaApplicationQueryV1 query, CancellationToken ct) => Ok(await owner.ListAsync(query, ct));
    [HttpGet("applications/{applicationId:guid}/edit-context")]
    public async Task<IActionResult> EditContext(Guid applicationId, CancellationToken ct) => Ok(await owner.EditContextAsync(applicationId, ct));
    [HttpPut("applications/{applicationId:guid}"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Edit(Guid applicationId, RmaFormalEditV1 command, CancellationToken ct) =>
        Application(await owner.EditAsync(applicationId, Command(), command, Request.Headers.IfMatch, ct));
    [HttpPost("applications/{applicationId:guid}/submit"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Submit(Guid applicationId, RmaFormalTransitionV1 command, CancellationToken ct) =>
        Application(await owner.SubmitAsync(applicationId, Command(), command, Request.Headers.IfMatch, ct));
    [HttpPost("applications/{applicationId:guid}/withdrawals"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Withdraw(Guid applicationId, RmaFormalTransitionV1 command, CancellationToken ct) =>
        Application(await owner.WithdrawAsync(applicationId, Command(), command, Request.Headers.IfMatch, ct));
    [HttpGet("applications/{applicationId:guid}")]
    public async Task<IActionResult> Read(Guid applicationId, [FromQuery] int? version, CancellationToken ct) => Application(await owner.ReadAsync(applicationId, version, ct));
    [HttpPost("commands/query"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Query(RmaCommandQueryV1 query, CancellationToken ct) => Ok(await owner.QueryCommandAsync(query, ct));
}
