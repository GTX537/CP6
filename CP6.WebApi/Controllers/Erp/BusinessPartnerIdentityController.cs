using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/business-partners/identity-owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessPartnerIdentityController(BusinessPartnerIdentityOwnerV1 owner) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { code = 0, data = await owner.ReadAsync(id, version, ct) });
    }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BusinessPartnerIdentityQueryV1 query, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { code = 0, data = await owner.ListAsync(query, ct) });
    }
}
