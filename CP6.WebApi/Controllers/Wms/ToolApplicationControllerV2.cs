using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolApplicationControllerV2(ToolApplicationOwnerV2 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) => Ok(new { code = 0, data = value });
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/application-context")]
    public async Task<IActionResult> Context(Guid id, CancellationToken ct)
    { var value = await owner.ContextAsync(id, ct); Response.Headers.ETag = value.Etag; return Result(value); }
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/approval-applications"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ToolApplicationCommandV2 input, CancellationToken ct)
        => Result(await owner.ApplyAsync(id, Request.Headers.IfMatch.ToString(), input, ct));
    [HttpGet("api/wms/tool-application/v2/operations/{id:guid}")]
    public async Task<IActionResult> Original(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("api/wms/tool-application/v2/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
