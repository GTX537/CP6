using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/products/identity-owner-v1")]
[OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommercialProductIdentityController(CommercialProductIdentityReader reader) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await reader.ReadAsync(id, version, ct);
        return Ok(new { code = 0, data = result });
    }
}
