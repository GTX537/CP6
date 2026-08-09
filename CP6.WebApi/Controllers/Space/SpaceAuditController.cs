using CP6.Core.Auth;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space/audit")]
[Authorize]
public sealed class SpaceAuditController : ControllerBase
{
    private readonly ISpaceAuditQueryService _query;
    private readonly ISpaceAuditWriter _writer;
    private readonly SpaceObservabilityOptions _options;

    public SpaceAuditController(
        ISpaceAuditQueryService query,
        ISpaceAuditWriter writer,
        IOptions<SpaceObservabilityOptions> options)
    {
        _query = query;
        _writer = writer;
        _options = options.Value;
    }

    [HttpGet("events")]
    [RequirePermission("space-audit", "read")]
    public async Task<IActionResult> Query(
        [FromQuery] SpaceAuditQueryDto query,
        CancellationToken ct)
    {
        if (!_options.AuditQueryEnabled)
            return Disabled();

        var data = await _query.QueryAsync(query, ct);
        await TryAuditReadAsync(
            new SpaceAuditEventInput(
                "space.audit.read",
                "SpaceAuditEvent",
                query.CorrelationId?.ToString(),
                SpaceAuditOutcome.Succeeded,
                Evidence: new SpaceAuditEvidence(
                    PermissionCode: "space-audit:read",
                    AuthorizationResult: "Allowed",
                    ItemCount: data.Items.Count),
                ClientType: "Web"),
            ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    [HttpGet("timeline/{correlationId:guid}")]
    [RequirePermission("space-audit", "read")]
    public async Task<IActionResult> Timeline(
        Guid correlationId,
        CancellationToken ct)
    {
        if (!_options.AuditQueryEnabled)
            return Disabled();

        var data = await _query.GetTimelineAsync(correlationId, ct);
        await TryAuditReadAsync(
            new SpaceAuditEventInput(
                "space.audit.timeline.read",
                "Correlation",
                correlationId.ToString(),
                SpaceAuditOutcome.Succeeded,
                Evidence: new SpaceAuditEvidence(
                    PermissionCode: "space-audit:read",
                    AuthorizationResult: "Allowed",
                    ItemCount: data.Count),
                ClientType: "Web"),
            ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    private async Task TryAuditReadAsync(
        SpaceAuditEventInput input,
        CancellationToken ct)
    {
        try
        {
            await _writer.TryAppendAsync(input, ct);
        }
        catch
        {
            // Read auditing is fail-open by design. The authorized result is
            // already a safe DTO and must remain available if audit storage
            // is unavailable. The production writer emits its own safe log.
        }
    }

    private static ObjectResult Disabled() =>
        new(new
        {
            code = 404,
            message = "SPACE_AUDIT_QUERY_DISABLED",
            data = (object?)null,
        })
        {
            StatusCode = StatusCodes.Status404NotFound,
        };
}
