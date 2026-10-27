using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;
[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolAssociationController(ToolAssociationOwnerV1 owner,
    ToolAssociationEngineeringOwnerV1 engineering, ToolAssociationPhysicalOwnerV1 physical) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/engineering-candidates")]
    public async Task<IActionResult> Candidates(Guid id, [FromQuery] string engineeringNo, [FromQuery] int revision, CancellationToken ct)
        => Result(await owner.CandidatesAsync(id, engineeringNo, revision, ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/engineering-links")]
    public async Task<IActionResult> List(Guid id, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await owner.ListAsync(id, page, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/engineering-links"), RequestSizeLimit(32768)]
    public async Task<IActionResult> Propose(Guid id, [FromBody] ToolAssociationProposalCommandV1 command, CancellationToken ct)
        => Result(await owner.ProposeAsync(id, Request.Headers.IfMatch.ToString(), command, ct));
    [HttpGet("api/tool-associations/owner-v1/{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct)
    { var value = await owner.ReadAsync(id, version, ct); if (version == null) Response.Headers.ETag = ToolAssociationOwnerV1.Etag(value); return Result(value); }
    [HttpPost("api/tool-specifications/owner-v1/instance-association-decisions"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Engineering([FromBody] ToolAssociationDecisionCommandV1 command, CancellationToken ct)
        => Result(await engineering.DecideAsync(Request.Headers.IfMatch.ToString(), command, ct));
    [HttpPost("api/wms/specials/v1/tools/engineering-link-decisions"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Physical([FromBody] ToolAssociationDecisionCommandV1 command, CancellationToken ct)
        => Result(await physical.DecideAsync(Request.Headers.IfMatch.ToString(), command, ct));
    [HttpGet("api/tool-associations/owner-v1/operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("api/tool-associations/owner-v1/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
