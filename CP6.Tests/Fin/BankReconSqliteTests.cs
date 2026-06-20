using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public partial class BankReconSqliteTests
{
    [Fact]
    public async Task PostingGuard_LockedAccount_BlocksPosting()
    {
        var db = TestHelper.CreateInMemoryContext();
        var periodSvc = new FiscalPeriodService(db, 1);
        var period = await periodSvc.EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid(); var other = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", IsLeaf = true, IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = other, Code = "6603", Name = "费用", IsLeaf = true, IsActive = true });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, IsActive = true };
        db.BankAccounts.Add(acct);
        db.BankStatements.Add(new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id, PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Status = BankStatementStatus.Locked });
        await db.SaveChangesAsync();
        var entry = new JournalEntry { Id = Guid.NewGuid(), VoucherDate = new(2026, 6, 10), Source = VoucherSource.Manual };
        entry.Lines.Add(new JournalLine { AccountId = bankGl, Debit = 100, LineNo = 1 });
        entry.Lines.Add(new JournalLine { AccountId = other, Credit = 100, LineNo = 2 });
        var r = await BankReconGuard.CheckPostingAsync(db, entry);
        Assert.False(r.Ok);
        Assert.Equal("E-A4-RECON-LOCKED-POSTING", r.Code);
    }

    [Fact]
    public async Task PostingGuard_NonBankEntry_PassesEndToEnd()
    {
        // Arrange: two GL accounts with NO BankAccount mapping
        var db = TestHelper.CreateInMemoryContext();
        var periodSvc = new FiscalPeriodService(db, 1);
        var debitId = Guid.NewGuid(); var creditId = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = debitId, Code = "1122", Name = "应收账款", IsLeaf = true, IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = creditId, Code = "6001", Name = "主营收入", IsLeaf = true, IsActive = true });
        await db.SaveChangesAsync();

        // Use the real JournalEntryService (AutoPostAsync) — exercises the guard hook site
        var journal = new JournalEntryService(db, periodSvc, new FinSequenceService(db));
        var entry = new JournalEntry { Id = Guid.NewGuid(), VoucherDate = new(2026, 6, 10), Source = VoucherSource.AP };
        entry.Lines.Add(new JournalLine { AccountId = debitId, Debit = 200, LineNo = 1 });
        entry.Lines.Add(new JournalLine { AccountId = creditId, Credit = 200, LineNo = 2 });

        // Act: post through the full service (guard is called inside AutoPostAsync)
        var r = await journal.AutoPostAsync(entry);

        // Assert: guard must NOT block a non-bank entry
        Assert.True(r.Ok);
        Assert.Equal(JournalStatus.Posted, entry.Status);
    }

    [Fact]
    public async Task PostingGuard_UnlockedStatement_Passes()
    {
        // Arrange: bank GL + bank account + OPEN (not Locked) statement
        var db = TestHelper.CreateInMemoryContext();
        var periodSvc = new FiscalPeriodService(db, 1);
        var period = await periodSvc.EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid(); var other = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", IsLeaf = true, IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = other, Code = "6603", Name = "费用", IsLeaf = true, IsActive = true });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, IsActive = true };
        db.BankAccounts.Add(acct);
        db.BankStatements.Add(new BankStatement { Id = Guid.NewGuid(), No = "BKR-2", BankAccountId = acct.Id, FiscalPeriodId = period.Id, PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Status = BankStatementStatus.Open });
        await db.SaveChangesAsync();

        var entry = new JournalEntry { Id = Guid.NewGuid(), VoucherDate = new(2026, 6, 10), Source = VoucherSource.Manual };
        entry.Lines.Add(new JournalLine { AccountId = bankGl, Debit = 100, LineNo = 1 });
        entry.Lines.Add(new JournalLine { AccountId = other, Credit = 100, LineNo = 2 });

        // Act: guard called directly, same as the block test but statement is Open
        var r = await BankReconGuard.CheckPostingAsync(db, entry);

        // Assert: open statement must NOT block posting
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task ReversalGuard_LockedReconciledOrigin_BlocksReverse()
    {
        var db = TestHelper.CreateInMemoryContext();
        var periodSvc = new FiscalPeriodService(db, 1);
        var period = await periodSvc.EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var bankGl = Guid.NewGuid();
        db.GlAccounts.Add(new GlAccount { Id = bankGl, Code = "1002", Name = "银行", IsLeaf = true, IsActive = true });
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = bankGl, IsActive = true };
        db.BankAccounts.Add(acct);
        var stmt = new BankStatement { Id = Guid.NewGuid(), No = "BKR-1", BankAccountId = acct.Id, FiscalPeriodId = period.Id, PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Status = BankStatementStatus.Locked };
        db.BankStatements.Add(stmt);
        var origin = new JournalEntry { Id = Guid.NewGuid(), No = "GL-1", VoucherDate = new(2026, 6, 4), Source = VoucherSource.AP, Status = JournalStatus.Posted };
        var bankLine = new JournalLine { Id = Guid.NewGuid(), EntryId = origin.Id, LineNo = 1, AccountId = bankGl, Debit = 100 };
        origin.Lines.Add(bankLine);
        origin.Lines.Add(new JournalLine { Id = Guid.NewGuid(), EntryId = origin.Id, LineNo = 2, AccountId = Guid.NewGuid(), Credit = 100 });
        db.JournalEntries.Add(origin);
        var match = new BankReconMatch { Id = Guid.NewGuid(), StatementId = stmt.Id, MatchType = BankReconMatchType.Manual, MatchedAt = DateTime.Now, MatchedBy = "a" };
        db.BankReconMatches.Add(match);
        db.BankReconJournalLinks.Add(new BankReconJournalLink { Id = Guid.NewGuid(), MatchGroupId = match.Id, JournalLineId = bankLine.Id, JournalEntryId = origin.Id, BankSignedAmount = 100 });
        await db.SaveChangesAsync();

        var loaded = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.Id == origin.Id);
        var r = await BankReconGuard.CheckReversalAsync(db, loaded);
        Assert.False(r.Ok);
        Assert.Equal("E-A4-RECON-LOCKED-REVERSAL", r.Code);
    }
}
