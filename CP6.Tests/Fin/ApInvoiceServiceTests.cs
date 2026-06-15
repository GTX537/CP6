using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章03 B-2：应付发票录入（防重 + 行级算税）+ 过账（发 AP.InvoicePosted 事件→引擎生成凭证）。
/// 可抵扣税独立进项税行；不可抵扣并入成本无独立税行。
/// </summary>
public class ApInvoiceServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(ApInvoiceService svc, GlAccountService gl)> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var engine = new AutoVoucherEngine(db,
            new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));
        return (new ApInvoiceService(db, engine, new FinSequenceService(db)), gl);
    }

    private static async Task<Guid> TaxCodeAsync(CP6Context db, decimal rate, bool recoverable)
    {
        var t = new TaxCode
        {
            Id = Guid.NewGuid(), Code = $"IN{rate * 100}{(recoverable ? "R" : "N")}", Name = "进项",
            Rate = rate, Direction = TaxDirection.Input, Recoverable = recoverable,
        };
        db.TaxCodes.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static ApInvoice Invoice(Guid expenseAcc, Guid? taxCodeId, string supInvNo) => new()
    {
        SupplierId = "SUP1", SupplierInvoiceNo = supInvNo, InvoiceDate = Biz, DueDate = Biz.AddDays(30),
        Lines = { new ApInvoiceLine { ItemId = "M1", Qty = 1, UnitPrice = 1000, Amount = 1000, TaxCodeId = taxCodeId, ExpenseAccountId = expenseAcc } },
    };

    [Fact]
    public async Task Create_DuplicateSupplierInvoiceNo_Rejected()
    {
        using var db = NewDb();
        var (svc, gl) = await SetupAsync(db);
        var mat = (await gl.GetByCodeAsync("1401"))!.Id;
        var tax = await TaxCodeAsync(db, 0.10m, recoverable: true);

        Assert.True((await svc.CreateAsync(Invoice(mat, tax, "INV-1"), "u")).Ok);
        var r = await svc.CreateAsync(Invoice(mat, tax, "INV-1"), "u");

        Assert.False(r.Ok);
        Assert.Equal("E-FIN-201", r.Code);
    }

    [Fact]
    public async Task Post_RecoverableTax_GeneratesDebitMaterialPlusInputTax_CreditAp()
    {
        using var db = NewDb();
        var (svc, gl) = await SetupAsync(db);
        var mat = (await gl.GetByCodeAsync("1401"))!.Id;
        var tax = await TaxCodeAsync(db, 0.10m, recoverable: true);

        var inv = Invoice(mat, tax, "INV-2");
        await svc.CreateAsync(inv, "u");
        Assert.Equal(1000m, inv.NetAmount);
        Assert.Equal(100m, inv.TaxAmount);
        Assert.Equal(1100m, inv.GrossAmount);

        var r = await svc.PostAsync(inv.Id, "u");
        Assert.True(r.Ok, r.Code);

        var posted = await db.ApInvoices.FindAsync(inv.Id);
        Assert.Equal(ApInvoiceStatus.Posted, posted!.Status);
        Assert.NotNull(posted.JournalEntryId);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal(1100m, entry.Lines.Sum(l => l.Debit));
        Assert.Equal(1100m, entry.Lines.Sum(l => l.Credit));
        var taxAcc = await gl.GetByRoleAsync("TAX_INPUT");
        Assert.Equal(100m, entry.Lines.Single(l => l.AccountId == taxAcc!.Id).Debit);
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        Assert.Equal("SUP1", entry.Lines.Single(l => l.AccountId == ap!.Id).PartnerId);
    }

    [Fact]
    public async Task Post_NonRecoverableTax_FoldedIntoExpense_NoSeparateTaxLine()
    {
        using var db = NewDb();
        var (svc, gl) = await SetupAsync(db);
        var mat = (await gl.GetByCodeAsync("1401"))!.Id;
        var tax = await TaxCodeAsync(db, 0.10m, recoverable: false);

        var inv = Invoice(mat, tax, "INV-3");
        await svc.CreateAsync(inv, "u");
        Assert.Equal(1100m, inv.NetAmount);     // 税并入成本
        Assert.Equal(0m, inv.TaxAmount);
        Assert.Equal(1100m, inv.GrossAmount);

        var r = await svc.PostAsync(inv.Id, "u");
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var taxAcc = await gl.GetByRoleAsync("TAX_INPUT");
        Assert.DoesNotContain(entry.Lines, l => l.AccountId == taxAcc!.Id);  // 无独立进项税行
        Assert.Equal(1100m, entry.Lines.Single(l => l.Debit > 0).Debit);     // 全进原材料
    }

    [Fact]
    public async Task Post_NonDraft_Rejected()
    {
        using var db = NewDb();
        var (svc, gl) = await SetupAsync(db);
        var mat = (await gl.GetByCodeAsync("1401"))!.Id;

        var inv = Invoice(mat, null, "INV-4");
        await svc.CreateAsync(inv, "u");
        Assert.True((await svc.PostAsync(inv.Id, "u")).Ok);
        var again = await svc.PostAsync(inv.Id, "u");        // 已过账再过账

        Assert.False(again.Ok);
        Assert.Equal("E-FIN-203", again.Code);
    }
}
