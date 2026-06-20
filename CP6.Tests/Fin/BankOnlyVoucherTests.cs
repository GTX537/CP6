using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public class BankOnlyVoucherTests
{
    private static async Task<(BankReconService svc, CP6.Core.EFDbContext.CP6Context db, Guid stmtId, Guid bankGlId, Guid feeGlId)> Fixture()
    {
        var db = TestHelper.CreateInMemoryContext();
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid(); var feeGl = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", Role = "BANK", IsLeaf = true, IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = feeGl, Code = "6603", Name = "财务费用", Role = "FIN_EXPENSE", IsLeaf = true, IsActive = true });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, IsActive = true };
        db.BankAccounts.Add(acct);
        var stmt = new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Status = BankStatementStatus.Open };
        db.BankStatements.Add(stmt);
        await db.SaveChangesAsync();
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var svc = new BankReconService(db, journal, new FiscalPeriodService(db, 1));
        return (svc, db, stmt.Id, bankGl, feeGl);
    }

    private static async Task<Guid> Fee(CP6.Core.EFDbContext.CP6Context db, Guid stmtId, decimal amt)
    {
        var l = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 1,
            TxnDate = new(2026, 6, 5), Direction = BankLineDirection.Withdrawal, Amount = amt, Source = BankLineSource.Imported,
            Description = "手续费", Category = BankLineCategory.BankCharge };
        l.RecomputeSigned(); db.BankStatementLines.Add(l); await db.SaveChangesAsync();
        return l.Id;
    }

    [Fact]
    public async Task Generate_FeeWithdrawal_CreatesVoucher_AndMatchesLine()
    {
        var (svc, db, stmtId, bankGl, feeGl) = await Fixture();
        var lineId = await Fee(db, stmtId, 10);
        var res = await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { lineId }, feeGl, null, null, "admin");
        Assert.Single(res);
        Assert.True(res[0].Ok);
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.NotNull(line.GeneratedJournalEntryId);
        var entry = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.Id == line.GeneratedJournalEntryId);
        Assert.Equal(VoucherSource.BankRecon, entry.Source);
        Assert.Contains(entry.Lines, l => l.AccountId == feeGl && l.Debit == 10m);    // 借 财务费用
        Assert.Contains(entry.Lines, l => l.AccountId == bankGl && l.Credit == 10m);  // 贷 银行GL
        Assert.Single(await db.BankReconJournalLinks.ToListAsync());                  // 关联新银行GL凭证行
    }

    [Fact]
    public async Task Generate_Idempotent_SecondCall_Rejected()
    {
        var (svc, db, stmtId, bankGl, feeGl) = await Fixture();
        var lineId = await Fee(db, stmtId, 10);
        await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { lineId }, feeGl, null, null, "admin");
        var res = await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { lineId }, feeGl, null, null, "admin");
        Assert.False(res[0].Ok);
        Assert.Equal("E-A4-BANKONLY-DUP", res[0].Code);
    }

    [Fact]
    public async Task Generate_Batch_PerLineResult_OneFailDoesNotRollbackOthers()
    {
        var (svc, db, stmtId, bankGl, feeGl) = await Fixture();
        var ok = await Fee(db, stmtId, 10);
        var dup = await Fee(db, stmtId, 20);
        await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { dup }, feeGl, null, null, "admin");  // dup 先生成
        var res = await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { ok, dup }, feeGl, null, null, "admin");
        Assert.Equal(2, res.Count);
        Assert.True(res.First(r => r.LineId == ok).Ok);
        Assert.False(res.First(r => r.LineId == dup).Ok);   // 逐行：dup 失败不影响 ok
    }

    [Fact]
    public async Task RegenerateAfterReverse_ClearsOldId_WritesNew()
    {
        var (svc, db, stmtId, bankGl, feeGl) = await Fixture();
        var lineId = await Fee(db, stmtId, 10);
        await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { lineId }, feeGl, null, null, "admin");
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        var oldEntryId = line.GeneratedJournalEntryId!.Value;
        var group = await db.BankReconMatches.FirstAsync();

        // 改错走反冲：先 Unmatch → ReverseAsync(原凭证) → 清旧 GeneratedJournalEntryId
        await svc.UnmatchAsync(group.Id, "admin");
        await new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db))
            .ReverseAsync(oldEntryId, "admin", "科目错", autoPost: true);
        line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        line.GeneratedJournalEntryId = null;   // 反冲后清空（前端/服务流程；本测显式清以模拟）
        await db.SaveChangesAsync();

        // 重生成 → 不被幂等挡，写新 id
        var res = await svc.GenerateBankOnlyVoucherAsync(stmtId, new() { lineId }, feeGl, null, null, "admin");
        Assert.True(res[0].Ok);
        line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.NotEqual(oldEntryId, line.GeneratedJournalEntryId);
    }
}
