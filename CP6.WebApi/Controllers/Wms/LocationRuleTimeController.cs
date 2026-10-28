using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, OrderOwnerResult, DocumentReviewAvailability, LocationRuleReviewAvailability]
[Route("api/location-rule/owner-v1/locations/{location:guid}/proposals/{id:guid}/time-evaluation")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LocationRuleTimeController(WarehouseClockOwnerV1 owner) : ControllerBase
{
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Evaluate(Guid location, Guid id, RuleTimeRequestV1 input, CancellationToken ct)
        => Ok(new { code = 0, data = await owner.EvaluateAsync(location, id, Request.Headers.IfMatch.ToString(), input, ct) });
}
