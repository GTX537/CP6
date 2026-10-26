using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;
[ApiController, Authorize, Route("api/engineering-mappings/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EngineeringProductMappingController(EngineeringProductMappingService service) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid engineeringId, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await service.ListAsync(engineeringId, page, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await service.ReadAsync(id, version, ct));
    [HttpGet("business-choices")]
    public async Task<IActionResult> Business([FromQuery] string search, CancellationToken ct) => Result(await service.BusinessChoicesAsync(search, ct));
    [HttpGet("site-choices")]
    public async Task<IActionResult> Sites([FromQuery] string search = "", CancellationToken ct = default) => Result(await service.SiteChoicesAsync(search, ct));
    [HttpPost("commands"), RequestSizeLimit(262144)]
    public async Task<IActionResult> Command([FromBody] EngineeringMappingCommandV1 command, CancellationToken ct) => Result(await service.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await service.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await service.OperationAsync(id, true, ct));
}
