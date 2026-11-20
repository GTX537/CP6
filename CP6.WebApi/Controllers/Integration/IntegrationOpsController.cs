using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Integration;

[ApiController]
[Authorize]
[Route("api/integration-ops/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntegrationOpsController(ITenantContext tenant, IntegrationOpsDirectoryV1? directory = null,
    ErpIntegrationOpsV1? erp = null) : ControllerBase
{
    [HttpGet("failures")]
    public Task<IActionResult> List([FromQuery] string? owner, [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? untilUtc, [FromQuery] int? size, [FromQuery] string? cursor, CancellationToken ct) =>
        Execute(async () => Ok(await directory!.ReadAsync(tenant.CurrentTenantId, owner, fromUtc, untilUtc, size, cursor, ct)));

    [HttpGet("failures/{failureRef}")]
    public Task<IActionResult> Detail(string failureRef, CancellationToken ct) =>
        Execute(async () => Ok(await erp!.DetailAsync(tenant.CurrentTenantId, failureRef, ct)));

    [HttpPost("failures/{failureRef}/eligibility")]
    public Task<IActionResult> Eligibility(string failureRef, CancellationToken ct) =>
        Execute(async () => Ok(await erp!.EligibilityAsync(tenant.CurrentTenantId, failureRef, ct)));

    [HttpPost("failures/{failureRef}/replays")]
    [RequestSizeLimit(32_768)]
    public async Task<IActionResult> Schedule(string failureRef, CancellationToken ct)
    {
        IntegrationReplayRequestV1? input;
        try
        {
            input = Request.HasJsonContentType()
                ? await Request.ReadFromJsonAsync<IntegrationReplayRequestV1>(cancellationToken: ct) : null;
        }
        catch (JsonException) { input = null; }
        if (input is null) return BadRequest(new { code = "REPLAY_INPUT_INVALID" });
        return await Execute(async () =>
        {
            var admission = await erp!.ScheduleAsync(tenant.CurrentTenantId, failureRef, input, ct);
            if (!admission.New) return Ok(admission.Receipt);
            if (admission.Receipt.Status == "Blocked") return Conflict(admission.Receipt);
            return AcceptedAtAction(nameof(Replay), new { id = input.ReplayOperationId }, admission.Receipt);
        }, input.ReplayOperationId);
    }

    [HttpGet("replays/{id:guid}")]
    public Task<IActionResult> Replay(Guid id, CancellationToken ct) =>
        Execute(async () => Ok(await erp!.ReadReplayAsync(tenant.CurrentTenantId, id, ct)));

    [HttpGet("replays/{id:guid}/audit")]
    public Task<IActionResult> Audit(Guid id, CancellationToken ct) =>
        Execute(async () => Ok(await erp!.ReadAuditAsync(tenant.CurrentTenantId, id, ct)));

    [HttpGet("owner-results/{requestId:guid}")]
    public Task<IActionResult> OwnerResult(Guid requestId, CancellationToken ct) =>
        Execute(async () => Ok(await erp!.ReadOwnerResultAsync(tenant.CurrentTenantId, requestId, ct)));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> execute, Guid? operation = null)
    {
        if (directory is null || erp is null) return NotFound();
        try { return await execute(); }
        catch (IntegrationOpsExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
        catch (ErpCommerceException error) when (error.Code.StartsWith("C03_", StringComparison.Ordinal))
        {
            return Conflict(new { code = "NATIVE_PRECONDITION_FAILED" });
        }
        catch (Exception error) when (IntegrationOpsContractV1.IsUnavailable(error))
        {
            if (operation is null) return StatusCode(503, new { code = "SOURCE_UNAVAILABLE" });
            Response.Headers.RetryAfter = "2";
            return StatusCode(503, new { code = "REPLAY_OUTCOME_UNKNOWN", status = "UNKNOWN", replayOperationId = operation });
        }
    }
}
