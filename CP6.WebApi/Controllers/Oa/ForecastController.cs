using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>旧发起前预演路由。提供准确发布版本后沿现行只读 Owner 解释。</summary>
[ApiController, Route("api/oa/forecast"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ForecastController(FlowAuthoringOwnerV2 owner) : LocalizedControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpPost("preview"), RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Preview([FromBody] PreviewReq input, CancellationToken ct)
    {
        if (input.PublishedVersionId is not Guid version || version == Guid.Empty)
            return UnprocessableEntity(new { code = "FORECAST_PIN_REQUIRED", legacyCode = "E-WF-006" });

        try
        {
            var forecast = new WorkflowForecastInputV1(version, null, null, input.VarsJson ?? "{}", null);
            var result = await owner.ForecastAsync(input.FlowKey, JsonSerializer.SerializeToUtf8Bytes(forecast, Json), ct);
            return Ok(new { code = 0, data = result });
        }
        catch (ScopeAuthorizationExceptionV1 e)
        { return StatusCode(e.Status, new { code = e.Message, legacyCode = "E-WF-006" }); }
        catch (Exception e) when (e is CurrentPermissionUnavailableException or Npgsql.NpgsqlException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE" }); }
    }

    public record PreviewReq(string FlowKey, string? VarsJson, Guid? PublishedVersionId = null);
}
