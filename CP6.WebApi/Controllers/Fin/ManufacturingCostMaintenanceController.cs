using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult, Route("api/fin/cost/owner-v1")]
public sealed class ManufacturingCostMaintenanceController(ManufacturingCostMaintenanceV1 owner) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpGet("rate-maintenance")]
    public Task<object> Rates([FromHeader(Name = Context)] Guid context, [FromQuery] string? search,
        [FromQuery] DateOnly? businessDate, CancellationToken ct) => owner.RatesAsync(context, search, businessDate, ct);

    [HttpGet("work-order-maintenance")]
    public Task<object> WorkOrder([FromHeader(Name = Context)] Guid context, [FromQuery] string workOrderNo,
        CancellationToken ct) => owner.WorkOrderAsync(context, workOrderNo, ct);
}
