using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/warehouses/{warehouse:guid}/location-drafts-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LocationDraftController(LocationDraftOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]public async Task<IActionResult> Scope(Guid warehouse,CancellationToken ct)=>Result(await owner.ScopeAsync(warehouse,ct));
    [HttpGet("creation-context")]public async Task<IActionResult> Context(Guid warehouse,CancellationToken ct)=>Result(await owner.ContextAsync(warehouse,ct));
    [HttpGet]public async Task<IActionResult> List(Guid warehouse,[FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(warehouse,page,ct));
    [HttpGet("{id:guid}")]public async Task<IActionResult> Read(Guid warehouse,Guid id,CancellationToken ct)=>Result(await owner.ReadAsync(warehouse,id,ct));
    [HttpPost,RequestSizeLimit(16384)]public async Task<IActionResult> Create(Guid warehouse,LocationDraftCommandV1 command,CancellationToken ct)
    {var r=await owner.CreateAsync(warehouse,command,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid warehouse,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(warehouse,id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid warehouse,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(warehouse,id,true,ct));
}
