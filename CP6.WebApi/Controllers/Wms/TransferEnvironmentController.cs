using CP6.Core.Services.Wms;
using CP6.Core.Services.Wms.Transfer;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, Route("api/wms/transfer/environment-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransferEnvironmentController(TransferEnvironmentOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet] public async Task<IActionResult> Context(CancellationToken ct) => Result(await owner.ContextAsync(ct));
    [HttpGet("versions/{version}")] public async Task<IActionResult> History(string version, CancellationToken ct) => Result(await owner.HistoryAsync(version, ct));
    [HttpPost("commands"), RequestSizeLimit(8192)] public async Task<IActionResult> Save(TransferEnvironmentCommandV1 command, CancellationToken ct) => Result(await owner.SaveAsync(command, ct));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}

[ApiController, Authorize, Route("api/wms/transfer/sources-v1"), OrderOwnerResult, DocumentReviewAvailability]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransferSourcesController(TransferProductSourcesV1 sources) : ControllerBase
{
    [HttpPost("product"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Product(InventoryIdentityRefV1 reference, CancellationToken ct)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = await sources.ReadAsync(reference, ct) }); }
}
