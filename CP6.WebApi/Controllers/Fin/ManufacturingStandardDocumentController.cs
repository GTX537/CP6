using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route(StandardDocumentBinding.Documents)]
public sealed class ManufacturingStandardDocumentController(ManufacturingStandardDocumentOwnerV1 documents) : ControllerBase
{
    private const string Context = "X-Finance-Context";

    [HttpPost]
    public Task<IActionResult> Create([FromHeader(Name = Context)] Guid context, [FromBody] StandardDocumentCreateV1 input,
        CancellationToken ct) => Write(input.CommandId, StatusCodes.Status201Created, () => documents.CreateAsync(context, input, ct));

    [HttpPost("{id:guid}/collection-plans")]
    public Task<IActionResult> Prepare([FromHeader(Name = Context)] Guid context, Guid id, [FromBody] StandardDocumentPrepareV1 input,
        CancellationToken ct) => Write(input.CommandId, StatusCodes.Status201Created, () => documents.PrepareAsync(context, id, input, ct));

    [HttpPost("{id:guid}/freeze")]
    public Task<IActionResult> Freeze([FromHeader(Name = Context)] Guid context, Guid id, [FromBody] StandardDocumentFreezeV1 input,
        CancellationToken ct) => Write(input.CommandId, StatusCodes.Status200OK, () => documents.FreezeAsync(context, id, input, ct));

    [HttpGet("{id:guid}")]
    public Task<object> Read([FromHeader(Name = Context)] Guid context, Guid id, CancellationToken ct)
        => documents.ReadAsync(context, id, null, ct);

    [HttpGet("{id:guid}/versions/{version:long}")]
    public Task<object> ReadVersion([FromHeader(Name = Context)] Guid context, Guid id, long version, CancellationToken ct)
        => documents.ReadAsync(context, id, version, ct);

    [HttpGet("/" + StandardDocumentBinding.Commands + "/{command:guid}")]
    public Task<object> ReadCommand([FromHeader(Name = Context)] Guid context, Guid command, CancellationToken ct)
        => documents.ReadCommandAsync(context, command, ct);

    private async Task<IActionResult> Write(Guid command, int status, Func<Task<StandardDocumentReplyV1>> write)
    {
        try
        {
            return StatusCode(status, await write());
        }
        catch (OrderPriceCorrectionException error) when (error.Status == 503 && error.Message == "OPERATIONS_OUTCOME_UNKNOWN")
        {
            return Accepted(new { kind = "OUTCOME_UNKNOWN", commandId = command, queryPath = $"/{StandardDocumentBinding.Commands}/{command:D}" });
        }
    }
}
