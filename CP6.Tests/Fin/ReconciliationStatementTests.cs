using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public class ReconciliationStatementTests
{
    private static async Task<(BankReconService svc, CP6.Core.EFDbContext.CP6Context db, Guid stmtId, Guid bankGl)> Fixture(
        string? acctCcy, decimal opening, decimal closing)
    {
        var db = TestHelper.CreateInMemoryContext();
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", Role = "BANK", IsLeaf = true, IsActive = true, CurrencyCd = acctCcy });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, CurrencyCd = acctCcy, IsActive = true };
        db.BankAccounts.Add(acct);
        var stmt = new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, CurrencyCd = acctCcy,
            OpeningBalance = opening, ClosingBalance = closing, Status = BankStatementStatus.Open };
        db.BankStatements.Add(stmt);
        await db.SaveChangesAsync();
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        return (new BankReconService(db, journal, new FiscalPeriodService(db, 1)), db, stmt.Id, bankGl);
    }

    private static async Task StmtLine(CP6.Core.EFDbContext.CP6Context db, Guid stmtId, int dir, decimal amt, string ccy = null!)
    {
        var l = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 1, TxnDate = new(2026, 6, 5),
            Direction = (BankLineDirection)dir, Amount = amt, CurrencyCd = ccy, Source = BankLineSource.Imported };
        l.RecomputeSigned(); db.BankStatementLines.Add(l); await db.SaveChangesAsync();
    }

    private static async Task BankGlEntry(CP6.Core.EFDbContext.CP6Context db, Guid bankGl, DateTime date, decimal debit, decimal credit, string? ccy = null, decimal? orig = null)
    {
        var e = new JournalEntry { Id = Guid.NewGuid(), No = $"GL-{Guid.NewGuid():N}".Substring(0, 12), VoucherDate = date, Source = VoucherSource.AP, Status = JournalStatus.Posted };
        e.Lines.Add(new JournalLine { Id = Guid.NewGuid(), EntryId = e.Id, LineNo = 1, AccountId = bankGl, Debit = debit, Credit = credit, CurrencyCd = ccy, OrigAmount = orig });
        e.Lines.Add(new JournalLine { Id = Guid.NewGuid(), EntryId = e.Id, LineNo = 2, AccountId = Guid.NewGuid(), Debit = credit, Credit = debit });
        db.JournalEntries.Add(e); await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InternalDiff_Zero_WhenOpeningPlusFlowEqualsClosing()
    {
        // 期初0 + 入100 − 出30 = 期末70
        var (svc, db, stmtId, _) = await Fixture(null, 0, 70);
        await StmtLine(db, stmtId, 1, 100);
        await StmtLine(db, stmtId, 2, 30);
        var s = await svc.GetReconciliationStatementAsync(stmtId);
        Assert.Equal(0m, s.StatementInternalDiff);
        Assert.Equal(100m, s.TotalDeposit);
        Assert.Equal(30m, s.TotalWithdrawal);
    }

    [Fact]
    public async Task BookOnly_DepositInTransit_AdjustsBankSide()
    {
        // GL 有一笔借100（账面已记），但流水无 → 在途存款，调银行侧
        var (svc, db, stmtId, bankGl) = await Fixture(null, 0, 0);
        await BankGlEntry(db, bankGl, new(2026, 6, 4), 100, 0);   // 未占用账面行
        var s = await svc.GetReconciliationStatementAsync(stmtId);
        Assert.Equal(100m, s.GlBankEndingBalance);
        Assert.Equal(100m, s.BookOnlyDepositInTransit);
        Assert.Equal(100m, s.BankAdjustedBalance);   // Closing(0)+100
        // BookAdjusted = GL(100) + 0 − 0 = 100
        Assert.Equal(0m, s.ReconciledDiff);
    }

    [Fact]
    public async Task Foreign_GlBankEndingBalance_UsesOrigAmount_NotBaseCurrency()
    {
        // USD 账户：GL 行 本位币700/原币100 → GlBankEndingBalance 按原币100，不用700
        var (svc, db, stmtId, bankGl) = await Fixture("USD", 0, 0);
        await BankGlEntry(db, bankGl, new(2026, 6, 4), 700, 0, ccy: "USD", orig: 100);
        var s = await svc.GetReconciliationStatementAsync(stmtId);
        Assert.Equal(100m, s.GlBankEndingBalance);   // 原币，不是700
    }
}
