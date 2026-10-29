using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/erp/sales-return-sources/v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SalesReturnSourceController(SalesReturnSourceReaderV1 owner) : ControllerBase
{
    [HttpGet("{outboundId:guid}/{shipmentLineId:guid}")]
    public async Task<IActionResult> Read(Guid outboundId, Guid shipmentLineId, CancellationToken ct) =>
        Ok(new { code = 0, data = await owner.ReadAsync(outboundId, shipmentLineId, ct) });
}
