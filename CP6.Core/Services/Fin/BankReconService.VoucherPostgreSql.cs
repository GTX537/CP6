using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public partial class BankReconService
{
    private async Task<List<BankOnlyLineResult>> GenerateBankOnlyVoucherPostgreSqlAsync(Guid statementId,
        List<Guid> lineIds, Guid counterAccountId, string? counterRole, string? partnerId, string? user,
        Func<CancellationToken, Task<Guid>>? currentActor, Guid? impersonatorId, CancellationToken ct)
    {
        if (_authority?.Owns(_db) != true || currentActor == null)
            throw new CurrentPermissionUnavailableException();

        var results = new List<BankOnlyLineResult>(lineIds.Count);
        foreach (var lineId in lineIds)
        {
            try
            {
                var row = await _authority.ExecuteAsync("fin-bank-reconciliation:generate-voucher", currentActor,
                    async (actor, token) =>
                    {
                        var result = await GenerateBankVoucherLineAsync(statementId, lineId, counterAccountId,
                            counterRole, partnerId, user, impersonatorId, token);
                        if (await currentActor(token) != actor)
                            throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                        return result;
                    }, ct);
                results.Add(row);
            }
            catch (BankVoucherRejected rejected)
            {
                // AutoPost may have saved a sequence number or period before rejecting the voucher.
                results.Add(BankVoucherFailed(lineId, rejected.Code));
            }
            catch (IamAdministrationExceptionV1 denied) when (denied.Status is 401 or 403 && results.Count > 0)
            {
                // All later lines require the same authority. Keep the already committed line results.
                for (var i = results.Count; i < lineIds.Count; i++)
                    results.Add(BankVoucherFailed(lineIds[i], denied.Message));
                break;
            }
            catch (DbUpdateConcurrencyException)
            {
                results.Add(BankVoucherFailed(lineId, "E-A4-BANKONLY-FAIL"));
            }
        }
        return results;
    }

    private async Task<BankOnlyLineResult> GenerateBankVoucherLineAsync(Guid statementId, Guid lineId,
        Guid counterAccountId, string? counterRole, string? partnerId, string? user, Guid? impersonatorId, CancellationToken ct)
    {
        var stmt = await _db.BankStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == statementId, ct);
        if (stmt == null) return BankVoucherFailed(lineId, "E-A4-MATCH-004");
        if (stmt.Status != BankStatementStatus.Open) return BankVoucherFailed(lineId, "E-A4-STATEMENT-LOCKED");

        var line = await _db.BankStatementLines.SingleOrDefaultAsync(x => x.Id == lineId && x.StatementId == statementId, ct);
        if (line == null) return BankVoucherFailed(lineId, "E-A4-MATCH-004");
        if (line.MatchStatus == BankLineMatchStatus.Matched || line.GeneratedJournalEntryId != null)
            return BankVoucherFailed(lineId, "E-A4-BANKONLY-DUP");

        var counterId = await ResolveBankVoucherCounterAsync(counterAccountId, counterRole, ct);
        if (counterId == null) return BankVoucherFailed(lineId, "E-A4-MATCH-003");
        var account = await _db.BankAccounts.AsNoTracking().SingleAsync(x => x.Id == stmt.BankAccountId, ct);
        // The auto-voucher source index identifies a posted source document. Here the source is a bank line.
        var result = await PostBankOnlyVoucherAsync(stmt, account, line, counterId.Value, partnerId, user,
            $"BKR-LINE-{line.Id:N}", ct);
        if (!result.Ok) throw new BankVoucherRejected(result.Code);
        _db.Sys_OperLogs.Add(new()
        {
            TenantId = _db.CurrentTenantId, UserName = user, ImpersonatorId = impersonatorId, HttpMethod = "POST",
            Controller = "BankReconciliation", Action = "GenerateBankVoucherLine",
            RequestUrl = $"/api/fin/bank-recon/{statementId:D}/generate-voucher",
            RequestBody = JsonSerializer.Serialize(new { lineId, result.JournalEntryId }),
            StatusCode = 200
        });
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private Task<Guid?> ResolveBankVoucherCounterAsync(Guid accountId, string? role, CancellationToken ct)
    {
        if (accountId != Guid.Empty) return Task.FromResult<Guid?>(accountId);
        if (role == null) return Task.FromResult<Guid?>(null);
        return _db.GlAccounts.Where(a => a.Role == role && a.IsActive && a.IsLeaf)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
    }

    private static BankOnlyLineResult BankVoucherFailed(Guid lineId, string? code)
        => new() { LineId = lineId, Ok = false, Code = code };

    private sealed class BankVoucherRejected(string? code) : Exception(code)
    {
        public string? Code { get; } = code;
    }
}
