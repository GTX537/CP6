using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Erp;
[ApiController,Authorize,Route("api/plate-molds/write-owner-v1"),OrderOwnerResult]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PlateMoldWriteOwnerController(PlateMoldWriteOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
