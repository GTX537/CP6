using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/products/identity-owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommercialProductUnitController(CommercialProductUnitOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("unit-bindings/scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string search, CancellationToken ct) => Result(await owner.ChoicesAsync(search, ct));
    [HttpGet("{id:guid}/unit-profile")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpGet("{id:guid}/unit-profile/editor")]
    public async Task<IActionResult> Editor(Guid id, CancellationToken ct) => Result(await owner.EditorAsync(id, ct));
    [HttpPost("unit-bindings/commands"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Command([FromBody] CommercialProductUnitCommandV1 command, CancellationToken ct) => Result(await owner.BindAsync(command, ct));
    [HttpGet("unit-bindings/operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("unit-bindings/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
