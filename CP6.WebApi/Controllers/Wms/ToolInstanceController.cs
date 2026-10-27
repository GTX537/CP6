using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/specials/v1/tools"),OrderOwnerResult]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ToolInstanceController(ToolInstanceOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]PlateMoldSearchQuery query,CancellationToken ct)=>Result(await owner.ListAsync(query,ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id,[FromQuery]string? version,CancellationToken ct)
    {var value=await owner.ReadAsync(id,version,ct);if(value.IsCurrent)Response.Headers.ETag=value.Etag;return Result(value);}
    [HttpPut("{id:guid}/profile"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Save(Guid id,[FromBody]ToolInstanceCommandV1 command,CancellationToken ct)
        =>Result(await owner.SaveAsync(id,Request.Headers.IfMatch.ToString(),command,ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
