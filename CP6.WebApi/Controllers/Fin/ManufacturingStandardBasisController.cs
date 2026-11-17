using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/fin/cost/owner-v1/standard-bases")]
public sealed class ManufacturingStandardBasisController(ManufacturingStandardBasisOwnerV1 basis) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpPost]
    public async Task<ActionResult<StandardBasisStatusV1>> Submit([FromHeader(Name = Context)] Guid context,
        [FromBody] StandardBasisSubmissionV1 command, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await basis.SubmitAsync(context, command, ct));

    [HttpPost("{id:guid}/review")]
    public Task<StandardBasisStatusV1> Review([FromHeader(Name = Context)] Guid context, Guid id,
        [FromBody] StandardBasisReviewV1 command, CancellationToken ct)
        => basis.ReviewAsync(context, id, command, ct);

    [HttpPost("{id:guid}/publish")]
    public Task<StandardBasisStatusV1> Publish([FromHeader(Name = Context)] Guid context, Guid id,
        [FromBody] StandardBasisReleaseV1 command, CancellationToken ct)
        => basis.PublishAsync(context, id, command, ct);

    [HttpPost("{id:guid}/revoke")]
    public Task<StandardBasisStatusV1> Revoke([FromHeader(Name = Context)] Guid context, Guid id,
        [FromBody] StandardBasisReviewV1 command, CancellationToken ct)
        => basis.RevokeAsync(context, id, command, ct);

    [HttpGet("{id:guid}")]
    public Task<object> Read([FromHeader(Name = Context)] Guid context, Guid id, CancellationToken ct)
        => basis.ReadAsync(context, id, ct);

    [HttpGet("comparisons/{workOrder:guid}/{version:long}")]
    public Task<object> ReadComparison([FromHeader(Name = Context)] Guid context, Guid workOrder, long version, CancellationToken ct)
        => basis.ReadComparisonAsync(context, workOrder, version, ct);
}
