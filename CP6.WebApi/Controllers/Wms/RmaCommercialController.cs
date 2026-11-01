using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, Route("api/wms/rma/v1/commercial"), RmaFormalResult]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class RmaCommercialController(RmaFormalOwnerV1 owner) : ControllerBase
{
    [HttpGet("applications/{application:guid}/context")]
    public async Task<IActionResult> Context(Guid application,CancellationToken ct) => Ok(await owner.CommercialContextAsync(application,ct));
    [HttpPost("applications/{application:guid}/requests"),RequestSizeLimit(65536)]
    public async Task<IActionResult> RequestOutcome(Guid application,RmaCommercialRequestV1 input,CancellationToken ct)
    {
        if(!Guid.TryParse(Request.Headers["Idempotency-Key"],out var key) || key==Guid.Empty) throw new OrderPriceCorrectionException("VALIDATION_FAILED",400);
        return Ok(await owner.RequestCommercialAsync(application,key,input,ct));
    }
    [HttpPost("applications/{application:guid}/requests/{request:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid application,Guid request,CancellationToken ct) => Ok(await owner.ObserveCommercialAsync(application,request,true,ct));
    [HttpPost("applications/{application:guid}/requests/{request:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid application,Guid request,CancellationToken ct) => Ok(await owner.ObserveCommercialAsync(application,request,false,ct));
    [HttpPost("commands/query"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(RmaCommercialQueryV1 query,CancellationToken ct) => Ok(await owner.QueryCommercialCommandAsync(query,ct));
}
