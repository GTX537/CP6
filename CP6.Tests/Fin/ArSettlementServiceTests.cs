using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章04 C-3：应收核销（一收款核多发票 + 超额拒）+ 尾差（销售折扣写冲）+ ★已实现汇兑损益（收款汇率vs发票汇率）
/// + 子账↔GL 勾稽（AR 子账未收合计 == GL AR_CONTROL 余额）。镜像 <see cref="ApSettlementServiceTests"/>，方向相反。
/// </summary>
public class ArSettlementServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private sealed class Kit
    {
        public required CP6Context Db;
        public required GlAccountService Gl;
        public required ArInvoiceService Inv;
        public required ReceiptService Rcp;
        public required ArSettlementService Settle;
        public required ArReconcileService Recon;
        public required Guid BankId;
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
            Inv = new ArInvoiceService(db, engine, journal, seq),
            Rcp = new ReceiptService(db, engine, journal, seq),
            Settle = new ArSettlementService(db, journal),
            Recon = new ArReconcileService(db),
            BankId = bank.Id,
        };
    }

    private static async Task<ArInvoice> PostedInvoiceAsync(Kit k, decimal amount, string? ccy = null, decimal rate = 1m)
    {
        var inv = new ArInvoice
        {
            CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz, CurrencyCd = ccy, FxRate = rate,
            Lines = { new ArInvoiceLine { Amount = amount } },   // 无税：Gross=Net=amount
        };
        await k.Inv.CreateAsync(inv, "u");
        await k.Inv.PostAsync(inv.Id, "u");
        return inv;
    }

    private static async Task<Receipt> PostedReceiptAsync(Kit k, decimal amount, string? ccy = null, decimal rate = 1m)
    {
        var rcp = new Receipt { CustomerId = "CUST1", ReceiptDate = Biz, Amount = amount, BankAccountId = k.BankId, CurrencyCd = ccy, FxRate = rate };
        await k.Rcp.ReceiveAsync(rcp, "u");
        return rcp;
    }

    [Fact]
    public async Task Settle_OneReceiptMultipleInvoices_AllSettled()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var i1 = await PostedInvoiceAsync(k, 100m);
        var i2 = await PostedInvoiceAsync(k, 200m);
        var rcp = await PostedReceiptAsync(k, 300m);

        var r = await k.Settle.SettleAsync(rcp.Id, new[]
        {
            new SettlementApply { InvoiceId = i1.Id, AppliedAmount = 100m },
            new SettlementApply { InvoiceId = i2.Id, AppliedAmount = 200m },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ArInvoiceStatus.Settled, (await db.ArInvoices.FindAsync(i1.Id))!.Status);
        Assert.Equal(ArInvoiceStatus.Settled, (await db.ArInvoices.FindAsync(i2.Id))!.Status);
        Assert.Equal(300m, (await db.Receipts.FindAsync(rcp.Id))!.SettledAmount);
        Assert.True((await k.Recon.ReconcileArAsync()).IsMatched);
    }

    [Fact]
    public async Task Settle_OverApplyReceipt_Rejected()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 200m);
        var rcp = await PostedReceiptAsync(k, 100m);

        var r = await k.Settle.SettleAsync(rcp.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 150m },
        }, "u");

        Assert.False(r.Ok);
        Assert.Equal("E-FIN-320", r.Code);
    }

    [Fact]
    public async Task Settle_DifferentCustomer_Rejected()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 100m);
        var rcp = new Receipt { CustomerId = "CUST2", ReceiptDate = Biz, Amount = 100m, BankAccountId = k.BankId };
        await k.Rcp.ReceiveAsync(rcp, "u");

        var r = await k.Settle.SettleAsync(rcp.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 100m },
        }, "u");

        Assert.False(r.Ok);
        Assert.Equal("E-FIN-321", r.Code);
    }

    [Fact]
    public async Task Settle_WithSalesDiscount_WritesOffDiff_SubLedgerMatchesGl()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 10000m);
        var rcp = await PostedReceiptAsync(k, 9998m);
        var fee = (await k.Gl.GetByCodeAsync("6003"))!.Id;   // 财务费用（销售折扣冲销科目）

        var r = await k.Settle.SettleAsync(rcp.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 9998m, DiscountAmount = 2m, DiscountAccountId = fee },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ArInvoiceStatus.Settled, (await db.ArInvoices.FindAsync(inv.Id))!.Status);  // 9998+2 清 10000
        var recon = await k.Recon.ReconcileArAsync();
        Assert.True(recon.IsMatched);
        Assert.Equal(0m, recon.GlBalance);

        // 折扣分录：借 财务费用 2 / 贷 应收 2
        var diff = await db.JournalEntries.Include(x => x.Lines).FirstAsync(e => e.SourceDocNo != null && e.SourceDocNo.Contains("#DIFF"));
        Assert.Equal(2m, diff.Lines.Single(l => l.AccountId == fee).Debit);
    }

    [Fact]
    public async Task Settle_ForeignCurrency_RealizedFxGain_Booked_AndReconciled()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var inv = await PostedInvoiceAsync(k, 100m, ccy: "USD", rate: 150m);   // 应收 15000 JPY
        var rcp = await PostedReceiptAsync(k, 100m, ccy: "USD", rate: 160m);   // 收 16000 JPY（外币升值→收益）

        var r = await k.Settle.SettleAsync(rcp.Id, new[]
        {
            new SettlementApply { InvoiceId = inv.Id, AppliedAmount = 100m },
        }, "u");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(ArInvoiceStatus.Settled, (await db.ArInvoices.FindAsync(inv.Id))!.Status);
        var fxGain = await k.Gl.GetByRoleAsync("FX_GAIN");
        var diff = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(e => e.SourceDocNo != null && e.SourceDocNo.Contains("#DIFF"));
        Assert.Equal(1000m, diff.Lines.Single(l => l.AccountId == fxGain!.Id).Credit);   // 100×(160-150) 汇兑收益
        Assert.True((await k.Recon.ReconcileArAsync()).IsMatched);
    }
}
