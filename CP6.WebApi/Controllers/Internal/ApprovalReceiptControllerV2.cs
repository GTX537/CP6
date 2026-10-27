using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Internal;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
[Route("/internal/oa/app/v2"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApprovalReceiptControllerV2 : ControllerBase
{
    [HttpPost("application-receipts"), RequestSizeLimit(1048576)]
    public Task<IActionResult> Receive() => Run(async (owner, ct) => {
        if (!Request.HasJsonContentType()) throw new ScopeAuthorizationExceptionV1("RECEIPT_CONTENT_TYPE_INVALID", 415);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + count > 1048576) throw new ScopeAuthorizationExceptionV1("APPLICATION_RECEIPT_INVALID", 422);
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return Reply(await owner.ReceiveAsync(bytes.ToArray(), ct));
    });
    [HttpGet("application-receipt-ingestions/{eventId:guid}")]
    public Task<IActionResult> Original(Guid eventId) => Run(async (owner, ct) => Reply(await owner.OriginalAsync(eventId, ct)));
    [HttpGet("application-receipt-projections/{streamId:guid}")]
    public Task<IActionResult> Current(Guid streamId) => Run(async (owner, ct) => {
        var value = await owner.CurrentAsync(streamId, ct); return File(ApprovalReceiptIngestionV2.ResponseBytes(value), "application/json");
    });
    private IActionResult Reply(ApprovalReceiptIngestResultV2 result)
    {
        if (result.Status == 409) return StatusCode(result.Status, result.Body);
        Response.StatusCode = result.Status;
        return File(ApprovalReceiptIngestionV2.ResponseBytes(result.Body), "application/json");
    }
    private async Task<IActionResult> Run(Func<ApprovalReceiptIngestionV2, CancellationToken, Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            await ApprovalIntakeServiceAuthorityV2.BindTenant(HttpContext, HttpContext.RequestAborted);
            if (Request.Query.Count != 0) throw new ScopeAuthorizationExceptionV1("APPLICATION_RECEIPT_INVALID", 422);
            return await action(HttpContext.RequestServices.GetRequiredService<ApprovalReceiptIngestionV2>(), HttpContext.RequestAborted);
        }
        catch (ScopeAuthorizationExceptionV1 e) { return Failure(e.Status, e.Message); }
        catch (CurrentPermissionUnavailableException) { return Failure(503, "AUTHORITY_UNAVAILABLE"); }
        catch (Exception e) when (e is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException)
        { return Failure(503, "RECEIPT_OUTCOME_UNKNOWN"); }
    }
    private ObjectResult Failure(int status, string code) => StatusCode(status, new { code, messageKey = code, correlationId = Guid.NewGuid(),
        retryable = status is 429 or 503 or 504, fieldErrors = status == 422 ? new[] { new { field = "$", code } } : [],
        currentRowVersion = (string?)null, resultQueryRef = (string?)null });
}
