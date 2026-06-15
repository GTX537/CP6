using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章04 C-3：销售退货红字（接 CreditNote）。红字发票过账生成"借收入+销项税/贷应收"（AR.CreditMemo，
/// 冲减应收）+"借FG/贷COGS"（AR.CogsReversal，货退回入库）；幂等 CreditNoteId；账龄按红字反向。
/// </summary>
public class ArCreditMemoServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(ArInvoiceService svc, GlAccountService gl, ArAgingService aging, Guid tax)> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var tc = new TaxCode { Id = Guid.NewGuid(), Code = "OUT10", Name = "销项10", Rate = 0.10m, Direction = TaxDirection.Output, Recoverable = true };
        db.TaxCodes.Add(tc);
        await db.SaveChangesAsync();
        return (new ArInvoiceService(db, engine, journal, new FinSequenceService(db)), gl, new ArAgingService(db), tc.Id);
    }

    private static FinCreditMemoRequest Req(Guid cnId, Guid tax) => new()
    {
        CreditNoteId = cnId, CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz.AddDays(30), EstimatedCost = 600m,
        Lines = { new FinShipmentInvoiceLine { ItemId = "P1", Qty = 1, UnitPrice = 1000, TaxCodeId = tax } },
    };

    [Fact]
    public async Task CreditMemo_ReversesRevenueAndCost()
    {
        using var db = NewDb();
        var (svc, gl, _, tax) = await SetupAsync(db);
        var cnId = Guid.NewGuid();

        var (r, id, no) = await svc.CreateCreditMemoAsync(Req(cnId, tax), "u");
        Assert.True(r.Ok, r.Code);
        Assert.NotNull(id);

        var ar = await gl.GetByRoleAsync("AR_CONTROL");
        var rev = await gl.GetByRoleAsync("REVENUE");
        var outTax = await gl.GetByRoleAsync("TAX_OUTPUT");
        var cogs = await gl.GetByRoleAsync("COGS");
        var fg = await gl.GetByRoleAsync("FG");

        // 冲收入：借 收入 1000 + 借 销项税 100 / 贷 应收 1100
        var cmEntry = await db.JournalEntries.Include(x => x.Lines).FirstAsync(j => j.Source == VoucherSource.AR && j.SourceDocNo == no);
        Assert.Equal(1000m, cmEntry.Lines.Single(l => l.AccountId == rev!.Id).Debit);
        Assert.Equal(100m, cmEntry.Lines.Single(l => l.AccountId == outTax!.Id).Debit);
        Assert.Equal(1100m, cmEntry.Lines.Single(l => l.AccountId == ar!.Id).Credit);
        Assert.Equal("CUST1", cmEntry.Lines.Single(l => l.AccountId == ar!.Id).PartnerId);

        // 冲成本：借 FG 600 / 贷 COGS 600（货退回入库）
        var costEntry = await db.JournalEntries.Include(x => x.Lines).FirstAsync(j => j.Source == VoucherSource.Cost && j.SourceDocNo == no);
        Assert.Equal(600m, costEntry.Lines.Single(l => l.AccountId == fg!.Id).Debit);
        Assert.Equal(600m, costEntry.Lines.Single(l => l.AccountId == cogs!.Id).Credit);

        var memo = await db.ArInvoices.FindAsync(id);
        Assert.True(memo!.IsCreditMemo);
        Assert.Equal(cnId, memo.CreditNoteId);
    }

    [Fact]
    public async Task CreditMemo_IsIdempotentByCreditNote()
    {
        using var db = NewDb();
        var (svc, _, _, tax) = await SetupAsync(db);
        var cnId = Guid.NewGuid();

        var r1 = await svc.CreateCreditMemoAsync(Req(cnId, tax), "u");
        var r2 = await svc.CreateCreditMemoAsync(Req(cnId, tax), "u");   // 重放同退货单
        Assert.Equal(r1.InvoiceId, r2.InvoiceId);
        Assert.Equal(1, await db.ArInvoices.CountAsync(x => x.CreditNoteId == cnId));
    }

    [Fact]
    public async Task CreditMemo_AgesAsNegative()
    {
        using var db = NewDb();
        var (svc, _, aging, tax) = await SetupAsync(db);

        await svc.CreateCreditMemoAsync(Req(Guid.NewGuid(), tax), "u");

        var rows = await aging.AgingAsync(Biz, "CUST1");
        var row = Assert.Single(rows);
        Assert.Equal(-1100m, row.Total);   // 红字反向，冲减客户应收
    }
}
