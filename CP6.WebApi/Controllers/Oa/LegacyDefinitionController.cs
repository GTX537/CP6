using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2/legacy-definitions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LegacyDefinitionController(FlowAuthoringOwnerV2 flows, FormAuthoringOwnerV2 forms) : ControllerBase
{
    [HttpGet("{kind}")]
    [HttpGet("{kind}/{key}")]
    public async Task<IActionResult> Read(string kind, string? key = null, [FromQuery] Guid? versionId = null,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        try
        {
            var result = kind switch
            {
                "FLOW" => await flows.LegacyAsync(key, versionId, page, ct),
                "FORM" => await forms.LegacyAsync(key, versionId, page, ct),
                _ => throw new ScopeAuthorizationExceptionV1("DEFINITION_KIND_INVALID", 400)
            };
            return Ok(new { code = 0, data = result });
        }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message, retryMode = "NONE" }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE", retryMode = "NONE" }); }
    }
}
