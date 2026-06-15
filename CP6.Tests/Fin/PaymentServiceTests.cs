using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章03 B-3：付款（借应付/贷银行）+ 预付（借预付账款/贷银行）+ 撤销（解核销还原发票→红冲付款凭证）。
/// </summary>
public class PaymentServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(PaymentService svc, GlAccountService gl, Guid bankId)> SetupAsync(CP6Context db)
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

        return (new PaymentService(db, engine, journal, new FinSequenceService(db)), gl, bank.Id);
    }

    private static Payment Pay(Guid bankId, bool prepay = false) => new()
    {
        SupplierId = "SUP1", PayDate = Biz, Amount = 1100m, BankAccountId = bankId, IsPrepayment = prepay,
    };

    [Fact]
    public async Task Pay_Normal_DebitsApControl_CreditsBank()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var p = Pay(bankId);
        var r = await svc.PayAsync(p, "u");
        Assert.True(r.Ok, r.Code);
        Assert.Equal(PaymentStatus.Posted, p.Status);
        Assert.NotNull(p.JournalEntryId);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        var bankGl = await gl.GetByCodeAsync("1002");
        Assert.Equal(1100m, entry.Lines.Single(l => l.AccountId == ap!.Id).Debit);    // 借 应付
        Assert.Equal("SUP1", entry.Lines.Single(l => l.AccountId == ap!.Id).PartnerId);
        Assert.Equal(1100m, entry.Lines.Single(l => l.AccountId == bankGl!.Id).Credit); // 贷 银行
    }

    [Fact]
    public async Task Pay_Prepayment_DebitsPrepaidAccount()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var p = Pay(bankId, prepay: true);
        var r = await svc.PayAsync(p, "u");
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var prepaid = await gl.GetByRoleAsync("AP_PREPAYMENT");
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        Assert.Equal(1100m, entry.Lines.Single(l => l.Debit > 0).Debit);
        Assert.Equal(prepaid!.Id, entry.Lines.Single(l => l.Debit > 0).AccountId);     // 借 预付账款
        Assert.DoesNotContain(entry.Lines, l => l.AccountId == ap!.Id);                 // 不冲应付
    }

    [Fact]
    public async Task ReversePayment_RedReversesVoucher_AndSetsReversed()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var p = Pay(bankId);
        await svc.PayAsync(p, "u");
        var voucherId = p.JournalEntryId!.Value;

        var r = await svc.ReversePaymentAsync(p.Id, "u", "付错了");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(PaymentStatus.Reversed, (await db.Payments.FindAsync(p.Id))!.Status);
        Assert.Equal(JournalStatus.Reversed, (await db.JournalEntries.FindAsync(voucherId))!.Status);
        Assert.True(await db.JournalEntries.AnyAsync(j => j.ReverseOfId == voucherId));  // 红冲凭证已生成
    }

    [Fact]
    public async Task ReversePayment_WithSettlement_RestoresInvoiceBalance()
    {
        using var db = NewDb();
        var (svc, gl, bankId) = await SetupAsync(db);

        var p = Pay(bankId);
        await svc.PayAsync(p, "u");

        // 手工造一张被本付款核销的已过账发票（B-4 核销服务尚未建，此处模拟其结果）
        var inv = new ApInvoice
        {
            Id = Guid.NewGuid(), No = "AP-X", SupplierId = "SUP1", InvoiceDate = Biz, DueDate = Biz,
            GrossAmount = 1100m, NetAmount = 1100m, SettledAmount = 1100m, Status = ApInvoiceStatus.Settled,
        };
        db.ApInvoices.Add(inv);
        db.ApSettlements.Add(new ApSettlement { Id = Guid.NewGuid(), PaymentId = p.Id, ApInvoiceId = inv.Id, SettledAmount = 1100m });
        p.SettledAmount = 1100m;
        await db.SaveChangesAsync();

        var r = await svc.ReversePaymentAsync(p.Id, "u", "撤销");
        Assert.True(r.Ok, r.Code);

        var back = await db.ApInvoices.FindAsync(inv.Id);
        Assert.Equal(0m, back!.SettledAmount);                       // 欠款还原
        Assert.Equal(ApInvoiceStatus.Posted, back.Status);
        Assert.False(await db.ApSettlements.AnyAsync(s => s.PaymentId == p.Id));  // 核销关系清除
    }
}
