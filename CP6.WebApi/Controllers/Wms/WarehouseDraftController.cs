using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, Route("api/wms/master/drafts-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class WarehouseDraftController(WarehouseDraftOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet("creation-context")]public async Task<IActionResult> Context(CancellationToken ct)=>Result(await owner.ContextAsync(ct));
    [HttpGet("warehouses")]public async Task<IActionResult> List([FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(page,ct));
    [HttpGet("warehouses/{id:guid}")]public async Task<IActionResult> Read(Guid id,CancellationToken ct)=>Result(await owner.ReadAsync(id,ct));
    [HttpPost("warehouses"),RequestSizeLimit(16384)]public async Task<IActionResult> Create(WarehouseDraftCommandV1 command,CancellationToken ct)
    {var r=await owner.CreateAsync(command,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
