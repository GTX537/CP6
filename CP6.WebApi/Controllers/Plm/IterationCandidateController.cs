using CP6.WebApi.Controllers.Erp;
using CP6.Core.Services.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Plm;
[ApiController,Authorize,Route("api/iteration-candidates/owner-v1"),OrderOwnerResult,RequirementSetReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class IterationCandidateController(IterationCandidateService owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("{iterationId:guid}")]
    public async Task<IActionResult> List(Guid iterationId,[FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Result(await owner.ListAsync(iterationId,search,page,ct));
    [HttpGet("{iterationId:guid}/{id:guid}")]
    public async Task<IActionResult> Read(Guid iterationId,Guid id,[FromQuery]string? version,CancellationToken ct)=>Result(await owner.ReadAsync(iterationId,id,version,ct));
    public sealed record Comparison(EngineeringSourceRefV1 Before,EngineeringSourceRefV1 After);
    [HttpPost("{iterationId:guid}/compare")]
    public async Task<IActionResult> Compare(Guid iterationId,[FromBody]Comparison value,CancellationToken ct)=>Result(await owner.CompareAsync(iterationId,value.Before,value.After,ct));
    [HttpPost("commands"),RequestSizeLimit(2097152)]
    public async Task<IActionResult> Command([FromBody]IterationCandidateCommandV1 command,CancellationToken ct)=>Result(await owner.CommandAsync(command,ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
