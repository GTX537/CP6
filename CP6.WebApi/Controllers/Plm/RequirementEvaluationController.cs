using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Plm;
[ApiController,Authorize,Route("api/requirement-evaluations/owner-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class RequirementEvaluationController(RequirementEvaluationService owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct)=>Result(await owner.ScopeAsync(ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id,[FromQuery]string? version,CancellationToken ct)=>Result(await owner.ReadAsync(id,version,ct));
    [HttpGet("{id:guid}/controls/{revision}")]
    public async Task<IActionResult> History(Guid id,string revision,CancellationToken ct)=>Result(await owner.ReadControlAsync(id,revision,ct));
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)=>Result(await owner.RolesAsync(ct));
    [HttpPost("source")]
    public async Task<IActionResult> Source([FromBody]EngineeringSourceRefV1 reference,CancellationToken ct)=>Result(await owner.SourceAsync(reference,ct));
    [HttpPost("commands"),RequestSizeLimit(2097152)]
    public async Task<IActionResult> Command([FromBody]RequirementEvaluationCommandV1 command,CancellationToken ct)=>Result(await owner.CommandAsync(command,ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
