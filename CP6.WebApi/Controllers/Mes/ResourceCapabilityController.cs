using CP6.Core.Services.Mes;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController, Authorize, Route("api/mes/resource-capabilities/owner-v1/{kind}"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResourceCapabilityController(ResourceCapabilityOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(string kind, CancellationToken ct) => Result(await owner.ScopeAsync(kind, ct));
    [HttpGet("{id:guid}/editor")]
    public async Task<IActionResult> Editor(string kind, Guid id, CancellationToken ct) => Result(await owner.EditorAsync(kind, id, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(string kind, Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(kind, id, version, ct));
    [HttpPost("commands"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Save(string kind, [FromBody] ResourceCapabilityCommandV1 input, CancellationToken ct) => Result(await owner.SaveAsync(kind, input, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(string kind, Guid id, CancellationToken ct) => Result(await owner.OperationAsync(kind, id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(string kind, Guid id, CancellationToken ct) => Result(await owner.OperationAsync(kind, id, true, ct));
}
