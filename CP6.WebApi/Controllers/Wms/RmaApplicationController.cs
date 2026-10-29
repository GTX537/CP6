using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, Route("api/wms/rma/native-applications-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RmaApplicationController(RmaApplicationOwnerV1 owner) : ControllerBase
{
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Ok(new { code = 0, data = await owner.ScopeAsync(ct) });
    [HttpGet("sources")]
    public async Task<IActionResult> Sources([FromQuery] string? keyword, [FromQuery] int page = 1, CancellationToken ct = default) =>
        Ok(new { code = 0, data = await owner.SourcesAsync(keyword, page, ct) });
    [HttpGet("sources/{id:guid}")]
    public async Task<IActionResult> Source(Guid id, CancellationToken ct) => Ok(new { code = 0, data = await owner.SourceAsync(id, ct) });
    [HttpGet("sources/{id:guid}/fulfilment")]
    public async Task<IActionResult> Fulfilment(Guid id, CancellationToken ct) => Ok(new { code = 0, data = await owner.FulfilmentAsync(id, ct) });
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default) =>
        Ok(new { code = 0, data = await owner.ListAsync(page, ct) });
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] int? version, CancellationToken ct)
    {
        var value = await owner.ReadAsync(id, version, ct); Response.Headers.ETag = value.Etag;
        return Ok(new { code = 0, data = value });
    }
    [HttpPost, RequestSizeLimit(65536)]
    public async Task<IActionResult> Create(RmaApplicationCreateV1 command, CancellationToken ct)
    {
        var value = await owner.CreateAsync(command, ct); Response.Headers.ETag = value.Etag;
        return StatusCode(201, new { code = 0, data = value });
    }
    [HttpPost("{id:guid}/submit"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Submit(Guid id, RmaApplicationSubmitV1 command, CancellationToken ct)
    {
        var value = await owner.SubmitAsync(id, command, Request.Headers.IfMatch.ToString(), ct); Response.Headers.ETag = value.Etag;
        return Ok(new { code = 0, data = value });
    }
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) =>
        Ok(new { code = 0, data = await owner.OperationAsync(id, false, ct) });
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        Ok(new { code = 0, data = await owner.OperationAsync(id, true, ct) });
}
