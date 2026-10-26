using CP6.Core.Services.Mes;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController, Authorize, Route("api/mes/resource-sources/owner-v1/{kind}"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResourceSourceController(ResourceSourceOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(string kind, CancellationToken ct) => Result(await owner.ScopeAsync(kind, ct));
    [HttpGet]
    public async Task<IActionResult> List(string kind, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Result(await owner.ListAsync(kind, search, page, pageSize, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(string kind, Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(kind, id, version, ct));
    [HttpPost("commands"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Command(string kind, [FromBody] ResourceSourceCommandV1 input, CancellationToken ct) => Result(await owner.SaveAsync(kind, input, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(string kind, Guid id, CancellationToken ct) => Result(await owner.OperationAsync(kind, id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(string kind, Guid id, CancellationToken ct) => Result(await owner.OperationAsync(kind, id, true, ct));
}
