using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>Tenant ERP administrators can inspect and reschedule exhausted result and post-commit deliveries.</summary>
[ApiController]
[Authorize(Roles = "1,Admin")]
[Route("api/erp-integration/delivery-deadletters")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErpDeliveryReplayController(ITenantContext tenant, ErpIntegrationRuntime? runtime = null,
    ErpDeliveryReplayService? replay = null, ErpIntegrationOpsV1? ops = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (runtime is null || replay is null) return NotFound();
        if (Actor() is null || !runtime.Options.Enabled || !runtime.Options.Tenants.ContainsKey(tenant.CurrentTenantId)) return Forbid();
        try
        {
            if (ops is not null) await ops.RequireLegacyListReadAsync(tenant.CurrentTenantId, ct);
            var items = await replay.ListAsync(tenant.CurrentTenantId, ct);
            return Ok(new { items = items.Select(x => x with
            { ErrorCode = x.ErrorCode is null ? null : IntegrationOpsDirectoryV1.SafeCode(x.ErrorCode) }) });
        }
        catch (IntegrationOpsExceptionV1 ex) { return StatusCode(ex.Status, new { code = ex.Message }); }
        catch (ErpCommerceException ex) { return Failure(ex); }
        catch (Exception ex) when (IntegrationOpsContractV1.IsUnavailable(ex))
        { return StatusCode(503, new { code = "C03_ERP_UNAVAILABLE" }); }
    }

    [HttpPost("results/{outboxId:guid}/replay")]
    public Task<IActionResult> ScheduleResult(Guid outboxId, [FromBody] ErpReplayRequest input, CancellationToken ct) =>
        Schedule(IntegrationOpsContractV1.Outbox, outboxId.ToString("D"), input,
            actor => replay!.ScheduleResultAsync(tenant.CurrentTenantId, outboxId, input, actor, ct), ct);

    [HttpPost("bridges/{orderKey}/replay")]
    public Task<IActionResult> ScheduleBridge(string orderKey, [FromBody] ErpReplayRequest input, CancellationToken ct) =>
        Schedule(IntegrationOpsContractV1.Bridge, orderKey, input,
            actor => replay!.ScheduleBridgeAsync(tenant.CurrentTenantId, orderKey, input, actor, ct), ct);

    private async Task<IActionResult> Schedule(string type, string id, ErpReplayRequest input,
        Func<string, Task<ErpDeliveryReplayScheduled>> operation, CancellationToken ct)
    {
        if (runtime is null || replay is null) return NotFound();
        var actor = Actor();
        if (actor is null || !runtime.Options.Enabled || !runtime.Options.Tenants.ContainsKey(tenant.CurrentTenantId)) return Forbid();
        try
        {
            if (ops is null) return Accepted(await operation(actor));
            var admission = await ops.ScheduleLegacyAsync(tenant.CurrentTenantId, type, id, input, ct);
            return admission.Receipt.Status == "Blocked"
                ? Conflict(new { code = admission.Receipt.SafeCode, operationId = input.OperationId })
                : Accepted(new ErpDeliveryReplayScheduled(input.OperationId,
                    type == IntegrationOpsContractV1.Outbox ? "result" : "bridge", id,
                    admission.Receipt.OriginalMessageRef, admission.Receipt.AcceptedAtUtc));
        }
        catch (IntegrationOpsExceptionV1 ex) { return StatusCode(ex.Status, new { code = ex.Message }); }
        catch (ErpCommerceException ex) { return Failure(ex); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "C03_DELIVERY_REPLAY_PRECONDITION_FAILED" }); }
        catch (Exception ex) when (IntegrationOpsContractV1.IsUnavailable(ex))
        {
            return ops is null ? StatusCode(503, new { code = "C03_ERP_UNAVAILABLE" }) :
                StatusCode(503, new { code = "C03_ERP_UNAVAILABLE", status = "UNKNOWN", operationId = input.OperationId });
        }
    }

    private IActionResult Failure(ErpCommerceException exception)
    {
        var status = exception.Code switch
        {
            "C03_DELIVERY_REPLAY_NOT_FOUND" => 404,
            "C03_DELIVERY_REPLAY_TENANT_DISABLED" => 403,
            "C03_DELIVERY_REPLAY_PRECONDITION_FAILED" or "C03_DELIVERY_REPLAY_OPERATION_CONFLICT" or
                "C03_DELIVERY_REPLAY_NOT_DEADLETTERED" => 409,
            _ => 400
        };
        return StatusCode(status, new { code = exception.Code });
    }

    private string? Actor()
    {
        if (User.HasClaim(c => c.Type == "client_id" || (c.Type is "sub" or ClaimTypes.NameIdentifier &&
                c.Value.StartsWith("service:", StringComparison.Ordinal)))) return null;
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return id is { Length: > 0 and <= 100 } && !id.Any(char.IsControl) ? id : null;
    }
}
