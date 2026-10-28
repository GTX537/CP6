using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Plm;
[ApiController,Authorize,Route("api/plm-engineering-baselines/owner-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class EngineeringBaselinesController(EngineeringBaselineOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet]public async Task<IActionResult> List([FromQuery]Guid? revision,[FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(revision,page,ct));
    [HttpGet("{id:guid}")]public async Task<IActionResult> Read(Guid id,CancellationToken ct)=>Result(await owner.ReadAsync(id,ct));
    [HttpGet("{id:guid}/where-used")]public async Task<IActionResult> Used(Guid id,CancellationToken ct)=>Result(await owner.WhereUsedAsync(id,ct));
    [HttpGet("diff")]public async Task<IActionResult> Diff([FromQuery]Guid before,[FromQuery]Guid after,CancellationToken ct)=>Result(await owner.DiffAsync(before,after,ct));
    [HttpGet("choices/{iteration:guid}")]public async Task<IActionResult> Choices(Guid iteration,CancellationToken ct)=>Result(await owner.ChoicesAsync(iteration,ct));
    [HttpPost("freeze"),RequestSizeLimit(65536)]public async Task<IActionResult> Freeze([FromBody]EngineeringFreezeCommandV1 c,CancellationToken ct)=>Result(await owner.FreezeAsync(c,ct));
    [HttpPost("cancel"),RequestSizeLimit(65536)]public async Task<IActionResult> Cancel([FromBody]EngineeringFreezeCancelV1 c,CancellationToken ct)=>Result(await owner.CancelAsync(c,ct));
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,ct));
    [HttpPost("successor"),RequestSizeLimit(65536)]public async Task<IActionResult> Successor([FromBody]EngineeringBaselineSuccessorV1 c,CancellationToken ct)=>Result(await owner.SuccessorAsync(c,ct));
    [HttpGet("successor-operations/{id:guid}")]public async Task<IActionResult> SuccessorOperation(Guid id,CancellationToken ct)=>Result(await owner.SuccessorOperationAsync(id,false,ct));
    [HttpPost("successor-operations/{id:guid}/close")]public async Task<IActionResult> CloseSuccessor(Guid id,CancellationToken ct)=>Result(await owner.SuccessorOperationAsync(id,true,ct));
}
