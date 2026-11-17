using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route(ManufacturingCostMasterV1.Versions)]
public sealed class ManufacturingCostMasterController(ManufacturingCostMasterOwnerV1 masters) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpPost("policies")]
    public Task<IActionResult> SubmitPolicy([FromHeader(Name = Context)] Guid context, [FromBody] ManufacturingCostPolicySubmitV1 input,
        CancellationToken ct) => Write(input.CommandId, StatusCodes.Status201Created, () => masters.SubmitPolicyAsync(context, input, ct));

    [HttpPost("rate-bases")]
    public Task<IActionResult> SubmitBasis([FromHeader(Name = Context)] Guid context, [FromBody] ManufacturingRateBasisSubmitV1 input,
        CancellationToken ct) => Write(input.CommandId, StatusCodes.Status201Created, () => masters.SubmitBasisAsync(context, input, ct));

    [HttpPost("{resource}/{id:guid}/review")]
    public Task<IActionResult> Review([FromHeader(Name = Context)] Guid context, string resource, Guid id,
        [FromBody] ManufacturingCostMasterDecisionV1 input, CancellationToken ct)
        => Write(input.CommandId, StatusCodes.Status200OK, () => masters.ReviewAsync(context, resource, id, input, ct));

    [HttpPost("{resource}/{id:guid}/publish")]
    public Task<IActionResult> Publish([FromHeader(Name = Context)] Guid context, string resource, Guid id,
        [FromBody] ManufacturingCostMasterDecisionV1 input, CancellationToken ct)
        => Write(input.CommandId, StatusCodes.Status200OK, () => masters.PublishAsync(context, resource, id, input, ct));

    [HttpGet("{id:guid}")]
    public Task<object> Read([FromHeader(Name = Context)] Guid context, Guid id, CancellationToken ct)
        => masters.ReadAsync(context, id, ct);

    private async Task<IActionResult> Write(Guid command, int status, Func<Task<JsonElement>> operation)
    {
        try
        {
            return StatusCode(status, await operation());
        }
        catch (OrderPriceCorrectionException error) when (error.Status == 503 && error.Message == "OPERATIONS_OUTCOME_UNKNOWN")
        {
            return Accepted(new
            {
                kind = "OUTCOME_UNKNOWN", commandId = command,
                queryPath = $"/{FinanceCostCommandJournalV1.Commands}/{command:D}", workId = (Guid?)null,
            });
        }
    }
}
