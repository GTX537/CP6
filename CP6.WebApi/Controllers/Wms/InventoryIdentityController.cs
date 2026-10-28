using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, Route("api/inventory-identities/owner-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InventoryIdentityController(InventoryIdentityOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")] public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("creation-context")] public async Task<IActionResult> Creation(CancellationToken ct) => Result(await owner.CreationContextAsync(ct));
    [HttpGet] public async Task<IActionResult> List([FromQuery] string? kind, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default)
        => Result(await owner.ListAsync(kind, search, page, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpPost("commands"), RequestSizeLimit(65536)] public async Task<IActionResult> Command(InventoryIdentityCommandV1 command, CancellationToken ct) => Result(await owner.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
    [HttpPost("catalogue"), RequestSizeLimit(4096)] public async Task<IActionResult> Catalogue(InventoryIdentityRefV1 reference, CancellationToken ct) => Result(await owner.CatalogueAsync(reference, ct));
}
