using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/bridge-health")]
[Authorize]
public class BridgeHealthController : ControllerBase
{
    private readonly IBridgeHealthService _service;

    public BridgeHealthController(IBridgeHealthService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>GET /api/bridge-health/metrics</summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken ct)
    {
        var metrics = await _service.GetMetricsAsync(ct: ct);
        return Ok(new { code = 0, message = "OK", data = metrics });
    }

    /// <summary>POST /api/bridge-health/compensate/{eventId}</summary>
    [HttpPost("compensate/{eventId:guid}")]
    public async Task<IActionResult> Compensate(Guid eventId, CancellationToken ct)
    {
        var ok = await _service.CompensateAsync(eventId, CurrentUser, ct);
        if (!ok)
        {
            return NotFound(new { code = 404, message = "Dead letter event was not found.", data = (object?)null });
        }

        return Ok(new
        {
            code = 0,
            message = "OK",
            data = new { eventId, status = IntegrationEventStatus.Compensated },
        });
    }
}
