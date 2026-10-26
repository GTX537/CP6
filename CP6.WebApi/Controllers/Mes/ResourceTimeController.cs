using CP6.Core.Services.Mes;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Mes;

[ApiController, Authorize, Route("api/mes/resource-sources/owner-v1/{kind}/{id:guid}/time-values"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResourceTimeController(ResourceTimeOwnerV1 owner) : ControllerBase
{
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Read(string kind, Guid id, [FromBody] ResourceTimeRequestV1 input, CancellationToken ct)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = await owner.ReadAsync(kind, id, input, ct) }); }
}
