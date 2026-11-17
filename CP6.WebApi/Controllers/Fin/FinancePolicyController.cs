using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route("api/fin/policy/owner-v1")]
public sealed class FinancePolicyController(FinancePolicyOwnerV1 policies) : ControllerBase
{
    private const string Context = "X-Finance-Context";
    [HttpPost]
    public Task<IActionResult> Submit([FromHeader(Name=Context)] Guid context, [FromBody] FinancePolicySubmitV1 input, CancellationToken ct)
        => Write(input.CommandId, () => policies.SubmitAsync(context,input,ct));
    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve([FromHeader(Name=Context)] Guid context, Guid id, [FromBody] FinancePolicyDecisionV1 input, CancellationToken ct)
        => Write(input.CommandId, () => policies.ApproveAsync(context,id,input,ct));
    [HttpPost("{id:guid}/publish")]
    public Task<IActionResult> Publish([FromHeader(Name=Context)] Guid context, Guid id, [FromBody] FinancePolicyControlV1 input, CancellationToken ct)
        => Write(input.CommandId, () => policies.PublishAsync(context,id,input,ct));
    [HttpPost("{id:guid}/revoke")]
    public Task<IActionResult> Revoke([FromHeader(Name=Context)] Guid context, Guid id, [FromBody] FinancePolicyControlV1 input, CancellationToken ct)
        => Write(input.CommandId, () => policies.RevokeAsync(context,id,input,ct));
    [HttpGet("{id:guid}")]
    public Task<JsonElement> Read([FromHeader(Name=Context)] Guid context, Guid id, CancellationToken ct)
        => policies.ReadAsync(context,id,ct);
    [HttpGet("commands/{id:guid}")]
    public Task<JsonElement> Command([FromHeader(Name=Context)] Guid context, Guid id, CancellationToken ct)
        => policies.CommandAsync(context,id,ct);
    private async Task<IActionResult> Write(Guid command, Func<Task<JsonElement>> operation)
    {
        try { return Ok(await operation()); }
        catch (OrderPriceCorrectionException error) when (error.Status==503 && error.Message=="OPERATIONS_OUTCOME_UNKNOWN")
        { return Accepted(new { kind="OUTCOME_UNKNOWN",commandId=command,queryPath=$"/api/fin/policy/owner-v1/commands/{command:D}" }); }
    }
}
