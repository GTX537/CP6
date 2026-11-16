using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManufacturingRateVersionController(ManufacturingRateOwnerV1 owner) : ControllerBase
{
    private const string FinanceContext = "X-Finance-Context";

    [HttpGet("api/fin/cost/v1/options")]
    public Task<object> Options([FromQuery] string? legalEntityKey, [FromQuery] string? ledgerKey, CancellationToken ct)
        => owner.OptionsAsync(legalEntityKey, ledgerKey, ct);

    [HttpGet("api/fin/cost/v1/selections")]
    public async Task<ActionResult<object>> Select([FromHeader(Name = FinanceContext)] Guid context,
        [FromQuery] string kind, [FromQuery] DateOnly? businessDate, CancellationToken ct)
        => kind == "COST_CENTER" ? Ok(await owner.CentersAsync(context, businessDate, ct))
            : UnprocessableEntity(new { message = "FINANCE_SELECTION_KIND_UNAVAILABLE" });

    // These actions publish versions for the existing cost collector. The R2 command wire uses its own adoption.
    [HttpPost("api/fin/cost/owner-v1/rates")]
    public async Task<ActionResult<ManufacturingRateStatusV1>> Draft([FromHeader(Name = FinanceContext)] Guid context,
        [FromBody] ManufacturingRateDraftV1 command, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await owner.DraftAsync(context, command, ct));

    [HttpPost("api/fin/cost/owner-v1/rates/{id:guid}/submit")]
    public Task<ManufacturingRateStatusV1> Submit([FromHeader(Name = FinanceContext)] Guid context,
        Guid id, [FromBody] ManufacturingRateTransitionV1 command, CancellationToken ct)
        => owner.SubmitAsync(context, id, command, ct);

    [HttpPost("api/fin/cost/owner-v1/rates/{id:guid}/review")]
    public Task<ManufacturingRateStatusV1> Review([FromHeader(Name = FinanceContext)] Guid context,
        Guid id, [FromBody] ManufacturingRateTransitionV1 command, CancellationToken ct)
        => owner.ReviewAsync(context, id, command, ct);

    [HttpPost("api/fin/cost/owner-v1/rates/{id:guid}/release-preview")]
    public Task<ManufacturingRatePreviewV1> Preview([FromHeader(Name = FinanceContext)] Guid context,
        Guid id, [FromBody] ManufacturingRateTransitionV1 command, CancellationToken ct)
        => owner.PreviewAsync(context, id, command, ct);

    [HttpPost("api/fin/cost/owner-v1/rates/{id:guid}/publish")]
    public Task<ManufacturingRateStatusV1> Publish([FromHeader(Name = FinanceContext)] Guid context,
        Guid id, [FromBody] ManufacturingRatePublishV1 command, CancellationToken ct)
        => owner.PublishAsync(context, id, command, ct);
}
