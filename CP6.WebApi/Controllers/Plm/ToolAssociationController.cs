using CP6.Core.Services.Erp;
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
    public async Task<IActionResult> Candidates(Guid id, [FromQuery] string engineeringNo, [FromQuery] int? revision,
        [FromQuery] int? pageSize, [FromQuery] Guid? after, CancellationToken ct)
    {
        var query = Request.Query;
        if (revision == null || query["revision"].Count != 1 || query["engineeringNo"].Count != 1
            || query.ContainsKey("pageSize") && (pageSize == null || query["pageSize"].Count != 1)
            || query.ContainsKey("after") && (after == null || pageSize == null || query["after"].Count != 1))
            throw new OrderPriceCorrectionException("TOOL_CANDIDATE_QUERY_INVALID", 400);
        if (pageSize == null)
            return Result(await owner.CandidatesAsync(id, engineeringNo, revision.Value, ct));

        var page = await owner.CandidatePageAsync(id, engineeringNo, revision.Value, pageSize.Value, after, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { code = 0, data = page.Items, nextCursor = page.NextCursor });
    }
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
