using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/locations/{location:guid}/local-controls-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LocationControlController(LocationControlOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet]public async Task<IActionResult> Read(Guid location,int page=1,CancellationToken ct=default)
    {var r=await owner.ReadAsync(location,page,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpPost("blocks"),RequestSizeLimit(8192)]public async Task<IActionResult> Block(Guid location,LocationBlockCommandV1 c,CancellationToken ct)
        =>Result(await owner.BlockAsync(location,Request.Headers.IfMatch,c,ct));
    [HttpPost("release-decisions"),RequestSizeLimit(8192)]public async Task<IActionResult> Decide(Guid location,LocationReleaseDecisionCommandV1 c,CancellationToken ct)
        =>Result(await owner.DecideReleaseAsync(location,Request.Headers.IfMatch,c,ct));
    [HttpPost("releases"),RequestSizeLimit(8192)]public async Task<IActionResult> Release(Guid location,LocationBlockReleaseCommandV1 c,CancellationToken ct)
        =>Result(await owner.ReleaseAsync(location,Request.Headers.IfMatch,c,ct));
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid location,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(location,id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid location,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(location,id,true,ct));
}
