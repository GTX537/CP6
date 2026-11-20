using System.Data.Common;
using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>ERP tenant administrators can inspect failure metadata and schedule an audited original-message replay.</summary>
[ApiController]
[Authorize(Roles = "1,Admin")]
[Route("api/erp-integration/deadletters")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErpReplayController(ITenantContext tenant, ErpIntegrationRuntime? runtime = null,
    IDbContextFactory<ErpIntegrationContext>? factory = null, ErpInboxReplayService? replay = null,
    ErpIntegrationOpsV1? ops = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (runtime is null || factory is null) return NotFound();
        if (Actor() is null || !runtime.Options.Tenants.ContainsKey(tenant.CurrentTenantId)) return Forbid();
        try
        {
            if (ops is not null) await ops.RequireLegacyListReadAsync(tenant.CurrentTenantId, ct);
            await using var db = await factory.CreateDbContextAsync(ct);
            var items = await db.Inbox.AsNoTracking().Where(x => x.TenantId == tenant.CurrentTenantId && x.Status == ErpInboxStatus.DeadLettered)
                .OrderBy(x => x.ReceivedAtUtc).ThenBy(x => x.MessageId).Take(100)
                .Select(x => new { x.MessageId, x.EventType, x.AggregateId, x.AggregateVersion, x.AttemptCount,
                    x.PayloadSha256, x.ErrorCode, x.ReceivedAtUtc, x.ProcessedAtUtc, x.ReplayedAtUtc, x.RowVersion }).ToArrayAsync(ct);
            return Ok(new { items = items.Select(x => new { x.MessageId, x.EventType, x.AggregateId, x.AggregateVersion,
                x.AttemptCount, x.PayloadSha256, ErrorCode = x.ErrorCode is null ? null : IntegrationOpsDirectoryV1.SafeCode(x.ErrorCode),
                x.ReceivedAtUtc, x.ProcessedAtUtc, x.ReplayedAtUtc, x.RowVersion }) });
        }
        catch (IntegrationOpsExceptionV1 ex) { return StatusCode(ex.Status, new { code = ex.Message }); }
        catch (Exception ex) when (ex is DbException or TimeoutException)
        { return StatusCode(503, new { code = "C03_ERP_UNAVAILABLE" }); }
    }

    [HttpPost("{messageId}/replay")]
    public async Task<IActionResult> Schedule(string messageId, [FromBody] ErpReplayRequest input, CancellationToken ct)
    {
        if (runtime is null || replay is null) return NotFound();
        var actor = Actor();
        if (actor is null || !runtime.Options.Tenants.ContainsKey(tenant.CurrentTenantId)) return Forbid();
        try
        {
            if (ops is null) return Accepted(await replay.ScheduleAsync(tenant.CurrentTenantId, messageId, input, actor, ct));
            var admission = await ops.ScheduleLegacyAsync(tenant.CurrentTenantId, IntegrationOpsContractV1.Inbox, messageId, input, ct);
            return admission.Receipt.Status == "Blocked"
                ? Conflict(new { code = admission.Receipt.SafeCode, operationId = input.OperationId })
                : Accepted(new ErpReplayScheduled(input.OperationId, messageId, admission.Receipt.AcceptedAtUtc));
        }
        catch (IntegrationOpsExceptionV1 ex) { return StatusCode(ex.Status, new { code = ex.Message }); }
        catch (ErpCommerceException ex)
        {
            var status = ex.Code switch
            {
                "C03_REPLAY_TENANT_DISABLED" => 403,
                "C03_REPLAY_NOT_FOUND" => 404,
                "C03_REPLAY_PRECONDITION_FAILED" or "C03_REPLAY_OPERATION_CONFLICT" or "C03_REPLAY_NOT_DEADLETTERED" => 409,
                _ => 400
            };
            return StatusCode(status, new { code = ex.Code });
        }
        catch (Exception ex) when (IntegrationOpsContractV1.IsUnavailable(ex))
        {
            return ops is null ? StatusCode(503, new { code = "C03_ERP_UNAVAILABLE" }) :
                StatusCode(503, new { code = "C03_ERP_UNAVAILABLE", status = "UNKNOWN", operationId = input.OperationId });
        }
    }

    private string? Actor()
    {
        if (User.HasClaim(c => c.Type == "client_id" || (c.Type is "sub" or ClaimTypes.NameIdentifier &&
                c.Value.StartsWith("service:", StringComparison.Ordinal)))) return null;
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return id is { Length: > 0 and <= 100 } && !id.Any(char.IsControl) ? id : null;
    }
}
