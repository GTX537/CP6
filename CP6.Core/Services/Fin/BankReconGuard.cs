using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>A4 锁后守卫（spec §7.2）。供 JournalEntryService 直查同 DbContext，无循环依赖。</summary>
public static class BankReconGuard
{
    /// <summary>过账守卫：凭证若命中某银行账户的 GL 科目，且该账户存在覆盖凭证落期(FiscalPeriod)的已锁会话 → 拒。</summary>
    public static async Task<FinResult> CheckPostingAsync(CP6Context db, JournalEntry entry)
    {
        var accountIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        // 命中的银行账户（一个 GL 科目可能被多个 BankAccount 共用 → 保守阻断）
        var bankAccts = await db.BankAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.GlAccountId)).Select(a => a.Id).ToListAsync();
        if (bankAccts.Count == 0) return FinResult.Pass();

        // 凭证落期：按 VoucherDate 解析 FiscalPeriod
        var period = await db.FiscalPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Year == entry.VoucherDate.Year && p.Month == entry.VoucherDate.Month);
        if (period == null) return FinResult.Pass();   // 期间还没建 → 必无锁定会话

        var locked = await db.BankStatements.AsNoTracking().AnyAsync(s =>
            bankAccts.Contains(s.BankAccountId) && s.FiscalPeriodId == period.Id && s.Status == BankStatementStatus.Locked);
        return locked ? FinResult.Fail("E-A4-RECON-LOCKED-POSTING") : FinResult.Pass();
    }

    /// <summary>反冲守卫：被反冲原凭证的任一行已被对账(BankReconJournalLink)、且其会话已锁 → 拒。</summary>
    public static async Task<FinResult> CheckReversalAsync(CP6Context db, JournalEntry origin)
    {
        var lineIds = origin.Lines.Select(l => l.Id).ToList();
        var groupIds = await db.BankReconJournalLinks.AsNoTracking()
            .Where(x => lineIds.Contains(x.JournalLineId))
            .Select(x => x.MatchGroupId).Distinct().ToListAsync();
        if (groupIds.Count == 0) return FinResult.Pass();
        var stmtIds = await db.BankReconMatches.AsNoTracking()
            .Where(m => groupIds.Contains(m.Id)).Select(m => m.StatementId).Distinct().ToListAsync();
        var anyLocked = await db.BankStatements.AsNoTracking()
            .AnyAsync(s => stmtIds.Contains(s.Id) && s.Status == BankStatementStatus.Locked);
        return anyLocked ? FinResult.Fail("E-A4-RECON-LOCKED-REVERSAL") : FinResult.Pass();
    }
}
