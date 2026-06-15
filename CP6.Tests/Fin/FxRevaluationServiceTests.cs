using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章07 §4 D-1：期末未实现汇兑重估。reversing 法——本期末按期末汇率重估未结外币 AP/AR，
/// 生成重估凭证（借贷方向：AP 负债 / AR 资产）+ 下期初冲回凭证；本位币跳过；缺汇率跳过；幂等；CloseAsync 集成触发。
/// </summary>
public class FxRevaluationServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db) => new(db, 1);
    private static JournalEntryService Jes(CP6Context db) => new(db, Periods(db), new FinSequenceService(db));
    private static FxRevaluationService Reval(CP6Context db) => new(db, Jes(db), new FxRateService(db));

    private static async Task<GlAccountService> SeedCoaAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        return gl;
    }

    /// <summary>登记某币种期末汇率（RateDate 取月初，确保 ≤ 期末基准日均命中）。</summary>
    private static void SeedRate(CP6Context db, string ccy, decimal rate, int y = 2026, int m = 6)
    {
        db.FxRates.Add(new FxRate { Id = Guid.NewGuid(), CurrencyCd = ccy, RateDate = new DateTime(y, m, 1), Rate = rate });
        db.SaveChanges();
    }

    private static async Task<Guid> JuneAsync(CP6Context db)
        => (await Periods(db).EnsureOpenAsync(new DateTime(2026, 6, 1))).Id;

    private static void AddApInvoice(CP6Context db, decimal gross, decimal settled, decimal rate, string? ccy,
        bool creditMemo = false, ApInvoiceStatus status = ApInvoiceStatus.Posted)
    {
        db.ApInvoices.Add(new ApInvoice
        {
            Id = Guid.NewGuid(), No = $"AP-{Guid.NewGuid():N}".Substring(0, 12),
            SupplierId = "SUP1", InvoiceDate = new DateTime(2026, 6, 10), DueDate = new DateTime(2026, 7, 10),
            CurrencyCd = ccy, FxRate = rate, GrossAmount = gross, SettledAmount = settled,
            IsCreditMemo = creditMemo, Status = status,
        });
        db.SaveChanges();
    }

    private static void AddArInvoice(CP6Context db, decimal gross, decimal settled, decimal rate, string? ccy,
        ArInvoiceStatus status = ArInvoiceStatus.Posted)
    {
        db.ArInvoices.Add(new ArInvoice
        {
            Id = Guid.NewGuid(), No = $"AR-{Guid.NewGuid():N}".Substring(0, 12),
            CustomerId = "CUS1", InvoiceDate = new DateTime(2026, 6, 10), DueDate = new DateTime(2026, 7, 10),
            CurrencyCd = ccy, FxRate = rate, GrossAmount = gross, SettledAmount = settled, Status = status,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Ap_ForeignOpen_UnrealizedGain_DebitsAp_CreditsFxGain_AndReversesNextPeriod()
    {
        using var db = NewDb();
        var gl = await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.6m);                       // 期末汇率
        AddApInvoice(db, gross: 5000m, settled: 0m, rate: 7.0m, ccy: "USD");  // 账面 35000，期末 33000

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);

        var apId = (await gl.GetByRoleAsync("AP_CONTROL"))!.Id;
        var gainId = (await gl.GetByRoleAsync("FX_GAIN"))!.Id;

        var reval = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo == "FXREVAL-202606");
        Assert.Equal(VoucherSource.FxReval, reval.Source);
        Assert.Equal(JournalStatus.Posted, reval.Status);
        Assert.Equal(2000m, reval.Lines.Single(l => l.AccountId == apId).Debit);    // 负债调低 → 借应付
        Assert.Equal("SUP1", reval.Lines.Single(l => l.AccountId == apId).PartnerId);
        Assert.Equal(2000m, reval.Lines.Single(l => l.AccountId == gainId).Credit);  // 未实现收益

        // 下期初冲回（借贷对调）
        var rev = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo == "FXREVAL-202606-REV");
        Assert.Equal(7, rev.VoucherDate.Month);
        Assert.Equal(2000m, rev.Lines.Single(l => l.AccountId == apId).Credit);
        Assert.Equal(2000m, rev.Lines.Single(l => l.AccountId == gainId).Debit);
    }

    [Fact]
    public async Task Ar_ForeignOpen_UnrealizedLoss_CreditsAr_DebitsFxLoss()
    {
        using var db = NewDb();
        var gl = await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.5m);
        AddArInvoice(db, gross: 1000m, settled: 0m, rate: 7.0m, ccy: "USD");  // 账面 7000，期末 6500 → 资产调低 500

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);

        var arId = (await gl.GetByRoleAsync("AR_CONTROL"))!.Id;
        var lossId = (await gl.GetByRoleAsync("FX_LOSS"))!.Id;
        var reval = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo == "FXREVAL-202606");
        Assert.Equal(500m, reval.Lines.Single(l => l.AccountId == arId).Credit);   // 资产调低 → 贷应收
        Assert.Equal("CUS1", reval.Lines.Single(l => l.AccountId == arId).PartnerId);
        Assert.Equal(500m, reval.Lines.Single(l => l.AccountId == lossId).Debit);  // 未实现损失
    }

    [Fact]
    public async Task BaseCurrency_NotRevalued_NoVoucher()
    {
        using var db = NewDb();
        await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        AddApInvoice(db, gross: 9999m, settled: 0m, rate: 1m, ccy: null);    // 本位币(空)
        AddArInvoice(db, gross: 8888m, settled: 0m, rate: 1m, ccy: "JPY");   // 本位币(JPY)

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);
        Assert.False(await db.JournalEntries.AnyAsync());                    // 无外币敞口 → 不生成凭证
    }

    [Fact]
    public async Task MissingPeriodEndRate_SkipsInvoice_NoVoucher()
    {
        using var db = NewDb();
        await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        AddApInvoice(db, gross: 5000m, settled: 0m, rate: 7.0m, ccy: "EUR");  // EUR 期末汇率未登记

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);
        Assert.False(await db.JournalEntries.AnyAsync(e => e.SourceDocNo!.StartsWith("FXREVAL")));
    }

    [Fact]
    public async Task SettledForeignInvoice_NotRevalued()
    {
        using var db = NewDb();
        await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.0m);
        AddApInvoice(db, gross: 5000m, settled: 5000m, rate: 7.0m, ccy: "USD", status: ApInvoiceStatus.Settled);

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);
        Assert.False(await db.JournalEntries.AnyAsync());   // 已结清，开口=0，不重估
    }

    [Fact]
    public async Task Idempotent_SecondCall_NoDoubleVoucher()
    {
        using var db = NewDb();
        await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.6m);
        AddApInvoice(db, gross: 5000m, settled: 0m, rate: 7.0m, ccy: "USD");

        await Reval(db).RevalueAsync(june, "boss");
        await Reval(db).RevalueAsync(june, "boss");   // 第二次应跳过

        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.SourceDocNo == "FXREVAL-202606"));
        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.SourceDocNo == "FXREVAL-202606-REV"));
    }

    [Fact]
    public async Task CreditMemo_ReversesDirection()
    {
        using var db = NewDb();
        var gl = await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.6m);
        // 红字应付（负向负债）：汇率下降时方向与正常发票相反 → 贷应付 / 借汇兑损失
        AddApInvoice(db, gross: 5000m, settled: 0m, rate: 7.0m, ccy: "USD", creditMemo: true);

        var r = await Reval(db).RevalueAsync(june, "boss");
        Assert.True(r.Ok, r.Code);

        var apId = (await gl.GetByRoleAsync("AP_CONTROL"))!.Id;
        var lossId = (await gl.GetByRoleAsync("FX_LOSS"))!.Id;
        var reval = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo == "FXREVAL-202606");
        Assert.Equal(2000m, reval.Lines.Single(l => l.AccountId == apId).Credit);    // 红字反向
        Assert.Equal(2000m, reval.Lines.Single(l => l.AccountId == lossId).Debit);
    }

    [Fact]
    public async Task CloseAsync_WithReval_TriggersRevaluation_AndLocks()
    {
        using var db = NewDb();
        await SeedCoaAsync(db);
        var june = await JuneAsync(db);
        SeedRate(db, "USD", 6.6m);
        AddApInvoice(db, gross: 5000m, settled: 0m, rate: 7.0m, ccy: "USD");

        var close = new PeriodCloseService(db, Periods(db), new TrialBalanceService(db), Reval(db));
        var r = await close.CloseAsync(june, "boss");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(PeriodStatus.Closed, (await db.FiscalPeriods.FindAsync(june))!.Status);
        Assert.True(await db.JournalEntries.AnyAsync(e => e.SourceDocNo == "FXREVAL-202606"));      // 结账触发重估
        Assert.True(await db.JournalEntries.AnyAsync(e => e.SourceDocNo == "FXREVAL-202606-REV"));  // 含下期初冲回
    }
}
