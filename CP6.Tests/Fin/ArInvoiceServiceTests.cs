using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章04 C-2：应收过账双凭证（收入确认 借应收/贷收入+销项税 + 成本结转 借COGS/贷FG）、
/// 出货自动开票幂等（ShipmentId）、出货取消红冲两凭证。
/// </summary>
public class ArInvoiceServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(ArInvoiceService svc, GlAccountService gl, Guid tax)> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var tc = new TaxCode { Id = Guid.NewGuid(), Code = "OUT10", Name = "销项10", Rate = 0.10m, Direction = TaxDirection.Output, Recoverable = true };
        db.TaxCodes.Add(tc);
        await db.SaveChangesAsync();
        return (new ArInvoiceService(db, engine, journal, new FinSequenceService(db)), gl, tc.Id);
    }

    [Fact]
    public async Task Post_GeneratesRevenueAndCostVouchers()
    {
        using var db = NewDb();
        var (svc, gl, tax) = await SetupAsync(db);

        var inv = new ArInvoice
        {
            CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz.AddDays(30), CostAmount = 700m,
            Lines = { new ArInvoiceLine { ItemId = "P1", Qty = 1, UnitPrice = 1000, Amount = 1000, TaxCodeId = tax } },
        };
        Assert.True((await svc.CreateAsync(inv, "u")).Ok);
        Assert.Equal(1100m, inv.GrossAmount);
        Assert.Equal(100m, inv.TaxAmount);

        Assert.True((await svc.PostAsync(inv.Id, "u")).Ok);

        var ar = await gl.GetByRoleAsync("AR_CONTROL");
        var rev = await gl.GetByRoleAsync("REVENUE");
        var outTax = await gl.GetByRoleAsync("TAX_OUTPUT");
        var cogs = await gl.GetByRoleAsync("COGS");
        var fg = await gl.GetByRoleAsync("FG");

        var revenueEntry = await db.JournalEntries.Include(x => x.Lines).FirstAsync(j => j.Source == VoucherSource.AR && j.SourceDocNo == inv.No);
        Assert.Equal(1100m, revenueEntry.Lines.Single(l => l.AccountId == ar!.Id).Debit);
        Assert.Equal("CUST1", revenueEntry.Lines.Single(l => l.AccountId == ar!.Id).PartnerId);
        Assert.Equal(1000m, revenueEntry.Lines.Single(l => l.AccountId == rev!.Id).Credit);
        Assert.Equal(100m, revenueEntry.Lines.Single(l => l.AccountId == outTax!.Id).Credit);

        var costEntry = await db.JournalEntries.Include(x => x.Lines).FirstAsync(j => j.Source == VoucherSource.Cost && j.SourceDocNo == inv.No);
        Assert.Equal(700m, costEntry.Lines.Single(l => l.AccountId == cogs!.Id).Debit);
        Assert.Equal(700m, costEntry.Lines.Single(l => l.AccountId == fg!.Id).Credit);

        var posted = await db.ArInvoices.FindAsync(inv.Id);
        Assert.Equal(ArInvoiceStatus.Posted, posted!.Status);
        Assert.NotNull(posted.JournalEntryId);
        Assert.NotNull(posted.CostJournalEntryId);
    }

    [Fact]
    public async Task CreateFromShipment_PostsAndIsIdempotent()
    {
        using var db = NewDb();
        var (svc, _, tax) = await SetupAsync(db);

        FinShipmentInvoiceRequest Req() => new()
        {
            ShipmentId = "SH-1", OrderId = "ORD-1", CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz.AddDays(30),
            EstimatedCost = 700m,
            Lines = { new FinShipmentInvoiceLine { ItemId = "P1", Qty = 2, UnitPrice = 500, TaxCodeId = tax } },
        };

        var r1 = await svc.CreateFromShipmentAsync(Req(), "wms");
        Assert.True(r1.Result.Ok, r1.Result.Code);
        Assert.NotNull(r1.InvoiceId);

        var r2 = await svc.CreateFromShipmentAsync(Req(), "wms");   // 重放同出货
        Assert.Equal(r1.InvoiceId, r2.InvoiceId);                   // 幂等不重复开票
        Assert.Equal(1, await db.ArInvoices.CountAsync(x => x.ShipmentId == "SH-1"));
        // 收入 + 成本各一张凭证
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.AR));
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
    }

    [Fact]
    public async Task Reverse_ReversesBothVouchers_AndInvoice()
    {
        using var db = NewDb();
        var (svc, _, tax) = await SetupAsync(db);

        var inv = new ArInvoice
        {
            CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz, CostAmount = 700m,
            Lines = { new ArInvoiceLine { Qty = 1, UnitPrice = 1000, Amount = 1000, TaxCodeId = tax } },
        };
        await svc.CreateAsync(inv, "u");
        await svc.PostAsync(inv.Id, "u");
        var revId = inv.JournalEntryId!.Value;
        var costId = inv.CostJournalEntryId!.Value;

        Assert.True((await svc.ReverseAsync(inv.Id, "u", "出货取消")).Ok);

        Assert.Equal(JournalStatus.Reversed, (await db.JournalEntries.FindAsync(revId))!.Status);
        Assert.Equal(JournalStatus.Reversed, (await db.JournalEntries.FindAsync(costId))!.Status);
        Assert.Equal(ArInvoiceStatus.Reversed, (await db.ArInvoices.FindAsync(inv.Id))!.Status);
        Assert.Equal(2, await db.JournalEntries.CountAsync(j => j.ReverseOfId != null));   // 两张红冲凭证
    }
}
