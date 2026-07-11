using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章05 A-3：记账规则种子 + 端到端（导 COA → 播规则 → 引擎据真实角色+科目生成平衡凭证）。
/// 含 HeaderAccount 模式（银行行科目来自事件头）与外币换算。
/// </summary>
public class PostingRuleSeedTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static AutoVoucherEngine Engine(CP6Context db) =>
        new(db, new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));

    private static readonly DateTime Biz = new(2026, 6, 15);

    [Fact]
    public void EnsureSeeded_SeedsStandardRules_AndIsIdempotent()
    {
        using var db = NewDb();
        var n1 = PostingRuleSeed.EnsureSeeded(db);
        var n2 = PostingRuleSeed.EnsureSeeded(db);   // 重播

        Assert.Equal(15, n1);
        Assert.Equal(0, n2);                          // ★ 幂等
        Assert.Equal(15, db.PostingRules.Count());
        Assert.All(new[]
            {
                "AP.InvoicePosted", "AP.Payment", "AP.Prepayment", "AP.CreditMemo", "AR.Revenue", "AR.Cogs",
                "AR.Receipt", "AR.Advance", "AR.CreditMemo", "AR.CogsReversal", "Subcontract.CostPosted",
                "Inventory.Received", "Inventory.AdjustGain", "Inventory.AdjustLoss", "Inventory.Scrapped",
            },
            et => Assert.True(db.PostingRules.Any(r => r.EventType == et)));
    }

    [Fact]
    public async Task SeededApInvoiceRule_OverRealCoa_GeneratesBalancedVoucher()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "test");
        PostingRuleSeed.EnsureSeeded(db);

        var mat = await gl.GetByCodeAsync("1401");    // 原材料
        var evt = new FinBizEvent
        {
            EventType = "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = "AP-100",
            BizDate = Biz, PartnerId = "SUP1", Description = "买原料",
        };
        evt.HeaderAmounts["GrossAmount"] = 1100m;
        evt.HeaderAmounts["TaxAmount"] = 100m;
        var dl = new FinBizEventLine();
        dl.Guids["ExpenseAccountId"] = mat!.Id;
        dl.Amounts["Amount"] = 1000m;
        evt.DocLines.Add(dl);

        var r = await Engine(db).GenerateAsync(evt);
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal(1100m, entry.Lines.Sum(l => l.Debit));     // 借 原材料1000 + 进项税100
        Assert.Equal(1100m, entry.Lines.Sum(l => l.Credit));    // 贷 应付1100
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        var apLine = entry.Lines.Single(l => l.AccountId == ap!.Id);
        Assert.Equal(1100m, apLine.Credit);
        Assert.Equal("SUP1", apLine.PartnerId);                 // 带供应商
    }

    [Fact]
    public async Task SeededPaymentRule_HeaderAccount_ResolvesBankFromEvent()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "test");
        PostingRuleSeed.EnsureSeeded(db);

        var bank = await gl.GetByCodeAsync("1002");   // 银行存款（无 Role，付款时按账户带入）
        var evt = new FinBizEvent
        {
            EventType = "AP.Payment", Source = VoucherSource.AP, SourceDocNo = "PAY-1",
            BizDate = Biz, PartnerId = "SUP1", Description = "付款",
        };
        evt.HeaderAmounts["Amount"] = 1100m;
        evt.HeaderGuids["BankGlAccountId"] = bank!.Id;

        var r = await Engine(db).GenerateAsync(evt);
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var bankLine = entry.Lines.Single(l => l.Credit > 0);
        Assert.Equal(bank.Id, bankLine.AccountId);              // ★ 科目来自事件头
        Assert.Equal(1100m, bankLine.Credit);
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        Assert.Equal("SUP1", entry.Lines.Single(l => l.AccountId == ap!.Id).PartnerId);
    }

    [Fact]
    public async Task ForeignCurrencyInvoice_ConvertsToFunctionalAndKeepsOrig()
    {
        using var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "test");
        PostingRuleSeed.EnsureSeeded(db);

        var mat = await gl.GetByCodeAsync("1401");
        var evt = new FinBizEvent
        {
            EventType = "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = "AP-USD",
            BizDate = Biz, PartnerId = "SUP1", CurrencyCd = "USD", FxRate = 150m,   // 1 USD = 150 JPY
        };
        evt.HeaderAmounts["GrossAmount"] = 110m;   // 原币
        evt.HeaderAmounts["TaxAmount"] = 10m;
        var dl = new FinBizEventLine();
        dl.Guids["ExpenseAccountId"] = mat!.Id;
        dl.Amounts["Amount"] = 100m;
        evt.DocLines.Add(dl);

        var r = await Engine(db).GenerateAsync(evt);
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal(16500m, entry.Lines.Sum(l => l.Debit));    // 110 USD × 150 = 16500 JPY
        Assert.Equal(16500m, entry.Lines.Sum(l => l.Credit));
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        var apLine = entry.Lines.Single(l => l.AccountId == ap!.Id);
        Assert.Equal("USD", apLine.CurrencyCd);                 // 原币留痕
        Assert.Equal(150m, apLine.FxRate);
        Assert.Equal(110m, apLine.OrigAmount);
    }
}
