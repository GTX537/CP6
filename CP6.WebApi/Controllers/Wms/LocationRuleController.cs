using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/locations/{location:guid}/rules-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LocationRuleController(LocationRuleOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]public async Task<IActionResult> Scope(Guid location,CancellationToken ct)=>Result(await owner.ScopeAsync(location,ct));
    [HttpGet("context")]public async Task<IActionResult> Context(Guid location,CancellationToken ct)=>Result(await owner.ContextAsync(location,ct));
    [HttpGet]public async Task<IActionResult> List(Guid location,[FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(location,page,ct));
    [HttpGet("{id:guid}")]public async Task<IActionResult> Read(Guid location,Guid id,[FromQuery]string? version=null,CancellationToken ct=default)=>Result(await owner.ReadAsync(location,id,version,ct));
    [HttpPost,RequestSizeLimit(65536)]public async Task<IActionResult> Create(Guid location,LocationRuleCreateV1 command,CancellationToken ct)
    {var r=await owner.CreateAsync(location,command,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpPatch("{id:guid}"),RequestSizeLimit(65536)]public async Task<IActionResult> Patch(Guid location,Guid id,LocationRuleUpdateV1 command,CancellationToken ct)
    {var r=await owner.PatchAsync(location,id,Request.Headers.IfMatch.ToString(),command,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpPost("{id:guid}/withdraw"),RequestSizeLimit(8192)]public async Task<IActionResult> Withdraw(Guid location,Guid id,LocationRuleWithdrawV1 command,CancellationToken ct)
    {var r=await owner.WithdrawAsync(location,id,Request.Headers.IfMatch.ToString(),command,ct);Response.Headers.ETag=r.Etag;return Result(r);}
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid location,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(location,id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid location,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(location,id,true,ct));
}
