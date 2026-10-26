using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/uom-profiles/owner-v1/time-bases"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UomTimeBasisController(UomTimeBasisOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("{id:guid}/editor")]
    public async Task<IActionResult> Editor(Guid id, CancellationToken ct) => Result(await owner.EditorAsync(id, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpPost("commands"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Command([FromBody] UomTimeBasisCommandV1 input, CancellationToken ct) => Result(await owner.SaveAsync(input, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
