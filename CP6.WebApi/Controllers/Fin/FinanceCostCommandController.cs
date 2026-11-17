using System.Diagnostics;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route(FinanceCostCommandJournalV1.Commands)]
public sealed class FinanceCostCommandController(ManufacturingStandardDocumentOwnerV1 documents) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpGet("{command:guid}")]
    public Task<IActionResult> Read([FromHeader(Name = Context)] Guid context, Guid command, CancellationToken ct)
        => ReadAsync(command, () => documents.ReadFormalCommandAsync(context, command, ct));

    [HttpGet("{command:guid}/result")]
    public Task<IActionResult> ReadResult([FromHeader(Name = Context)] Guid context, Guid command, CancellationToken ct)
        => ReadAsync(command, () => documents.ReadFormalCommandResultAsync(context, command, ct));

    private async Task<IActionResult> ReadAsync(Guid command, Func<Task<object>> read)
    {
        try
        {
            return Ok(await read());
        }
        catch (OrderPriceCorrectionException error) when (error.Status is 403 or 404)
        {
            return NotFound(Problem(404, "NOT_FOUND_OR_NOT_VISIBLE", null));
        }
        catch (OrderPriceCorrectionException error) when (error.Status == 503)
        {
            var code = error.Message == "OWNER_PARTICIPANT_UNPROVEN" ? error.Message : "AUTHORITY_UNAVAILABLE";
            return StatusCode(503, new
            {
                kind = "UNKNOWN",
                commandId = command,
                problem = Problem(503, code, $"/{FinanceCostCommandJournalV1.Commands}/{command:D}"),
            });
        }
    }

    private object Problem(int status, string code, string? originalQuery) => new
    {
        status,
        code,
        messageKey = code,
        traceId = TraceId(),
        issues = Array.Empty<object>(),
        retry = originalQuery == null ? "NONE" : "QUERY_ORIGINAL",
        commitKnowledge = "UNKNOWN",
        originalQuery,
        currentRowVersion = (string?)null,
    };

    private Guid TraceId()
    {
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
            return Guid.ParseExact(activity.TraceId.ToHexString(), "N");

        if (Guid.TryParse(HttpContext.TraceIdentifier, out var trace))
            return trace;
        trace = Guid.NewGuid();
        HttpContext.TraceIdentifier = trace.ToString("D");
        return trace;
    }
}
