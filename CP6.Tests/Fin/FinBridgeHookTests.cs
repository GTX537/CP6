using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章05 §5 / C-2b：FinBridgeHook 出货确认→AR 自动开票（双凭证）+ 幂等 + 出货取消→红冲。
/// </summary>
public class FinBridgeHookTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Biz = new(2026, 6, 15);

    private static async Task<FinBridgeHook> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "t");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var ar = new ArInvoiceService(db, engine, journal, new FinSequenceService(db));
        return new FinBridgeHook(db, ar, NullLogger<FinBridgeHook>.Instance);
    }

    private static FinShipmentInvoiceRequest Req() => new()
    {
        ShipmentId = "OUT-1", OrderId = "ORD-1", CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz.AddDays(30),
        EstimatedCost = 700m,
        Lines = { new FinShipmentInvoiceLine { ItemId = "P1", Qty = 1, UnitPrice = 1000 } },
    };

    [Fact]
    public async Task OnShipmentConfirmed_AutoCreatesInvoice_DualVouchers_PersistsEvent()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);

        var r = await hook.OnShipmentConfirmedAsync(Req(), "wms");
        Assert.True(r.Success, r.Message);

        var inv = await db.ArInvoices.SingleAsync(x => x.ShipmentId == "OUT-1");
        Assert.Equal(ArInvoiceStatus.Posted, inv.Status);
        Assert.NotNull(inv.JournalEntryId);
        Assert.NotNull(inv.CostJournalEntryId);
        // 收入 + 成本各一凭证
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.AR));
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
        // Phase6 事件落库
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.TargetModule == "FIN" && e.SourceNo == "OUT-1" && e.Status == "SUCCESS"));
    }

    [Fact]
    public async Task OnShipmentConfirmed_Idempotent_NoDoubleInvoice()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);

        await hook.OnShipmentConfirmedAsync(Req(), "wms");
        await hook.OnShipmentConfirmedAsync(Req(), "wms");   // 重放同出货

        Assert.Equal(1, await db.ArInvoices.CountAsync(x => x.ShipmentId == "OUT-1"));
    }

    [Fact]
    public async Task OnShipmentCancelled_ReversesInvoice()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);
        await hook.OnShipmentConfirmedAsync(Req(), "wms");

        var r = await hook.OnShipmentCancelledAsync("OUT-1", "wms");
        Assert.True(r.Success, r.Message);

        var inv = await db.ArInvoices.SingleAsync(x => x.ShipmentId == "OUT-1");
        Assert.Equal(ArInvoiceStatus.Reversed, inv.Status);
        Assert.Equal(2, await db.JournalEntries.CountAsync(j => j.ReverseOfId != null));   // 收入+成本各一红冲
    }

    [Fact]
    public async Task OnShipmentCancelled_NoInvoice_Skipped()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);

        var r = await hook.OnShipmentCancelledAsync("NOPE", "wms");
        Assert.False(r.Success);
        Assert.Contains("SKIPPED", r.Message);
    }
}
