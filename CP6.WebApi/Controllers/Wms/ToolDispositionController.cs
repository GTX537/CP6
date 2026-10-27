using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolDispositionController(ToolDispositionOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/disposition-context")]
    public async Task<IActionResult> Context(Guid id, CancellationToken ct)
    { var value = await owner.ContextAsync(id, ct); Response.Headers.ETag = value.Etag; return Result(value); }
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/disposition-requests"), RequestSizeLimit(8192)]
    public async Task<IActionResult> RequestDisposition(Guid id, [FromBody] ToolDispositionRequestV1 command, CancellationToken ct)
        => Result(await owner.RequestAsync(id, Request.Headers.IfMatch.ToString(), command, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/new-use-blocks"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Block(Guid id, [FromBody] ToolDispositionBlockV1 command, CancellationToken ct)
        => Result(await owner.BlockAsync(id, Request.Headers.IfMatch.ToString(), command, ct));
    [HttpGet("api/wms/tool-disposition/v1/operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("api/wms/tool-disposition/v1/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
