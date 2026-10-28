using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Plm;
[ApiController, Authorize, Route("api/plm-engineering-output-relations/owner-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class EngineeringOutputRelationsController(EngineeringOutputRelationsOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl="no-store"; return Ok(new{code=0,data=value}); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(search,page,ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id,[FromQuery]string? version,CancellationToken ct)=>Result(await owner.ReadAsync(id,version,ct));
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)=>Result(await owner.RolesAsync(ct));
    [HttpGet("{id:guid}/diff")]
    public async Task<IActionResult> Diff(Guid id,[FromQuery]string before,[FromQuery]string after,CancellationToken ct)=>Result(await owner.DiffAsync(id,before,after,ct));
    [HttpPost("commands"),RequestSizeLimit(33554432)]
    public async Task<IActionResult> Command([FromBody]EngineeringOutputRelationsCommandV1 command,CancellationToken ct)=>Result(await owner.CommandAsync(command,ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
