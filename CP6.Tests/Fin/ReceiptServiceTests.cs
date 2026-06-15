using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章04 C-3：收款（借银行/贷应收）+ 预收（借银行/贷预收账款 AR_ADVANCE）+ 撤销（解核销还原发票→红冲收款凭证）。
/// 镜像 <see cref="PaymentServiceTests"/>，方向相反。
/// </summary>
public class ReceiptServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(ReceiptService svc, GlAccountService gl, Guid bankId)> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);

        var bankGl = (await gl.GetByCodeAsync("1002"))!;   // 银行存款
        var bank = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "主账户", GlAccountId = bankGl.Id };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        return (new ReceiptService(db, engine, journal, new FinSequenceService(db)), gl, bank.Id);
    }

    private static Receipt Rcp(Guid bankId, bool advance = false) => new()
    {
        CustomerId = "CUST1", ReceiptDate = Biz, Amount = 1100m, BankAccountId = bankId, IsAdvance = advance,
    };

    [Fact]
    public async Task Receive_Normal_DebitsBank_CreditsArControl()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var rcp = Rcp(bankId);
        var r = await svc.ReceiveAsync(rcp, "u");
        Assert.True(r.Ok, r.Code);
        Assert.Equal(ReceiptStatus.Posted, rcp.Status);
        Assert.NotNull(rcp.JournalEntryId);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var ar = await gl.GetByRoleAsync("AR_CONTROL");
        var bankGl = await gl.GetByCodeAsync("1002");
        Assert.Equal(1100m, entry.Lines.Single(l => l.AccountId == bankGl!.Id).Debit);   // 借 银行
        Assert.Equal(1100m, entry.Lines.Single(l => l.AccountId == ar!.Id).Credit);      // 贷 应收
        Assert.Equal("CUST1", entry.Lines.Single(l => l.AccountId == ar!.Id).PartnerId);
    }

    [Fact]
    public async Task Receive_Advance_CreditsAdvanceAccount_NotArControl()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var rcp = Rcp(bankId, advance: true);
        var r = await svc.ReceiveAsync(rcp, "u");
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var advance = await gl.GetByRoleAsync("AR_ADVANCE");
        var ar = await gl.GetByRoleAsync("AR_CONTROL");
        Assert.Equal(1100m, entry.Lines.Single(l => l.Credit > 0).Credit);
        Assert.Equal(advance!.Id, entry.Lines.Single(l => l.Credit > 0).AccountId);       // 贷 预收账款
        Assert.DoesNotContain(entry.Lines, l => l.AccountId == ar!.Id);                    // 不冲应收
    }

    [Fact]
    public async Task ReverseReceipt_RedReversesVoucher_AndSetsReversed()
    {
        using var db = NewDb();
        var (svc, _, bankId) = await SetupAsync(db);

        var rcp = Rcp(bankId);
        await svc.ReceiveAsync(rcp, "u");
        var voucherId = rcp.JournalEntryId!.Value;

        var r = await svc.ReverseReceiptAsync(rcp.Id, "u", "收错了");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ReceiptStatus.Reversed, (await db.Receipts.FindAsync(rcp.Id))!.Status);
        Assert.Equal(JournalStatus.Reversed, (await db.JournalEntries.FindAsync(voucherId))!.Status);
        Assert.True(await db.JournalEntries.AnyAsync(j => j.ReverseOfId == voucherId));    // 红冲凭证已生成
    }

    [Fact]
    public async Task ReverseReceipt_WithSettlement_RestoresInvoiceBalance()
    {
        using var db = NewDb();
        var (svc, _, bankId) = await SetupAsync(db);

        var rcp = Rcp(bankId);
        await svc.ReceiveAsync(rcp, "u");

        // 手工造一张被本收款核销的已过账应收发票
        var inv = new ArInvoice
        {
            Id = Guid.NewGuid(), No = "AR-X", CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz,
            GrossAmount = 1100m, NetAmount = 1100m, SettledAmount = 1100m, Status = ArInvoiceStatus.Settled,
        };
        db.ArInvoices.Add(inv);
        db.ArSettlements.Add(new ArSettlement { Id = Guid.NewGuid(), ReceiptId = rcp.Id, ArInvoiceId = inv.Id, SettledAmount = 1100m });
        rcp.SettledAmount = 1100m;
        await db.SaveChangesAsync();

        var r = await svc.ReverseReceiptAsync(rcp.Id, "u", "撤销");
        Assert.True(r.Ok, r.Code);

        var back = await db.ArInvoices.FindAsync(inv.Id);
        Assert.Equal(0m, back!.SettledAmount);                       // 欠款还原
        Assert.Equal(ArInvoiceStatus.Posted, back.Status);
        Assert.False(await db.ArSettlements.AnyAsync(s => s.ReceiptId == rcp.Id));  // 核销关系清除
    }
}
