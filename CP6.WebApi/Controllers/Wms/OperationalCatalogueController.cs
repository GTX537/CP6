using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, Route("api/operational-catalogue/v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationalCatalogueController(OperationalCatalogueEvidenceAdapter catalogue) : ControllerBase
{
    [HttpPost("unit-conversion"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Conversion(OperationalUnitConversionQueryV1 input, CancellationToken ct) =>
        Ok(new { code = 0, data = await catalogue.ReadConversionAsync(input, ct) });

    [HttpPost("warehouses/{warehouse:guid}/capacity-metric"), RequestSizeLimit(16384)]
    public async Task<IActionResult> CapacityMetric(Guid warehouse, OperationalCapacityMetricQueryV1 input, CancellationToken ct) =>
        Ok(new { code = 0, data = await catalogue.ReadCapacityMetricAsync(warehouse, input, ct) });
}
