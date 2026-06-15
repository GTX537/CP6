using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章03 B-4：核销（一付款核多发票 + 超额拒）+ 尾差（现金折扣写冲）+ ★已实现汇兑损益（发票汇率vs付款汇率）
/// + 子账↔GL 勾稽（AP 子账未付合计 == GL AP_CONTROL 余额）。
/// </summary>
public class ApSettlementServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private sealed class Kit
    {
        public required CP6Context Db;
        public required GlAccountService Gl;
        public required ApInvoiceService Inv;
        public required PaymentService Pay;
        public required ApSettlementService Settle;
        public required ApReconcileService Recon;
        public required Guid BankId;
        public required Guid MatId;
    }

    private static async Task<Kit> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var seq = new FinSequenceService(db);

        var bankGl = (await gl.GetByCodeAsync("1002"))!;
        var bank = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "主账户", GlAccountId = bankGl.Id };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        return new Kit
        {
            Db = db, Gl = gl,
            Inv = new ApInvoiceService(db, engine, seq),
            Pay = new PaymentService(db, engine, journal, seq),
            Settle = new ApSettlementService(db, journal),
            Recon = new ApReconcileService(db),
            BankId = bank.Id,
            MatId = (await gl.GetByCodeAsync("1401"))!.Id,
        };
    }

    private static async Task<ApInvoice> PostedInvoiceAsync(Kit k, decimal amount, string supInvNo, string? ccy = null, decimal rate = 1m)
    {
        var inv = new ApInvoice
        {
            SupplierId = "SUP1", SupplierInvoiceNo = supInvNo, InvoiceDate = Biz, DueDate = Biz,
            CurrencyCd = ccy, FxRate = rate,
            Lines = { new ApInvoiceLine { Amount = amount, ExpenseAccountId = k.MatId } },
        };
        await k.Inv.CreateAsync(inv, "u");
        await k.Inv.PostAsync(inv.Id, "u");
        return inv;
    }

    private static async Task<Payment> PostedPaymentAsync(Kit k, decimal amount, string? ccy = null, decimal rate = 1m)
    {
        var p = new Payment { SupplierId = "SUP1", PayDate = Biz, Amount = amount, BankAccountId = k.BankId, CurrencyCd = ccy, FxRate = rate };
        await k.Pay.PayAsync(p, "u");
        return p;
    }

    [Fact]
    public async Task Settle_OnePaymentMultipleInvoices_AllSettled()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var i1 = await PostedInvoiceAsync(k, 100m, "M-1");
        var i2 = await PostedInvoiceAsync(k, 200m, "M-2");
        var pay = await PostedPaymentAsync(k, 300m);

        var r = await k.Settle.SettleAsync(pay.Id, new[]
        {
            new SettlementApply { InvoiceId = i1.Id, AppliedAmount = 100m },
            new SettlementApply { InvoiceId = i2.Id, AppliedAmount = 200m },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ApInvoiceStatus.Settled, (await db.ApInvoices.FindAsync(i1.Id))!.Status);
        Assert.Equal(ApInvoiceStatus.Settled, (await db.ApInvoices.FindAsync(i2.Id))!.Status);
        Assert.Equal(300m, (await db.Payments.FindAsync(pay.Id))!.SettledAmount);
        Assert.True((await k.Recon.ReconcileApAsync()).IsMatched);
    }

    [Fact]
    public async Task Settle_OverApplyPayment_Rejected()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 200m, "O-1");
        var pay = await PostedPaymentAsync(k, 100m);

        var r = await k.Settle.SettleAsync(pay.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 150m },
        }, "u");

        Assert.False(r.Ok);
        Assert.Equal("E-FIN-220", r.Code);
    }

    [Fact]
    public async Task Settle_WithCashDiscount_WritesOffDiff_SubLedgerMatchesGl()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 10000m, "C-1");
        var pay = await PostedPaymentAsync(k, 9998m);
        var fee = (await k.Gl.GetByCodeAsync("6003"))!.Id;   // 财务费用

        var r = await k.Settle.SettleAsync(pay.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 9998m, DiscountAmount = 2m, DiscountAccountId = fee },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ApInvoiceStatus.Settled, (await db.ApInvoices.FindAsync(inv.Id))!.Status);  // 9998+2 清 10000
        var recon = await k.Recon.ReconcileApAsync();
        Assert.True(recon.IsMatched);
        Assert.Equal(0m, recon.GlBalance);
    }

    [Fact]
    public async Task Settle_ForeignCurrency_RealizedFxGain_Booked_AndReconciled()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 100m, "F-1", ccy: "USD", rate: 150m);   // 应付 15000 JPY
        var pay = await PostedPaymentAsync(k, 100m, ccy: "USD", rate: 140m);          // 付 14000 JPY

        var r = await k.Settle.SettleAsync(pay.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 100m },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ApInvoiceStatus.Settled, (await db.ApInvoices.FindAsync(inv.Id))!.Status);
        var fxGain = await k.Gl.GetByRoleAsync("FX_GAIN");
        var diff = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(e => e.SourceDocNo != null && e.SourceDocNo.Contains("#DIFF"));
        Assert.Equal(1000m, diff.Lines.Single(l => l.AccountId == fxGain!.Id).Credit);   // 100×(150-140) 汇兑收益
        Assert.True((await k.Recon.ReconcileApAsync()).IsMatched);
    }
}
