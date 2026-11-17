using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route("api/fin/cost/owner-v1")]
public sealed class ManufacturingCostSourceController(
    ManufacturingQualificationOwnerV1 qualifications, ManufacturingCostCenterOwnerV1 centers) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpPost("qualifications")]
    public Task<ManufacturingQualificationDecisionV1> DecideQualification([FromHeader(Name = Context)] Guid context,
        [FromBody] ManufacturingQualificationCommandV1 command, CancellationToken ct)
        => qualifications.DecideAsync(context, command, ct);

    [HttpPost("center-versions")]
    public async Task<ActionResult<ManufacturingCenterEditionV1>> SubmitCenter([FromHeader(Name = Context)] Guid context,
        [FromBody] ManufacturingCenterSubmissionV1 command, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await centers.SubmitAsync(context, command, ct));

    [HttpPost("center-versions/{id:guid}/review")]
    public Task<ManufacturingCenterEditionV1> ReviewCenter([FromHeader(Name = Context)] Guid context,
        Guid id, [FromBody] ManufacturingCenterReviewV1 command, CancellationToken ct)
        => centers.ReviewAsync(context, id, command, ct);

    [HttpPost("center-versions/{id:guid}/publish")]
    public Task<ManufacturingCenterEditionV1> PublishCenter([FromHeader(Name = Context)] Guid context,
        Guid id, [FromBody] ManufacturingCenterPublishV1 command, CancellationToken ct)
        => centers.PublishAsync(context, id, command, ct);
}
