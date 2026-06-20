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

    [Fact]
    public async Task AutoMatch_Phase1_UniqueExact_Matches11()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);          // 唯一精确候选
        var r = await svc.AutoMatchAsync(stmtId, "admin");
        Assert.True(r.Ok);
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.Single(await db.BankReconMatches.ToListAsync());
        Assert.Single(await db.BankReconJournalLinks.ToListAsync());
    }

    [Fact]
    public async Task AutoMatch_Phase1_MultipleCandidates_LeftManual()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);
        await PostedBankLine(db, glId, new(2026, 6, 6), 100, 0);          // 两候选→不自动
        await svc.AutoMatchAsync(stmtId, "admin");
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        Assert.Empty(await db.BankReconMatches.ToListAsync());
    }

    [Fact]
    public async Task AutoMatch_Phase2_OneToMany_UniqueSubset_Matches()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        // 一笔银行出账 −90 ↔ 两张付款凭证行（−60 + −30），有界子集和唯一解
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 2, 90);
        await PostedBankLine(db, glId, new(2026, 6, 4), 0, 60);   // 银行侧 −60（Credit）
        await PostedBankLine(db, glId, new(2026, 6, 4), 0, 30);   // 银行侧 −30
        var r = await svc.AutoMatchAsync(stmtId, "admin");
        Assert.True(r.Ok);
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.Equal(2, await db.BankReconJournalLinks.CountAsync());
    }

    [Fact]
    public async Task AutoMatch_Phase2_MultipleSolutions_LeftManual()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 2, 100);
        await PostedBankLine(db, glId, new(2026, 6, 4), 0, 100);   // 解1：单行 −100
        await PostedBankLine(db, glId, new(2026, 6, 4), 0, 60);    // 解2：−60 + −40
        await PostedBankLine(db, glId, new(2026, 6, 4), 0, 40);
        await svc.AutoMatchAsync(stmtId, "admin");
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);  // 多解→不自动
    }

    [Fact]
    public async Task AutoMatch_Phase2_ManyToOne_UniqueSubset_Matches()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        // 两笔银行入账（+60 + +40）↔ 一张合并收款凭证行（银行侧 +100，Debit），有界子集和唯一解
        var l1 = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 60);
        var l2 = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 40);
        await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);   // 银行侧 +100（Debit）
        var r = await svc.AutoMatchAsync(stmtId, "admin");
        Assert.True(r.Ok);
        Assert.Equal(BankLineMatchStatus.Matched, (await db.BankStatementLines.FirstAsync(x => x.Id == l1)).MatchStatus);
        Assert.Equal(BankLineMatchStatus.Matched, (await db.BankStatementLines.FirstAsync(x => x.Id == l2)).MatchStatus);
        var grp = await db.BankReconMatches.SingleAsync();
        Assert.Equal(2, await db.BankStatementLines.CountAsync(x => x.MatchGroupId == grp.Id));  // N:1 组含两条流水
        Assert.Equal(1, await db.BankReconJournalLinks.CountAsync());                            // 单凭证行
    }

    // ── C-3: ManualMatchAsync + UnmatchAsync ──

    [Fact]
    public async Task ManualMatch_NM_BalancedSum_Succeeds()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        // 客户付1000、银行实收990、手续费10 → +990 流水 ↔ 借银行+1000 + 贷银行−10 净+990
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 990);
        var jl1 = await PostedBankLine(db, glId, new(2026, 6, 4), 1000, 0);   // +1000
        var jl2 = await PostedBankLine(db, glId, new(2026, 6, 4), 0, 10);     // −10
        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jl1, jl2 } };
        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.True(r.Ok);
        Assert.Equal(2, await db.BankReconJournalLinks.CountAsync());
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
    }

    [Fact]
    public async Task ManualMatch_UnbalancedSum_Fails()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 990);
        var jl1 = await PostedBankLine(db, glId, new(2026, 6, 4), 1000, 0);
        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jl1 } };
        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-001", r.Code);
    }

    [Fact]
    public async Task ManualMatch_JlNotBankGl_Fails()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        // 凭证行用别的科目
        var entry = new JournalEntry { Id = Guid.NewGuid(), No = "X", VoucherDate = new(2026, 6, 4), Source = VoucherSource.AP, Status = JournalStatus.Posted };
        var jl = new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 1, AccountId = Guid.NewGuid(), Debit = 100 };
        entry.Lines.Add(jl); entry.Lines.Add(new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 2, AccountId = Guid.NewGuid(), Credit = 100 });
        db.JournalEntries.Add(entry); await db.SaveChangesAsync();
        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jl.Id } };
        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-004", r.Code);
    }

    [Fact]
    public async Task ManualMatch_AlreadyOccupied_Fails()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var l1 = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        var jl = await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);
        await svc.ManualMatchAsync(new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { l1 }, JournalLineIds = { jl } }, null, "admin");
        // 第二条流水想再占同一凭证行
        var l2 = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 2, TxnDate = new(2026, 6, 6), Direction = BankLineDirection.Deposit, Amount = 100, Source = BankLineSource.Imported };
        l2.RecomputeSigned(); db.BankStatementLines.Add(l2); await db.SaveChangesAsync();
        var r = await svc.ManualMatchAsync(new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { l2.Id }, JournalLineIds = { jl } }, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-002", r.Code);
    }

    // ── C-3 Bug fixes ──

    /// <summary>Bug2: 外币账户选了原币缺失的凭证行 → 应 return FinResult.Fail("E-A4-MATCH-003") 而非 throw</summary>
    [Fact]
    public async Task ManualMatch_ForeignCurrencyMismatch_ReturnsFail()
    {
        var (svc, db, stmtId, glId) = await Fixture(acctCcy: "USD");
        // 流水：外币USD入账 +100
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100, ccy: "USD");
        // 凭证行：命中银行GL、Posted，但 OrigAmount == null（外币原币缺失）
        var jlId = await PostedBankLine(db, glId, new(2026, 6, 4), 700, 0, ccy: null, orig: null);
        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jlId } };

        // 必须返回 Fail，不能 throw
        var ex = await Record.ExceptionAsync(() => svc.ManualMatchAsync(req, null, "admin"));
        Assert.Null(ex);   // 之前会 throw InvalidOperationException，现在不应抛出

        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-003", r.Code);

        // 不应有任何持久化记录
        Assert.Empty(await db.BankReconMatches.ToListAsync());
        Assert.Empty(await db.BankReconJournalLinks.ToListAsync());
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
    }

    /// <summary>Bug2: 外币账户选了币种不符的凭证行 → 同样 return FinResult.Fail("E-A4-MATCH-003")</summary>
    [Fact]
    public async Task ManualMatch_ForeignCurrencyMismatch_WrongCurrency_ReturnsFail()
    {
        var (svc, db, stmtId, glId) = await Fixture(acctCcy: "USD");
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100, ccy: "USD");
        // 凭证行币种为 EUR（≠ USD）
        var jlId = await PostedBankLine(db, glId, new(2026, 6, 4), 700, 0, ccy: "EUR", orig: 100m);
        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jlId } };

        var ex = await Record.ExceptionAsync(() => svc.ManualMatchAsync(req, null, "admin"));
        Assert.Null(ex);

        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-003", r.Code);
        Assert.Empty(await db.BankReconMatches.ToListAsync());
    }

    /// <summary>Bug1+reversal guard: 反转凭证行 → 现有 guard 返回 E-A4-MATCH-003（previously-untested path）</summary>
    [Fact]
    public async Task ManualMatch_ReversalEntry_Fails()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        // 建一张 Source==Reversal 的凭证（直接设标志位）
        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(), No = "REV-001", VoucherDate = new(2026, 6, 4),
            Source = VoucherSource.Reversal, Status = JournalStatus.Posted
        };
        var jl = new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 1, AccountId = glId, Debit = 100 };
        entry.Lines.Add(jl);
        entry.Lines.Add(new JournalLine { Id = Guid.NewGuid(), EntryId = entry.Id, LineNo = 2, AccountId = Guid.NewGuid(), Credit = 100 });
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        var req = new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jl.Id } };
        var r = await svc.ManualMatchAsync(req, null, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-MATCH-003", r.Code);
        Assert.Empty(await db.BankReconMatches.ToListAsync());
    }

    [Fact]
    public async Task Unmatch_ReleasesLinesAndLinks()
    {
        var (svc, db, stmtId, glId) = await Fixture();
        var lineId = await AddStmtLine(db, stmtId, new(2026, 6, 5), 1, 100);
        var jl = await PostedBankLine(db, glId, new(2026, 6, 4), 100, 0);
        await svc.ManualMatchAsync(new ManualMatchRequest { StatementId = stmtId, StatementLineIds = { lineId }, JournalLineIds = { jl } }, null, "admin");
        var group = await db.BankReconMatches.FirstAsync();
        var r = await svc.UnmatchAsync(group.Id, "admin");
        Assert.True(r.Ok);
        Assert.Empty(await db.BankReconJournalLinks.ToListAsync());
        Assert.Empty(await db.BankReconMatches.ToListAsync());
        var line = await db.BankStatementLines.FirstAsync(x => x.Id == lineId);
        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        Assert.Null(line.MatchGroupId);
    }
}
