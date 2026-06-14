using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 记账凭证服务实现（章01 §5/§6）。
/// 错误码：101 至少两行 / 102 金额为负 / 103 非借贷二选一 / 104 非末级 / 105 已停用 /
/// 106 缺往来单位 / 107 借贷不平 / 108 科目不存在 / 110 仅待复核可过账 / 111 制单≠过账 /
/// 112 期间已结 / 113 手工不可直过 / 114 仅草稿可提交 / 115 仅待复核可驳回 / 130 凭证不存在。
/// </summary>
public class JournalEntryService : IJournalEntryService
{
    private readonly CP6Context _db;
    private readonly IFiscalPeriodService _period;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "GL";
    private const string SystemUser = "SYSTEM";

    public JournalEntryService(CP6Context db, IFiscalPeriodService period, IFinSequenceService seq)
    {
        _db = db;
        _period = period;
        _seq = seq;
    }

    public Task<JournalEntry?> GetAsync(Guid id) =>
        _db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<JournalEntry>> ListAsync(Guid? periodId = null, JournalStatus? status = null)
    {
        var q = _db.JournalEntries.Include(x => x.Lines).AsQueryable();
        if (periodId is Guid pid) q = q.Where(x => x.PeriodId == pid);
        if (status is JournalStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public async Task<Guid> CreateDraftAsync(JournalEntry entry, string makerId)
    {
        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        entry.Status = JournalStatus.Draft;
        entry.MakerId = makerId;
        entry.MakerAt = DateTime.Now;

        var period = await _period.EnsureOpenAsync(entry.VoucherDate, makerId);
        entry.PeriodId = period.Id;
        entry.No = await _seq.NextAsync(SeqKey, entry.VoucherDate);

        NormalizeLines(entry);
        entry.Creator = makerId;
        entry.CreateDate = DateTime.Now;
        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<FinResult> SubmitForReviewAsync(Guid entryId)
    {
        var e = await GetAsync(entryId);
        if (e == null) return FinResult.Fail("E-FIN-130");
        if (e.Status != JournalStatus.Draft) return FinResult.Fail("E-FIN-114");

        var v = await ValidateAsync(e);
        if (!v.Ok) return v;

        e.Status = JournalStatus.PendingReview;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> PostAsync(Guid entryId, string checkerId)
    {
        var e = await GetAsync(entryId);
        if (e == null) return FinResult.Fail("E-FIN-130");
        if (e.Status != JournalStatus.PendingReview) return FinResult.Fail("E-FIN-110");
        if (e.MakerId == checkerId) return FinResult.Fail("E-FIN-111");          // ★ maker-checker 铁则
        if (!await _period.IsOpenAsync(e.PeriodId)) return FinResult.Fail("E-FIN-112");  // ★ 锁期保护

        var v = await ValidateAsync(e);                                          // 过账前再校一次借贷恒等
        if (!v.Ok) return v;

        e.Status = JournalStatus.Posted;
        e.CheckerId = checkerId;
        e.CheckerAt = DateTime.Now;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> AutoPostAsync(JournalEntry entry)
    {
        if (entry.Source == VoucherSource.Manual) return FinResult.Fail("E-FIN-113");  // 手工不可直过

        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        var period = await _period.EnsureOpenAsync(entry.VoucherDate, SystemUser);
        entry.PeriodId = period.Id;
        if (string.IsNullOrEmpty(entry.No)) entry.No = await _seq.NextAsync(SeqKey, entry.VoucherDate);
        NormalizeLines(entry);

        var v = await ValidateAsync(entry);
        if (!v.Ok) return v;
        if (!await _period.IsOpenAsync(entry.PeriodId)) return FinResult.Fail("E-FIN-112");

        entry.Status = JournalStatus.Posted;
        entry.AutoPosted = true;
        entry.MakerId = SystemUser;
        entry.CheckerId = SystemUser;
        var now = DateTime.Now;
        entry.MakerAt = now;
        entry.CheckerAt = now;
        entry.Creator = SystemUser;
        entry.CreateDate = DateTime.Now;
        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> RejectAsync(Guid entryId, string reason)
    {
        var e = await GetAsync(entryId);
        if (e == null) return FinResult.Fail("E-FIN-130");
        if (e.Status != JournalStatus.PendingReview) return FinResult.Fail("E-FIN-115");

        e.Status = JournalStatus.Rejected;
        e.RejectReason = reason;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    /// <summary>给分录补行号并挂上头 Id。</summary>
    private static void NormalizeLines(JournalEntry entry)
    {
        var n = 1;
        foreach (var l in entry.Lines)
        {
            if (l.LineNo == 0) l.LineNo = n;
            l.EntryId = entry.Id;
            n++;
        }
    }

    /// <summary>加载分录所引科目后做完整校验。</summary>
    private async Task<FinResult> ValidateAsync(JournalEntry e)
    {
        var ids = e.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.GlAccounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        return ValidateBalance(e, accounts);
    }

    /// <summary>
    /// ★铁律1：借贷恒等 + 科目合法性校验（纯函数，不碰 DB，便于单测）。decimal 比较防浮点误差。
    /// </summary>
    public static FinResult ValidateBalance(JournalEntry e, IReadOnlyDictionary<Guid, GlAccount> accounts)
    {
        if (e.Lines.Count < 2) return FinResult.Fail("E-FIN-101");

        foreach (var ln in e.Lines)
        {
            if (ln.Debit < 0 || ln.Credit < 0) return FinResult.Fail("E-FIN-102", ln.LineNo);
            if ((ln.Debit > 0) == (ln.Credit > 0)) return FinResult.Fail("E-FIN-103", ln.LineNo);  // 同>0 或 同=0 都不行

            if (!accounts.TryGetValue(ln.AccountId, out var acc)) return FinResult.Fail("E-FIN-108", ln.LineNo);
            if (!acc.IsLeaf) return FinResult.Fail("E-FIN-104", acc.Code);
            if (!acc.IsActive) return FinResult.Fail("E-FIN-105", acc.Code);
            if (acc.RequirePartner && string.IsNullOrEmpty(ln.PartnerId)) return FinResult.Fail("E-FIN-106", acc.Code);
        }

        var totalDebit = e.Lines.Sum(l => l.Debit);
        var totalCredit = e.Lines.Sum(l => l.Credit);
        if (totalDebit != totalCredit) return FinResult.Fail("E-FIN-107", totalDebit, totalCredit);   // ★借贷恒等

        return FinResult.Pass();
    }
}
