using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Erp;
[ApiController, Authorize, Route("api/plate-molds/order-link-owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class ToolOrderLinkController(ToolOrderLinkOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new {code=0,data=value});}
    [HttpGet("scope")] public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Read(Guid id,CancellationToken ct)=>Result(await owner.ReadAsync(id,ct));
    [HttpPost("commands")] public async Task<IActionResult> Create(ToolOrderLinkCommandV1 command,CancellationToken ct)=>Result(await owner.CreateOrLinkAsync(command,ct));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")] public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
