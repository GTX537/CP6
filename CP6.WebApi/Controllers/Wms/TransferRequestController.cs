using CP6.Core.Services.Wms.Transfer;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, Route("api/wms/transfer/native-requests-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransferRequestController(TransferRequestOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("context")] public async Task<IActionResult> Context(CancellationToken ct) => Result(await owner.ContextAsync(ct));
    [HttpGet] public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default) => Result(await owner.ListAsync(page, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Read(Guid id, CancellationToken ct) => Result(await owner.ReadAsync(id, ct));
    [HttpGet("{id:guid}/revisions/{revision}")] public async Task<IActionResult> History(Guid id, string revision, CancellationToken ct) => Result(await owner.HistoryAsync(id, revision, ct));
    [HttpPost, RequestSizeLimit(2097152)] public async Task<IActionResult> Create(TransferRequestCreateV1 command, CancellationToken ct) => Result(await owner.CreateAsync(command, ct));
    [HttpPost("{id:guid}/edit"), RequestSizeLimit(2097152)] public async Task<IActionResult> Edit(Guid id, TransferRequestEditV1 command, CancellationToken ct) => Result(await owner.EditAsync(id, command, ct));
    [HttpPost("{id:guid}/decisions/{operation}"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Decide(Guid id, string operation, TransferRequestActionV1 command, CancellationToken ct) => Result(await owner.ActionAsync(id, operation, command, ct));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
