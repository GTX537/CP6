using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public class BankReconMatchTests
{
    // 测试夹具：建账户(GL=G)、期间、会话、若干凭证行
    private static async Task<(BankReconService svc, CP6.Core.EFDbContext.CP6Context db, Guid stmtId, Guid glId)> Fixture(
        string? acctCcy = null)
    {
        var db = TestHelper.CreateInMemoryContext();
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var glId = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = glId, Code = "1002", Name = "银行", Role = "BANK", IsLeaf = true, IsActive = true, CurrencyCd = acctCcy });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = glId, CurrencyCd = acctCcy, IsActive = true };
        db.BankAccounts.Add(acct);
        var stmt = new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, CurrencyCd = acctCcy, Status = BankStatementStatus.Open };
        db.BankStatements.Add(stmt);
        await db.SaveChangesAsync();
        var svc = new BankReconService(db, new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)),
            new FiscalPeriodService(db, 1));
        return (svc, db, stmt.Id, glId);
    }

    // 建一张已过账凭证（两行：银行行 + 对方行），返回银行行 Id
    private static async Task<Guid> PostedBankLine(CP6.Core.EFDbContext.CP6Context db, Guid glId,
        DateTime date, decimal debit, decimal credit, string? ccy = null, decimal? orig = null)
    {
        var entry = new JournalEntry { Id = Guid.NewGuid(), No = $"GL-{Guid.NewGuid():N}".Substring(0, 12),
            VoucherDate = date, Source = VoucherSource.AP, Status = JournalStatus.Posted };
        var bankLine = new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 1, AccountId = glId,
            Debit = debit, Credit = credit, CurrencyCd = ccy, OrigAmount = orig };
        var other = new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 2, AccountId = Guid.NewGuid(),
            Debit = credit, Credit = debit };
        entry.Lines.Add(bankLine); entry.Lines.Add(other);
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        return bankLine.Id;
    }

    private static async Task<Guid> AddStmtLine(CP6.Core.EFDbContext.CP6Context db, Guid stmtId,
        DateTime d, int dir, decimal amt, string? ccy = null)
    {
        var line = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 1,
            TxnDate = d, Direction = (BankLineDirection)dir, Amount = amt, CurrencyCd = ccy, Source = BankLineSource.Imported };
        line.RecomputeSigned();
        db.BankStatementLines.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    [Fact]
    public async Task Candidates_IncludesPosted_ExcludesReversed_AndOccupied()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);                       // 候选：借 +100
        var reversedBank = await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);    // 将被标记 Reversed
        var rev = await db.JournalEntries.FirstAsync(e => e.Lines.Any(l => l.Id == reversedBank));
        rev.Status = JournalStatus.Reversed; await db.SaveChangesAsync();

        // 占用场景：另一条 Posted 行已被其他匹配组占用，应排除
        var occupiedJlId = await PostedBankLine(db, glId, new(2026, 6, 3), 200, 0);
        var matchGroup = new BankReconMatch
        {
            Id = Guid.NewGuid(), StatementId = stmtId,
            MatchType = BankReconMatchType.Manual, StmtSignedSum = 200m,
            MatchedAt = DateTime.UtcNow, MatchedBy = "test"
        };
        db.BankReconMatches.Add(matchGroup);
        db.BankReconJournalLinks.Add(new BankReconJournalLink
        {
            Id = Guid.NewGuid(), MatchGroupId = matchGroup.Id,
            JournalLineId = occupiedJlId, JournalEntryId = Guid.NewGuid(), BankSignedAmount = 200m
        });
        await db.SaveChangesAsync();

        var cands = await svc.GetCandidatesAsync(stmtId, lineId, widen: false);
        Assert.Single(cands);                                           // 反转+占用的均被排除，仅剩第一条
        Assert.Equal(100m, cands[0].BankSignedAmount);
        Assert.DoesNotContain(cands, c => c.JournalLineId == occupiedJlId); // 已占用行不在候选中
    }

    [Fact]
    public async Task Candidates_Foreign_UsesOrigAmount_ExcludesMissingOrig()
    {
        var (svc, db, stmtId, glId) = await Fixture(acctCcy: "USD");
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100, ccy: "USD");
        await PostedBankLine(db, glId, new(2026, 6, 4), 700, 0, ccy: "USD", orig: 100);  // 本位币700 / 原币100 USD
        await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0, ccy: null, orig: null);  // 无原币→排除

        var cands = await svc.GetCandidatesAsync(stmtId, lineId, widen: false);
        Assert.Single(cands);
        Assert.Equal(100m, cands[0].BankSignedAmount);   // 按原币
    }
}
