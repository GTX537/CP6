using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
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
        var cost = new CostCollectService(db, new FinSequenceService(db));
        return new FinBridgeHook(db, ar, cost, NullLogger<FinBridgeHook>.Instance);
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

    [Fact]
    public async Task OnShipmentConfirmed_WithWorkOrder_UsesRealFgUnitCost_NotEstimate()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);
        // 成本单：实际总成本 1000 / 完工 10 → FG 单位成本 100
        db.CostSheets.Add(new CostSheet { Id = Guid.NewGuid(), No = "CS-X", WorkOrderNo = "WOX", CompletedQty = 10m, MaterialActual = 1000m, Status = CostSheetStatus.Collected });
        await db.SaveChangesAsync();

        var req = new FinShipmentInvoiceRequest
        {
            ShipmentId = "OUT-9", WorkOrderNo = "WOX", CustomerId = "CUST1", InvoiceDate = Biz, DueDate = Biz.AddDays(30),
            EstimatedCost = 700m,   // 应被真实成本覆盖
            Lines = { new FinShipmentInvoiceLine { ItemId = "P1", Qty = 2, UnitPrice = 1000 } },
        };
        var r = await hook.OnShipmentConfirmedAsync(req, "wms");
        Assert.True(r.Success, r.Message);

        var inv = await db.ArInvoices.SingleAsync(x => x.ShipmentId == "OUT-9");
        Assert.Equal(200m, inv.CostAmount);   // FG 单位成本 100 × 出货 2，非估算 700
    }

    [Fact]
    public async Task OnWorkOrderCompleted_AutoCollectsMaterialCost_PersistsEvent()
    {
        using var db = NewDb();
        var hook = await SetupAsync(db);
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WO9", ProductCd = "P1", CompletedQty = 10m, Status = WorkOrderStatus.Completed });
        db.Set<ProductMaterial>().Add(new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", SupplyPrice = 5m });
        db.Set<WorkOrderMaterial>().Add(new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO9", ProcessCd = "OP1", MaterialCd = "M1", PlanQty = 100m, ActualQty = 110m });
        await db.SaveChangesAsync();

        var r = await hook.OnWorkOrderCompletedAsync("WO9", "mes");
        Assert.True(r.Success, r.Message);

        var cs = await db.CostSheets.SingleAsync(x => x.WorkOrderNo == "WO9");
        Assert.Equal(550m, cs.MaterialActual);     // 110×5 料真实消耗
        Assert.Equal(0m, cs.LaborActual);          // 工费留 0 待财务补录
        Assert.Equal(CostSheetStatus.Collected, cs.Status);   // 仅归集未结转
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceModule == "MES" && e.TargetModule == "FIN" && e.SourceNo == "WO9" && e.Status == "SUCCESS"));
    }
}
