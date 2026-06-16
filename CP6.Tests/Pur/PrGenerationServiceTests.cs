using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wms;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购申请需求驱动生成单测（采购 章05 §2/§3）：缺料反流 / 工单 BOM 缺料 → 自动建 PR。
/// 估价取价表/历史；建议供应商按历史；幂等防重复生成（不依赖 MaterialShortage 加字段）。
/// </summary>
public class PrGenerationServiceTests
{
    private const string Sup = "SUP001";
    private const string Item = "MAT001";

    private static PrGenerationService NewSvc(CP6Context db) =>
        new(db, new SupplierPriceService(db), new SeqService(db));

    /// <summary>种子：供应商 + PR 采番配置 + 一档采购价（用于估价）。</summary>
    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = Sup, BpName = "外协甲", SupplierFlg = true,
        });
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0,
        });
        db.SupplierPrices.Add(new SupplierPrice
        {
            SupplierId = Sup, ItemId = Item, Price = 8m, MinQty = 1m, ValidFrom = new DateTime(2026, 1, 1),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<MaterialShortage> AddShortageAsync(CP6Context db, decimal required = 50m, decimal avail = 20m)
    {
        var sh = new MaterialShortage
        {
            WorkOrderNo = "WO20260601-0001",
            ProductCd = Item,
            RequiredQty = required,
            AvailableQty = avail,
            DetectedAt = new DateTime(2026, 6, 1),
            Status = MaterialShortageStatus.Open,
        };
        db.MaterialShortages.Add(sh);
        await db.SaveChangesAsync();
        return sh;
    }

    // ───────────────────────── 缺料反流 ─────────────────────────

    [Fact]
    public async Task GenerateFromShortage_CreatesPr_SourceShortage_WithRefNo()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var sh = await AddShortageAsync(db, required: 50m, avail: 20m);
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromShortageAsync(sh.Id, "u1");

        Assert.NotNull(prNo);
        Assert.StartsWith("PR", prNo);
        var pr = await db.PurchaseRequests.FindAsync(
            (await GetPrIdAsync(db, prNo!)));
        Assert.NotNull(pr);
        Assert.Equal(PrSource.Shortage, pr!.Source);
        Assert.Equal(sh.Id.ToString(), pr.SourceRefNo);

        var line = Assert.Single(GetLines(db, prNo!));
        Assert.Equal(Item, line.ItemId);
        Assert.Equal(30m, line.Qty);                 // 缺口 = 50 − 20
        Assert.Equal(8m, line.EstPrice);             // 估价取价表
        Assert.Equal(Sup, line.SuggestSupplierId);   // 建议供应商按价表历史
    }

    [Fact]
    public async Task GenerateFromShortage_Idempotent_NoDuplicatePr()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var sh = await AddShortageAsync(db);
        var svc = NewSvc(db);

        var first = await svc.GenerateFromShortageAsync(sh.Id, "u1");
        var second = await svc.GenerateFromShortageAsync(sh.Id, "u1");

        Assert.NotNull(first);
        Assert.Null(second);                                   // 已生成 → 防重，返回 null
        Assert.Single(db.PurchaseRequests.ToList());           // 只有一张 PR
    }

    [Fact]
    public async Task GenerateFromShortage_MissingShortage_ReturnsNull()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromShortageAsync(Guid.NewGuid(), "u1");
        Assert.Null(prNo);
    }

    [Fact]
    public async Task GenerateFromShortage_NoPriceTable_EstPriceNull_StillCreates()
    {
        using var db = TestHelper.CreateInMemoryContext();
        // 不种价表，仅采番 + 供应商
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "甲", SupplierFlg = true });
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0,
        });
        await db.SaveChangesAsync();
        var sh = await AddShortageAsync(db, required: 10m, avail: 0m);
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromShortageAsync(sh.Id, "u1");

        Assert.NotNull(prNo);
        var line = Assert.Single(GetLines(db, prNo!));
        Assert.Equal(10m, line.Qty);
        Assert.Null(line.EstPrice);                  // 无价表无历史 → 估价 null
        Assert.Null(line.SuggestSupplierId);         // 无历史 → 不建议供应商
    }

    [Fact]
    public async Task GenerateFromShortage_EstPrice_FallsBackToHistoricalPo()
    {
        using var db = TestHelper.CreateInMemoryContext();
        // 仅供应商 + 采番（无价表），但有历史 PO 行 → 估价回退历史最新 PO 价
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "甲", SupplierFlg = true });
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0,
        });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            PoNo = "PO20260501-0001", SupplierId = Sup, OrderDate = new DateTime(2026, 5, 1), Status = PoStatus.Confirmed,
        });
        db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PoNo = "PO20260501-0001", LineNo = 1, ItemId = Item, Qty = 5m, UnitPrice = 7.25m,
        });
        await db.SaveChangesAsync();
        var sh = await AddShortageAsync(db, required: 10m, avail: 0m);
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromShortageAsync(sh.Id, "u1");

        var line = Assert.Single(GetLines(db, prNo!));
        Assert.Equal(7.25m, line.EstPrice);          // 回退历史 PO 价
        Assert.Equal(Sup, line.SuggestSupplierId);   // 历史 PO 供应商
    }

    // ───────────────────────── 工单 BOM 缺料 ─────────────────────────

    [Fact]
    public async Task GenerateFromWorkOrder_CreatesPr_FromShortMaterials()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        const string wo = "WO20260601-0009";
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = wo, ProcessCd = "P1", MaterialCd = Item,
            PlanQty = 100m, ActualQty = 30m, SupplyStatus = 3,    // 3=不足
        });
        // 已手配的材料不进 PR
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = wo, ProcessCd = "P1", MaterialCd = "MAT002",
            PlanQty = 5m, ActualQty = 5m, SupplyStatus = 2,       // 2=入荷済
        });
        await db.SaveChangesAsync();
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromWorkOrderAsync(wo, "u1");

        Assert.NotNull(prNo);
        var line = Assert.Single(GetLines(db, prNo!));
        Assert.Equal(Item, line.ItemId);
        Assert.Equal(70m, line.Qty);                 // 缺口 = 100 − 30
        Assert.Equal(PrSource.WorkOrder, db.PurchaseRequests.Single().Source);
    }

    [Fact]
    public async Task GenerateFromWorkOrder_NoShortage_ReturnsNull()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        const string wo = "WO20260601-0010";
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = wo, ProcessCd = "P1", MaterialCd = Item,
            PlanQty = 10m, ActualQty = 10m, SupplyStatus = 2,
        });
        await db.SaveChangesAsync();
        var svc = NewSvc(db);

        var prNo = await svc.GenerateFromWorkOrderAsync(wo, "u1");
        Assert.Null(prNo);
    }

    [Fact]
    public async Task GenerateFromWorkOrder_Idempotent_PerMaterial()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        const string wo = "WO20260601-0011";
        db.WorkOrderMaterials.Add(new WorkOrderMaterial
        {
            WorkOrderNo = wo, ProcessCd = "P1", MaterialCd = Item,
            PlanQty = 100m, ActualQty = 30m, SupplyStatus = 3,
        });
        await db.SaveChangesAsync();
        var svc = NewSvc(db);

        var first = await svc.GenerateFromWorkOrderAsync(wo, "u1");
        var second = await svc.GenerateFromWorkOrderAsync(wo, "u1");

        Assert.NotNull(first);
        Assert.Null(second);                             // 同工单同料已开 PR → 防重
        Assert.Single(db.PurchaseRequests.ToList());
    }

    // ───────────────────────── helpers ─────────────────────────

    private static async Task<Guid> GetPrIdAsync(CP6Context db, string prNo)
    {
        var pr = db.PurchaseRequests.First(p => p.PrNo == prNo);
        await Task.CompletedTask;
        return pr.Id;
    }

    private static List<PurchaseRequestLine> GetLines(CP6Context db, string prNo) =>
        db.PurchaseRequestLines.Where(l => l.PrNo == prNo).OrderBy(l => l.LineNo).ToList();
}
