using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Plm;

[ApiController, Authorize, Route("api/engineering-source-checks/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EngineeringSourceCheckController(EngineeringSourceCheckService checks) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")] public async Task<IActionResult> Scope(CancellationToken ct) => Result(await checks.ScopeAsync(ct));
    [HttpPost("commands"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Check(EngineeringSourceCheckCommandV1 command, CancellationToken ct) => Result(await checks.CheckAsync(command, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Read(Guid id, CancellationToken ct) => Result(await checks.ReadAsync(id, ct));
    [HttpGet("revisions/{id:guid}")]
    public async Task<IActionResult> History(Guid id, int page = 1, CancellationToken ct = default) => Result(await checks.HistoryAsync(id, page, ct));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await checks.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await checks.OperationAsync(id, true, ct));
}
