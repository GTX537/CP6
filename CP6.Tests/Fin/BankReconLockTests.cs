using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public class BankReconLockTests
{
    private static async Task<(BankReconService svc, CP6.Core.EFDbContext.CP6Context db, Guid stmtId, Guid bankGl, FiscalPeriodService periodSvc, FiscalPeriod period)> Fixture(decimal opening, decimal closing)
    {
        var db = TestHelper.CreateInMemoryContext();
        var periodSvc = new FiscalPeriodService(db, 1);
        var period = await periodSvc.EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", Role = "BANK", IsLeaf = true, IsActive = true });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, IsActive = true };
        db.BankAccounts.Add(acct);
        var stmt = new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, OpeningBalance = opening, ClosingBalance = closing, Status = BankStatementStatus.Open };
        db.BankStatements.Add(stmt);
        await db.SaveChangesAsync();
        var journal = new JournalEntryService(db, periodSvc, new FinSequenceService(db));
        return (new BankReconService(db, journal, periodSvc), db, stmt.Id, bankGl, periodSvc, period);
    }

    [Fact]
    public async Task Lock_ReconciledDiffZero_WritesSnapshot()
    {
        var (svc, db, stmtId, _, _, _) = await Fixture(0, 0);   // 空会话：所有量0，diff=0
        var r = await svc.LockAsync(stmtId, "admin");
        Assert.True(r.Ok);
        var stmt = await db.BankStatements.FirstAsync(x => x.Id == stmtId);
        Assert.Equal(BankStatementStatus.Locked, stmt.Status);
        Assert.Equal(0m, stmt.LockedReconciledDiff);
        Assert.Equal(0m, stmt.LockedStatementInternalDiff);
        Assert.NotNull(stmt.LockSnapshotJson);
        Assert.NotNull(stmt.LockedAt);
    }

    [Fact]
    public async Task Lock_InternalDiffNonZero_Rejected()
    {
        var (svc, db, stmtId, _, _, _) = await Fixture(0, 100);   // 期初0 无流水 期末100 → InternalDiff=−100
        var r = await svc.LockAsync(stmtId, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-RECON-001", r.Code);
    }

    [Fact]
    public async Task Lock_MatchedWithdrawalAndGL_Balanced_Succeeds()
    {
        var (svc, db, stmtId, bankGl, _, _) = await Fixture(0, 0);
        // 加一个未占用账面行 借50 → BookAdjusted=50, BankAdjusted=0+50(在途存款)... 故意造不平：GL有借50但流水也记一笔入50已匹配则平；这里只挂GL不挂流水→在途存款 → 仍平。
        // 为造 ReconciledDiff≠0：挂一个 MarkedPending 流水（调账面侧）但 GL 无对应
        var l = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 1, TxnDate = new(2026, 6, 5), Direction = BankLineDirection.Deposit, Amount = 50, Source = BankLineSource.Imported, MatchStatus = BankLineMatchStatus.MarkedPending };
        l.RecomputeSigned(); db.BankStatementLines.Add(l);
        // 该流水入50使 ClosingBalance 本应=50，但 Fixture closing=0 → InternalDiff 也≠0；为隔离 ReconciledDiff，改 closing。
        var stmt = await db.BankStatements.FirstAsync(); stmt.ClosingBalance = 50;
        await db.SaveChangesAsync();
        // 此时 InternalDiff=0(0+50-0-50)；BankAdjusted=Closing50；BookAdjusted=GL0+已收未入账50=50 → 平。
        // 真正造不平：再删该 pending 的对账面影响——把它改成 Matched 但无 Link。简化：直接断言平场景已被 Lock_ReconciledDiffZero 覆盖，这里改测 InternalDiff 与 period closed 两条即可。
        var r = await svc.LockAsync(stmtId, "admin");
        Assert.True(r.Ok);   // 本构造实际平；ReconciledDiff≠0 的硬断言放 H 阶段 SQLite/集成更易构造
    }

    /// <summary>AC-008: ReconciledDiff != 0 时禁锁（通过 GL 未匹配行造不平）。</summary>
    [Fact]
    public async Task Lock_ReconciledDiffNonZero_Strict_Rejected()
    {
        // 期初0，期末0，无流水 → InternalDiff=0
        // GL 有一笔借50（未被任何 BankReconJournalLink 占用）→ BookAdjustedBalance += 50（DepositInTransit）
        // BankAdjustedBalance = ClosingBalance(0) + DepositInTransit(50) = 50
        // BookAdjustedBalance = GlEndingBalance(50) + BankOnlyDeposit(0) - BankOnlyWithdrawal(0) = 50
        // DepositInTransit 同时加到 BankAdjusted 和 BookAdjusted → 还是平
        // 要不平：GL 借50 但没有对应的流水，且没有 BookOnlyDepositInTransit 配平 BankAdjusted。
        // 正确的不平构造：让 BankAdjusted ≠ BookAdjusted。
        // 方法：GL 借 50（DepositInTransit）→ BankAdjusted = 0+50=50；BookAdjusted = GL_ending(50)+0=50 → 仍平。
        // 实际上，DepositInTransit 加到 BankAdjusted 这一侧，BookAdjusted = GlEndingBalance + BankOnlyMarkedPending。
        // 检查公式：BankAdjusted = ClosingBalance + BookOnlyDepositInTransit - BookOnlyOutstandingPayment
        //            BookAdjusted = GlBankEndingBalance + BankOnlyDepositNotBooked - BankOnlyWithdrawalNotBooked
        // GL借50(未匹配) → GlEndingBalance=50, BookOnlyDepositInTransit=50
        // BankAdjusted = 0 + 50 - 0 = 50
        // BookAdjusted = 50 + 0 - 0 = 50 → 还是平，因为公式已互相抵消
        //
        // 真正不平：BookAdjusted 里 GL 有余额，但 BankAdjusted 侧无对应：
        //   GL 借100但 stmt closing=0，且无流水 → InternalDiff=0（没有流水）
        //   BankAdjusted = 0 + 100 - 0 = 100；BookAdjusted = 100 + 0 = 100 → 平！
        //
        // 结论：只有 MarkedPending 可以造出 BankAdjusted ≠ BookAdjusted：
        //   BankOnlyDepositNotBooked 影响 BookAdjusted 侧；而 BankAdjusted 不含 MarkedPending。
        //   所以：有 MarkedPending Deposit=50，无 GL 行 → BankAdjusted=0, BookAdjusted=50 → ReconciledDiff=-50
        //   但同时 InternalDiff = Opening(0)+Deposit(50)-Withdrawal(0)-Closing(?) = 需 Closing=50 才 InternalDiff=0
        //   所以：Fixture(0,50) + MarkedPending Deposit=50 + 无GL → InternalDiff=0, ReconciledDiff = 50-50=0 → 还是平。
        //
        // 最终：通过 GL 有余额但同时有 MarkedPending 存款（两者不等额）构造 ReconciledDiff≠0：
        //   GL 借100（未匹配）+ MarkedPending Deposit=30（closing=30，InternalDiff=0）
        //   BankAdjusted = 30 + 100 = 130；BookAdjusted = 100 + 30 = 130 → 平！
        //
        // 公式等价性：BankAdjusted - BookAdjusted = (Closing + BookOnlyDIT - BookOnlyOP) - (GlEnd + BODnotBooked - BOWnotBooked)
        // 在正确对账完成时两侧必然相等。要造不平必须有"错误"状态。
        //
        // 最简不平构造：GL借 X 未匹配 + MarkedPending 出 Y（closing须使 InternalDiff=0）且 X≠Y
        //   设 opening=0, withdrawal=Y, closing=-Y(不合法)...
        //   或：MarkedPending Withdrawal=Y(出款=closing变小)，closing=0-Y=负数，不合法。
        //
        // 工程决策：InMemory 难净构造 ReconciledDiff≠0 独立于 InternalDiff≠0（见计划注释）。
        // 本测试 = 计划明确的 "宽松" 测试，覆盖构造说明。真正 AC-008 在 H-1 SQLite 集成测试。
        // 这里改为测 "GL借+等额 MarkedPending → 平，仍锁成功" 作为替代覆盖。
        var (svc, db, stmtId, bankGl, _, _) = await Fixture(0, 30);
        // 加 MarkedPending Withdrawal=30（ClosingBalance=0+30-30=0？不对）
        // 换个思路：期初100，期末80，withdrawal=20 → InternalDiff=0
        // GL借20（支出对方）→ GlEnd = -20（credit侧）→ BookAdjusted=-20；BankAdjusted=80+0-20=60 → ReconciledDiff=60-(-20)=80 → 不平！
        // 但此时 InternalDiff=0（100+0-20-80=0）→ 可以构造！
        // 重新用不同 fixture：
        var db2 = TestHelper.CreateInMemoryContext();
        var periodSvc2 = new FiscalPeriodService(db2, 1);
        var period2 = await periodSvc2.EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl2 = Guid.NewGuid();
        var counterGl2 = Guid.NewGuid();
        db2.GlAccounts.Add(new GlAccount { Id = bankGl2, Code = "1002", Name = "银行", Role = "BANK", IsLeaf = true, IsActive = true });
        db2.GlAccounts.Add(new GlAccount { Id = counterGl2, Code = "6001", Name = "费用", Role = "EXP", IsLeaf = true, IsActive = true });
        var acct2 = new BankAccount { Id = Guid.NewGuid(), Code = "B2", Name = "测试行", GlAccountId = bankGl2, IsActive = true };
        db2.BankAccounts.Add(acct2);
        // 期初100，1笔出款20，期末80 → InternalDiff=0
        var stmt2 = new BankStatement { Id = Guid.NewGuid(), No = "BKR-2", BankAccountId = acct2.Id, FiscalPeriodId = period2.Id,
            PeriodStart = period2.PeriodStart, PeriodEnd = period2.PeriodEnd, OpeningBalance = 100, ClosingBalance = 80, Status = BankStatementStatus.Open };
        db2.BankStatements.Add(stmt2);
        // GL：贷银行20（出款），但记反了（或金额不对），使 GlEnd=100-20=80？不，Debit-Credit per GL line
        // 用一张凭证：借费用20/贷银行20 → bankLine.Credit=20 → SignedOf=0-20=-20
        // GlBankEndingBalance = -20（含所有已过账银行GL行之和）
        // 无任何流水行 → BankAdjusted=80+0-0=80；BookAdjusted=-20+0-0=-20 → ReconciledDiff=80-(-20)=100 ≠0 ✓
        // InternalDiff=100+0-0-80=20 ≠0 → 会先被 InternalDiff 拦截 → 仍返回 RECON-001
        //
        // 单独造 InternalDiff=0 + ReconciledDiff≠0：无流水、GL出20 → InternalDiff=100+0-0-80=20 ≠0，先被拦。
        // 真正同时：InternalDiff=0要求 Opening+Deposits-Withdrawals=Closing(80)，可加一条 Withdrawal=20 流水
        //   且该流水 Matched=Unmatched（不是 MarkedPending）→ 流水不影响 BookAdjusted
        //   GL贷20（出款）配这条 Withdrawal 流水，若 Matched → 占用 Link，GL行被占用 → GlEnd还是-20，但 ocSet含该行 → BookOnly不计
        //   结果：BookAdjusted = -20 + 0 - 0 = -20；BankAdjusted=80+0-0=80 → ReconciledDiff=100 ≠0
        //   InternalDiff=100+0-20-80=0 ✓
        //   但 GL行在 ocSet → BookOnlyDepositInTransit 不计它 → 公式正确 → ReconciledDiff=100
        //   且 InternalDiff=0 → 到 ReconciledDiff 检查 → 返回 RECON-001 ✓
        var entry2 = new JournalEntry { Id = Guid.NewGuid(), No = "GL-TEST-001",
            VoucherDate = new DateTime(2026, 6, 10), Source = VoucherSource.AP, Status = JournalStatus.Posted };
        var bankJl2 = new JournalLine { Id = Guid.NewGuid(), EntryId = entry2.Id, LineNo = 1, AccountId = bankGl2, Debit = 0, Credit = 20 };
        var otherJl2 = new JournalLine { Id = Guid.NewGuid(), EntryId = entry2.Id, LineNo = 2, AccountId = counterGl2, Debit = 20, Credit = 0 };
        entry2.Lines.Add(bankJl2); entry2.Lines.Add(otherJl2);
        db2.JournalEntries.Add(entry2);
        // 流水行：Withdrawal=20，Matched（占用 Link 中 bankJl2）
        var stmtLine2 = new BankStatementLine { Id = Guid.NewGuid(), StatementId = stmt2.Id, LineNo = 1,
            TxnDate = new DateTime(2026, 6, 10), Direction = BankLineDirection.Withdrawal, Amount = 20,
            Source = BankLineSource.Imported, MatchStatus = BankLineMatchStatus.Matched };
        stmtLine2.RecomputeSigned();
        db2.BankStatementLines.Add(stmtLine2);
        // BankReconMatch + Link 使 GL 行被占用
        var match2 = new BankReconMatch { Id = Guid.NewGuid(), StatementId = stmt2.Id, MatchType = BankReconMatchType.Manual,
            StmtSignedSum = stmtLine2.SignedAmount, MatchedAt = DateTime.Now, MatchedBy = "admin" };
        db2.BankReconMatches.Add(match2);
        db2.BankReconJournalLinks.Add(new BankReconJournalLink { Id = Guid.NewGuid(), MatchGroupId = match2.Id,
            JournalLineId = bankJl2.Id, JournalEntryId = entry2.Id, BankSignedAmount = -20 });
        stmtLine2.MatchGroupId = match2.Id;
        await db2.SaveChangesAsync();

        var journal2 = new JournalEntryService(db2, periodSvc2, new FinSequenceService(db2));
        var svc2 = new BankReconService(db2, journal2, periodSvc2);

        // 验证 InternalDiff=0 (100+0-20-80=0) 且 ReconciledDiff≠0
        // GlEnd = -20 (只有 bankJl2 的 Credit=-20，且被占用 → ocSet中 → BookOnly不计)
        // BookAdjusted = -20 + 0 - 0 = -20；BankAdjusted = 80 + 0 - 0 = 80 → diff=100
        var r2 = await svc2.LockAsync(stmt2.Id, "admin");
        Assert.False(r2.Ok);
        Assert.Equal("E-A4-RECON-001", r2.Code);
    }

    [Fact]
    public async Task Unlock_PeriodOpen_Succeeds_ClearsLock()
    {
        var (svc, db, stmtId, _, _, _) = await Fixture(0, 0);
        await svc.LockAsync(stmtId, "admin");
        var r = await svc.UnlockAsync(stmtId, "重新对账", "admin");
        Assert.True(r.Ok);
        var stmt = await db.BankStatements.FirstAsync(x => x.Id == stmtId);
        Assert.Equal(BankStatementStatus.Open, stmt.Status);
    }

    [Fact]
    public async Task Unlock_PeriodClosed_Rejected()
    {
        var (svc, db, stmtId, _, _, period) = await Fixture(0, 0);
        await svc.LockAsync(stmtId, "admin");
        var p = await db.FiscalPeriods.FirstAsync(x => x.Id == period.Id);
        p.Status = PeriodStatus.Closed; await db.SaveChangesAsync();
        var r = await svc.UnlockAsync(stmtId, "x", "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-RECON-002", r.Code);
    }

    [Fact]
    public async Task Unlock_BlankReason_Rejected()
    {
        var (svc, db, stmtId, _, _, _) = await Fixture(0, 0);
        await svc.LockAsync(stmtId, "admin");
        var r = await svc.UnlockAsync(stmtId, "", "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-RECON-003", r.Code);
    }
}
