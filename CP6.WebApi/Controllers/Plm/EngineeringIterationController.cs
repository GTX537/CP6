using CP6.WebApi.Controllers.Erp;
using CP6.Core.Services.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;

[ApiController, Authorize, Route("api/engineering-iterations/owner-v1"), OrderOwnerResult, RequirementSetReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EngineeringIterationController(EngineeringIterationService owner) : ControllerBase
{
    private IActionResult Result<T>(T value)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("creation-context")]
    public async Task<IActionResult> CreationContext(CancellationToken ct) => Result(await owner.CreationContextAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? kind, [FromQuery] Guid? projectId, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await owner.ListAsync(kind, projectId, search, page, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpGet("members")]
    public async Task<IActionResult> Members([FromQuery] string? search,CancellationToken ct)=>Result(await owner.MembersAsync(search,ct));
    [HttpGet("sites")]
    public async Task<IActionResult> Sites([FromQuery] string search="",CancellationToken ct=default)=>Result(await owner.SitesAsync(search,ct));
    [HttpPost("commands"), RequestSizeLimit(2097152)]
    public async Task<IActionResult> Command([FromBody] EngineeringIterationCommandV1 command, CancellationToken ct) => Result(await owner.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
