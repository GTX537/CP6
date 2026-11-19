using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Authorize, Route("api/dashboard/scoped-v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceMetricsControllerV1(WorkspaceMetricsV1 workspace) : ControllerBase
{
    [HttpGet("definitions")]
    public IActionResult Definitions() => Ok(WorkspaceMetricsV1.Definitions);

    [HttpGet("{key}")]
    public async Task<IActionResult> Card(string key, [FromQuery] string version = "1", CancellationToken ct = default)
    {
        var card = await workspace.ReadAsync(key, version, null, 1, ct);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpGet("{key}/drilldown")]
    public async Task<IActionResult> Drilldown(string key, [FromQuery] string version, [FromQuery] DateTimeOffset? asOfUtc,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (asOfUtc is null || page is < 1 or > 1000) return BadRequest(new { code = "WORKSPACE_QUERY_INVALID" });
        var card = await workspace.ReadAsync(key, version, asOfUtc, page, ct);
        if (card is null) return NotFound();
        if (card.Availability == "HIDDEN") return StatusCode(403, new { code = "WORKSPACE_TARGET_FORBIDDEN" });
        return Ok(card);
    }
}
