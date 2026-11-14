using System.Data.Common;
using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.WebApi.Filters;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/bank-recon")]
[Authorize]
public class BankReconciliationController : ControllerBase
{
    private readonly IBankReconService _svc;
    public BankReconciliationController(IBankReconService svc) => _svc = svc;
    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    public record GenVoucherReq(List<Guid> LineIds, Guid CounterAccountId, string? CounterRole, string? PartnerId);
    public record MarkPendingReq(List<Guid> LineIds, BankLineCategory Category, byte[]? RowVersion);
    public record UnlockReq(string Reason);

    [HttpGet("{statementId}/candidates")]
    [RequirePermission("fin-bank-reconciliation", "match")]
    public async Task<IActionResult> Candidates(Guid statementId, [FromQuery] Guid lineId, [FromQuery] bool widen)
        => Ok2(await _svc.GetCandidatesAsync(statementId, lineId, widen));

    [HttpPost("{statementId}/auto-match")]
    [RequirePermission("fin-bank-reconciliation", "match")]
    public Task<IActionResult> AutoMatch(Guid statementId, CancellationToken ct)
        => ExecuteCurrentBankWriteAsync(async () => Fin(await _svc.AutoMatchAsync(statementId, CurrentUser, CurrentActor, ct)));

    [HttpPost("{statementId}/manual-match")]
    [RequirePermission("fin-bank-reconciliation", "match")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> ManualMatch(Guid statementId, [FromBody] ManualMatchRequest req, [FromHeader(Name = "X-Row-Version")] string? rv)
    {
        req.StatementId = statementId;
        var rowVersion = string.IsNullOrEmpty(rv) ? null : Convert.FromBase64String(rv);
        return Fin(await _svc.ManualMatchAsync(req, rowVersion, CurrentUser));
    }

    [HttpPost("unmatch/{groupId}")]
    [RequirePermission("fin-bank-reconciliation", "match")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> Unmatch(Guid groupId) => Fin(await _svc.UnmatchAsync(groupId, CurrentUser));

    [HttpPost("{statementId}/generate-voucher")]
    [RequirePermission("fin-bank-reconciliation", "generate-voucher")]
    public Task<IActionResult> GenerateVoucher(Guid statementId, [FromBody] GenVoucherReq req, CancellationToken ct)
        => ExecuteCurrentBankWriteAsync(async () =>
        {
            var impersonator = Guid.TryParse(User.FindFirst("impersonator_id")?.Value, out var id) ? (Guid?)id : null;
            return Ok2(await _svc.GenerateBankOnlyVoucherAsync(statementId, req.LineIds, req.CounterAccountId,
                req.CounterRole, req.PartnerId, CurrentUser, CurrentActor, ct, impersonator));
        });

    private async Task<IActionResult> ExecuteCurrentBankWriteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (IamAdministrationExceptionV1 error)
        {
            return StatusCode(error.Status, new { code = error.Status, message = error.Message });
        }
        catch (Exception error) when (error is CurrentPermissionUnavailableException or DbException or DbUpdateException)
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_FENCE_UNAVAILABLE" });
        }
    }

    private Task<Guid> CurrentActor(CancellationToken token) => IamAdministrationHttpV1.CurrentActor(HttpContext,
        HttpContext.RequestServices, token, allowImpersonation: true);

    [HttpPost("{statementId}/mark-pending")]
    [RequirePermission("fin-bank-reconciliation", "mark-pending")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> MarkPending(Guid statementId, [FromBody] MarkPendingReq req)
        => Fin(await _svc.MarkPendingAsync(statementId, req.LineIds, req.Category, req.RowVersion, CurrentUser));

    [HttpGet("{statementId}/reconciliation-statement")]
    [RequirePermission("fin-bank-reconciliation", "view")]
    public async Task<IActionResult> ReconStatement(Guid statementId)
        => Ok2(await _svc.GetReconciliationStatementAsync(statementId));

    [HttpPost("{statementId}/lock")]
    [RequirePermission("fin-bank-reconciliation", "lock")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> Lock(Guid statementId) => Fin(await _svc.LockAsync(statementId, CurrentUser));

    [HttpPost("{statementId}/unlock")]
    [RequirePermission("fin-bank-reconciliation", "unlock")]
    [CurrentWriteAuthority]
    public async Task<IActionResult> Unlock(Guid statementId, [FromBody] UnlockReq req) => Fin(await _svc.UnlockAsync(statementId, req.Reason, CurrentUser));
}
