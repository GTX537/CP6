using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;
[ApiController, Authorize, Route("api/controlled-document/owner-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ControlledDocumentController(ControlledDocumentOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? revisionId, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default)
        => Result(await owner.ListAsync(revisionId, search, page, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpGet("{id:guid}/diff")]
    public async Task<IActionResult> Diff(Guid id, [FromQuery] string before, [FromQuery] string after, CancellationToken ct)
        => Result(await owner.DiffAsync(id, before, after, ct));
    [HttpGet("{id:guid}/content/{occurrence:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid occurrence, [FromQuery] string version, CancellationToken ct)
    {
        var value=await owner.DownloadAsync(id,version,occurrence,ct);
        Response.Headers.CacheControl="no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers["X-Content-SHA256"]=value.ContentRef.Digest;
        return File(value.Bytes,"application/octet-stream",value.FileName,enableRangeProcessing:false);
    }
    [HttpPost("commands"), RequestSizeLimit(8388608)]
    public async Task<IActionResult> Command([FromBody] ControlledDocumentCommandV1 command, CancellationToken ct) => Result(await owner.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
