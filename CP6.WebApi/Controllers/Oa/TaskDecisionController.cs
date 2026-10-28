using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa/tasks")]
[Authorize]
public sealed class TaskDecisionController : ControllerBase
{
    private readonly ITaskDecisionService _decisions;
    private readonly ICurrentPermissionContext _context;
    private readonly IDelegateService _delegates;
    private readonly ApprovalTaskDecisionOwnerV2 _native;
    private readonly IPermissionService _permissions;

    public TaskDecisionController(
        ITaskDecisionService decisions, ICurrentPermissionContext context, IDelegateService delegates,
        ApprovalTaskDecisionOwnerV2 native, IPermissionService permissions)
    {
        _decisions = decisions;
        _context = context;
        _delegates = delegates;
        _native = native;
        _permissions = permissions;
    }

    [HttpPost("{taskId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid taskId, [FromBody] DecisionRequest request, CancellationToken ct)
    {
        try
        {
            if (await _native.IsNativeAsync(taskId, ct))
            {
                if (request.CommandId == null || request.TaskRowVersion == null || request.InstanceRowVersion == null
                    || request.ExpectedFormDataRowVersion != null || request.DataPatch.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                    throw new ScopeAuthorizationExceptionV1("DECISION_INPUT_INVALID", 422);
                var nativeResult = await _native.DecideAsync(taskId, new(request.CommandId.Value, request.Decision,
                    request.Comment ?? "", request.TaskRowVersion, request.InstanceRowVersion), ActingAs(), Request.Headers.IfMatch.FirstOrDefault(), ct);
                return Ok(nativeResult);
            }
            // Preserve the existing legacy permission gate. Native replay above is a
            // current read of an original result and must not require a revoked write grant.
            if (!await _permissions.HasActionAsync("oa-inbox", "approve"))
                return StatusCode(403, new { code = 403, message = "无权限：oa-inbox:approve" });
            var actual = (await _context.GetAsync()).UserId;
            var effective = actual;
            var header = Request.Headers["X-Acting-As"].ToString();
            if (Guid.TryParse(header, out var actingAs) && actingAs != Guid.Empty && actingAs != actual)
            {
                await _delegates.AssertActiveGrantAsync(actual, actingAs);
                effective = actingAs;
            }
            var result = await _decisions.DecideAsync(new(
                taskId, actual, effective, request.Decision, request.Comment,
                request.DataPatch, Decode(request.ExpectedFormDataRowVersion)), ct);
            return Ok(new { code = 0, message = "OK", data = result });
        }
        catch (ScopeAuthorizationExceptionV1 ex) { return NativeError(ex, request.CommandId); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException ex)
        { return NativeError(new(ex.Message, ex.Status), request.CommandId); }
        catch (CurrentPermissionUnavailableException)
        { return NativeError(new("AUTHORITY_UNAVAILABLE", 503), request.CommandId); }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Split(':')[0];
            var status = code == "E-WF-049" ? 409 : code == "E-WF-004" ? 409 : 400;
            return StatusCode(status, new { code, message = ex.Message });
        }
    }

    [HttpGet("{taskId:guid}/decision-context")]
    public async Task<IActionResult> DecisionContext(Guid taskId, CancellationToken ct)
    {
        try
        {
            var result = await _native.ContextAsync(taskId, ActingAs(), ct);
            Response.Headers.ETag = "\"" + result.TaskRowVersion + "\"";
            Response.Headers.CacheControl = "no-store";
            return Ok(result);
        }
        catch (ScopeAuthorizationExceptionV1 ex) { return NativeError(ex); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException ex)
        { return StatusCode(ex.Status, new { code = ex.Message, message = ex.Message }); }
    }
    [HttpGet("decision-commands/{commandId:guid}")]
    public async Task<IActionResult> Original(Guid commandId, CancellationToken ct)
    {
        try
        {
            Response.Headers.CacheControl = "no-store";
            var result = await _native.OriginalAsync(commandId, ct);
            return result == null ? NotFound() : Ok(result);
        }
        catch (ScopeAuthorizationExceptionV1 ex) { return NativeError(ex); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException ex)
        { return StatusCode(ex.Status, new { code = ex.Message, message = ex.Message }); }
    }
    private Guid? ActingAs()
    {
        var header = Request.Headers["X-Acting-As"].ToString();
        if (string.IsNullOrEmpty(header)) return null;
        if (!Guid.TryParseExact(header, "D", out var id) || id == Guid.Empty)
            throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID", 422);
        return id;
    }
    [HttpGet("document-review/{document:guid}")]
    public async Task<IActionResult> DocumentTasks(Guid document,CancellationToken ct)
    {
        try {Response.Headers.CacheControl="no-store";return Ok(await _native.DocumentTasksAsync(document,ActingAs(),ct));}
        catch(ScopeAuthorizationExceptionV1 ex){return NativeError(ex);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException ex){return NativeError(new(ex.Message,ex.Status));}
    }
    [HttpGet("requirement-set-review/{requirementSet:guid}")]
    public async Task<IActionResult> RequirementSetTasks(Guid requirementSet,CancellationToken ct)
    {
        try {Response.Headers.CacheControl="no-store";return Ok(await _native.RequirementSetTasksAsync(requirementSet,ActingAs(),ct));}
        catch(ScopeAuthorizationExceptionV1 ex){return NativeError(ex);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException ex){return NativeError(new(ex.Message,ex.Status));}
    }
    [HttpGet("iteration-review/{submission:guid}")]
    public async Task<IActionResult> IterationTasks(Guid submission,CancellationToken ct)
    {
        try {Response.Headers.CacheControl="no-store";return Ok(await _native.IterationTasksAsync(submission,ActingAs(),ct));}
        catch(ScopeAuthorizationExceptionV1 ex){return NativeError(ex);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException ex){return NativeError(new(ex.Message,ex.Status));}
    }
    [HttpPost("{taskId:guid}/decision-commands/{commandId:guid}/close")]
    public async Task<IActionResult> CloseCommand(Guid taskId,Guid commandId,CancellationToken ct)
    {
        try {Response.Headers.CacheControl="no-store";return Ok(await _native.CloseAsync(taskId,commandId,ct));}
        catch(ScopeAuthorizationExceptionV1 ex){return NativeError(ex,commandId);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException ex){return NativeError(new(ex.Message,ex.Status),commandId);}
    }
    private IActionResult NativeError(ScopeAuthorizationExceptionV1 error, Guid? commandId = null)
        => StatusCode(error.Status, new { code = error.Message, messageKey = error.Message, correlationId = Guid.NewGuid(),
            retryable = error.Status is 503 or 504, fieldErrors = Array.Empty<object>(), currentRowVersion = (string?)null,
            resultQueryRef = commandId is Guid id ? "/api/oa/tasks/decision-commands/" + id : null });

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("E-WF-049"); }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DecisionRequest(string Decision, string? Comment, JsonElement DataPatch = default,
        string? ExpectedFormDataRowVersion = null, Guid? CommandId = null, string? TaskRowVersion = null, string? InstanceRowVersion = null);
}
