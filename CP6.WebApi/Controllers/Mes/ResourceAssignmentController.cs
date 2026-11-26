using CP6.Core.Services.Mes;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController, Authorize, Route("api/mes/resource-assignments/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResourceAssignmentController(ResourceAssignmentOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { code = 0, data = value });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct)
        => Result(await owner.ReadAsync(id, version, ct));

    [HttpPost("commands"), RequestSizeLimit(1048576)]
    public async Task<IActionResult> Save([FromBody] ResourceAssignmentCommandV1 input, CancellationToken ct)
        => Result(await owner.SaveAsync(input, ct));

    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct)
        => Result(await owner.OperationAsync(id, false, ct));

    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
        => Result(await owner.OperationAsync(id, true, ct));
}
