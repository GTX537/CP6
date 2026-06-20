using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BankReconService : IBankReconService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;    // used by C-2 AutoMatch
    private readonly IFiscalPeriodService _period;     // used by C-2 AutoMatch
    private const int DefaultWindowDays = 90;
    private const int SubsetSumK = 8;                  // used by C-2 AutoMatch
    // must exceed the max possible date-distance so amount-match always outranks date-closeness
    private const int AmountMismatchPenalty = 100_000;

    public BankReconService(CP6Context db, IJournalEntryService journal, IFiscalPeriodService period)
    { _db = db; _journal = journal; _period = period; }

    public async Task<List<BankCandidateLine>> GetCandidatesAsync(Guid statementId, Guid statementLineId, bool widen)
    {
        var stmt = await _db.BankStatements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        var line = await _db.BankStatementLines.AsNoTracking().FirstAsync(x => x.Id == statementLineId);
        var raw = await LoadCandidateRowsAsync(stmt, widen ? null : DefaultWindowDays);
        // 按 (金额接近 + 日期接近) 排序，金额完全相等优先
        raw.ForEach(c => c.Rank = Math.Abs((c.VoucherDate - line.TxnDate).Days)
            + (c.BankSignedAmount == line.SignedAmount ? 0 : AmountMismatchPenalty));
        return raw.OrderBy(c => c.Rank).ThenBy(c => c.VoucherDate).ToList();
    }

    /// <summary>账面侧候选来源（spec §4.2）：命中银行GL、Posted、未反转、未占用、VoucherDate≤PeriodEnd、窗口、外币原币规则。</summary>
    private async Task<List<BankCandidateLine>> LoadCandidateRowsAsync(BankStatement stmt, int? windowDays)
    {
        var acct = await _db.BankAccounts.AsNoTracking().FirstAsync(x => x.Id == stmt.BankAccountId);
        var isForeign = !FxConstants.IsBase(acct.CurrencyCd);
        var occupied = _db.BankReconJournalLinks.Select(x => x.JournalLineId);
        var lowerDate = windowDays is int w ? stmt.PeriodStart.AddDays(-w) : DateTime.MinValue;

        var rows = await (from jl in _db.JournalLines.AsNoTracking()
                          join je in _db.JournalEntries.AsNoTracking() on jl.EntryId equals je.Id
                          where jl.AccountId == acct.GlAccountId
                                && je.Status == JournalStatus.Posted
                                && je.Source != VoucherSource.Reversal
                                && je.VoucherDate <= stmt.PeriodEnd
                                && je.VoucherDate >= lowerDate
                                && !occupied.Contains(jl.Id)
                          select new { jl, je }).ToListAsync();

        var list = new List<BankCandidateLine>();
        foreach (var r in rows)
        {
            decimal bankSigned;
            if (isForeign)
            {
                if (r.jl.OrigAmount is not decimal orig || r.jl.CurrencyCd != acct.CurrencyCd)
                    continue;   // 外币：缺原币/币种不符 → 不进自动候选（§4.2/§6）
                bankSigned = r.jl.Debit > 0 ? orig : -orig;
            }
            else
            {
                bankSigned = r.jl.Debit - r.jl.Credit;   // 本位币 Debit=+,Credit=−
            }
            list.Add(new BankCandidateLine
            {
                JournalLineId = r.jl.Id, JournalEntryId = r.je.Id, EntryNo = r.je.No,
                VoucherDate = r.je.VoucherDate, BankSignedAmount = bankSigned,
                CurrencyCd = r.jl.CurrencyCd, PartnerId = r.jl.PartnerId, Memo = r.jl.Memo,
            });
        }
        return list;
    }

    // ── C-2/C-3/D/E 实现 ──
    public Task<FinResult> AutoMatchAsync(Guid statementId, string? user) => throw new NotImplementedException();
    public Task<FinResult> ManualMatchAsync(ManualMatchRequest req, byte[]? stmtLineRowVersion, string? user) => throw new NotImplementedException();
    public Task<FinResult> UnmatchAsync(Guid groupId, string? user) => throw new NotImplementedException();
    public Task<List<BankOnlyLineResult>> GenerateBankOnlyVoucherAsync(Guid statementId, List<Guid> lineIds, Guid counterAccountId, string? counterRole, string? partnerId, string? user) => throw new NotImplementedException();
    public Task<FinResult> MarkPendingAsync(Guid statementId, List<Guid> lineIds, BankLineCategory category, byte[]? rowVersion, string? user) => throw new NotImplementedException();
    public Task<ReconciliationStatementDto> GetReconciliationStatementAsync(Guid statementId) => throw new NotImplementedException();
    public Task<FinResult> LockAsync(Guid statementId, string? user) => throw new NotImplementedException();
    public Task<FinResult> UnlockAsync(Guid statementId, string reason, string? user) => throw new NotImplementedException();
}
