using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/business-partners/finance-references-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessPartnerFinanceReferenceController(BusinessPartnerFinanceReferenceReader reader) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string field, [FromHeader(Name = "X-Finance-Context")] Guid? financeContext,
        [FromQuery] string? bpRevision, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if ((Request.Query.TryGetValue("bpRevision", out var revisions) && (revisions.Count != 1 || bpRevision is null))
            || Request.Headers["X-Finance-Context"].Count > 1)
            return BadRequest(new { code = 400, message = "BP_FINANCE_REFERENCE_INPUT_INVALID" });
        return Ok(new { code = 0, data = await reader.ReadAsync(id, field, financeContext, bpRevision, ct) });
    }
}
