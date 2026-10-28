using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Plm;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Internal;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IterationReviewFactControllerV1 : ControllerBase
{
    [HttpPost(IterationReviewFactReceiverV1.Route), RequestSizeLimit(1048576)]
    public Task<IActionResult> Receive() => Run(async (receiver, ct) => {
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + read > 1048576) throw new ScopeAuthorizationExceptionV1("DECISION_SHAPE_INVALID", 422);
            await bytes.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var result = await receiver.ReceiveAsync(bytes.ToArray(), ct); return StatusCode(result.Status, result.Body);
    });
    [HttpGet(IterationReviewFactReceiverV1.Route + "/{eventId:guid}")]
    public Task<IActionResult> Received(Guid eventId) => Run(async (receiver, ct) => {
        var result = await receiver.ReceivedAsync(eventId, ct); return StatusCode(result.Status, result.Body);
    });
    [HttpGet(IterationReviewFactReceiverV1.ApplicationRoute + "{streamId:guid}/result")]
    public Task<IActionResult> Application(Guid streamId) => Run(async (receiver, ct)
        => File(IterationReviewFactReceiverV1.ApplicationResponseBytes(await receiver.ApplicationAsync(streamId, ct)), "application/json"));
    [HttpGet(IterationReviewFactReceiverV1.ApplicationRoute + "{streamId:guid}/receipts")]
    public Task<IActionResult> Receipts(Guid streamId, [FromQuery] string afterRevision = "0", [FromQuery] int limit = 20, [FromQuery] string? cursor = null)
        => Run(async (receiver, ct) => File(IterationReviewFactReceiverV1.ApplicationResponseBytes(
            await receiver.ReceiptChainAsync(streamId, afterRevision, limit, cursor, ct)), "application/json"), true);
    private async Task<IActionResult> Run(Func<IterationReviewFactReceiverV1, CancellationToken, Task<IActionResult>> action, bool chain = false)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            await ApprovalFactReceiverServiceAuthorityV2.BindTenant(HttpContext, HttpContext.RequestAborted);
            if (Request.Query.Any(x => !chain || x.Key is not ("afterRevision" or "limit" or "cursor") || x.Value.Count != 1))
                throw new ScopeAuthorizationExceptionV1("DECISION_SHAPE_INVALID", 422);
            return await action(HttpContext.RequestServices.GetRequiredService<IterationReviewFactReceiverV1>(), HttpContext.RequestAborted);
        }
        catch (ScopeAuthorizationExceptionV1 e) { return Failure(e.Status, e.Message); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException e) { return Failure(e.Status, e.Message); }
        catch (CurrentPermissionUnavailableException) { return Failure(503, "AUTHORITY_UNAVAILABLE"); }
        catch (Exception e) when (e is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException)
        { return Failure(503, "AUTHORITY_UNAVAILABLE"); }
    }
    private ObjectResult Failure(int status, string code) => StatusCode(status, new { code, messageKey = code,
        correlationId = Guid.NewGuid(), retryable = status is 429 or 503 or 504, fieldErrors = Array.Empty<object>(),
        currentRowVersion = (string?)null, resultQueryRef = (string?)null });
}
