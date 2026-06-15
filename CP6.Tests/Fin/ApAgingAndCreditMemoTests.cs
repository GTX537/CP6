using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章03 B-5（后端）：账龄分桶 + 供应商红字（借应付/贷费用+进项转出，对账仍平）+ IFinAp 采购建票幂等。
/// </summary>
public class ApAgingAndCreditMemoTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<(ApInvoiceService inv, ApAgingService aging, ApReconcileService recon, GlAccountService gl, Guid mat)> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var engine = new AutoVoucherEngine(db,
            new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));
        var inv = new ApInvoiceService(db, engine, new FinSequenceService(db));
        return (inv, new ApAgingService(db), new ApReconcileService(db), gl, (await gl.GetByCodeAsync("1401"))!.Id);
    }

    private static async Task PostInvoiceAsync(ApInvoiceService svc, Guid mat, decimal amount, string no, DateTime due)
    {
        var inv = new ApInvoice
        {
            SupplierId = "SUP1", SupplierInvoiceNo = no, InvoiceDate = Biz, DueDate = due,
            Lines = { new ApInvoiceLine { Amount = amount, ExpenseAccountId = mat } },
        };
        await svc.CreateAsync(inv, "u");
        await svc.PostAsync(inv.Id, "u");
    }

    [Fact]
    public async Task Aging_BucketsByDueDate()
    {
        using var db = NewDb();
        var (svc, aging, _, _, mat) = await SetupAsync(db);
        await PostInvoiceAsync(svc, mat, 100m, "A", Biz.AddDays(10));    // 未到期
        await PostInvoiceAsync(svc, mat, 200m, "B", Biz.AddDays(-15));   // 逾期 15 → 1-30
        await PostInvoiceAsync(svc, mat, 400m, "C", Biz.AddDays(-45));   // 逾期 45 → 31-60
        await PostInvoiceAsync(svc, mat, 800m, "D", Biz.AddDays(-90));   // 逾期 90 → 60+

        var rows = await aging.AgingAsync(Biz);
        var row = Assert.Single(rows);
        Assert.Equal(100m, row.NotDue);
        Assert.Equal(200m, row.Days1To30);
        Assert.Equal(400m, row.Days31To60);
        Assert.Equal(800m, row.Days60Plus);
        Assert.Equal(1400m, row.Overdue);   // 200+400+800
        Assert.Equal(1500m, row.Total);     // +100 未到期
    }

    [Fact]
    public async Task CreditMemo_Posts_DebitAp_CreditExpenseTax_ReconcileMatched()
    {
        using var db = NewDb();
        var (svc, _, recon, gl, mat) = await SetupAsync(db);

        // 正常发票 1000 已过账
        await PostInvoiceAsync(svc, mat, 1000m, "N-1", Biz.AddDays(30));

        // 供应商红字 300（IsCreditMemo），无税
        var cm = new ApInvoice
        {
            SupplierId = "SUP1", IsCreditMemo = true, InvoiceDate = Biz, DueDate = Biz,
            Lines = { new ApInvoiceLine { Amount = 300m, ExpenseAccountId = mat } },
        };
        Assert.True((await svc.CreateAsync(cm, "u")).Ok);
        Assert.True((await svc.PostAsync(cm.Id, "u")).Ok, "credit memo post");

        var entry = await db.JournalEntries.Include(x => x.Lines)
            .FirstAsync(e => e.SourceDocNo == cm.No);
        var ap = await gl.GetByRoleAsync("AP_CONTROL");
        Assert.Equal(300m, entry.Lines.Single(l => l.AccountId == ap!.Id).Debit);   // 借 应付（红字冲减）
        Assert.Equal(300m, entry.Lines.Single(l => l.AccountId == mat).Credit);     // 贷 原材料转出

        var r = await recon.ReconcileApAsync();
        Assert.True(r.IsMatched, $"sub={r.SubLedger} gl={r.GlBalance}");
        Assert.Equal(700m, r.GlBalance);   // 1000 − 300
    }

    [Fact]
    public async Task FinAp_CreateInvoiceFromPurchase_Posts_AndIsIdempotent()
    {
        using var db = NewDb();
        var (svc, _, _, gl, mat) = await SetupAsync(db);
        IFinAp finAp = svc;

        var req = new FinApInvoiceRequest
        {
            SupplierId = "SUP1", SupplierInvoiceNo = "PO-INV-1", InvoiceDate = Biz, DueDate = Biz.AddDays(30),
            PurchaseOrderId = Guid.NewGuid(),
            Lines = { new FinApInvoiceRequestLine { ItemId = "M1", Qty = 2, UnitPrice = 500, ExpenseAccountId = mat } },
        };

        var r1 = await finAp.CreateInvoiceFromPurchaseAsync(req, "po-bot");
        Assert.True(r1.Result.Ok, r1.Result.Code);
        Assert.NotNull(r1.InvoiceId);
        var inv = await db.ApInvoices.FindAsync(r1.InvoiceId!.Value);
        Assert.Equal(ApInvoiceStatus.Posted, inv!.Status);
        Assert.Equal(1000m, inv.GrossAmount);                       // 2 × 500
        Assert.Equal(inv.PurchaseOrderId, req.PurchaseOrderId);     // 采购单号预留回填

        var r2 = await finAp.CreateInvoiceFromPurchaseAsync(req, "po-bot");   // 重放
        Assert.Equal(r1.InvoiceId, r2.InvoiceId);                   // 幂等：同一张
        Assert.Equal(1, await db.ApInvoices.CountAsync(x => x.SupplierInvoiceNo == "PO-INV-1"));
    }
}
