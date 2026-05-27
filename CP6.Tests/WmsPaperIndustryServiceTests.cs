using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// WMS 紙器業特化 単体テスト
///
/// テスト観点：
/// PaperRoll:
///   1. Create + Consume 残米減算 + 自動状态遷移
///   2. Match：紙質+巾+流れ+必要長 で適合検索（残米最小優先）
///   3. Slit：巾割り（親→子N+残端）
///   4. 過剰消費は拒否
/// Ink:
///   5. Create + Open 開封状态遷移
///   6. Mix：賞味期限は最早を継承
///   7. RecordMatch + Search 履歴
/// Pallet:
///   8. Create + 状态遷移 0→1→2→3
///   9. 出荷済の訂正拒否
/// Vmi:
///   10. SearchByCustomer 集計
///   11. CalculateMonthlyBilling Upsert
///   12. ConfirmBilling 確定
/// </summary>
public class WmsPaperIndustryServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, WmsSequenceService seq, StockMovementService stock) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return (db, seq, stock);
    }

    // ═════════ PaperRoll ═════════

    [Fact]
    public async Task PaperRoll_CreateAndConsume_ShouldUpdateRemaining()
    {
        var (db, seq, _) = Create();
        var svc = new PaperRollService(db, seq);
        var no = await svc.CreateAsync(new PaperRollDto
        {
            PaperGrade = "K280", WidthMm = 905, BasisWeight = 280, GrainDirection = "T",
            OriginalLengthM = 1000m,
            WarehouseCd = "W01", LocationCd = "PAPER-A-01",
            DisposeThresholdM = 50m,
        }, "u");

        // 消費 300 → 残 700
        await svc.ConsumeAsync(no, 300m, "u");
        var roll = await db.PaperRolls.SingleAsync();
        Assert.Equal(700m, roll.RemainingLengthM);
        Assert.Equal(PaperRollStatus.InUse, roll.Status); // 在庫→使用中

        // 消費 660 → 残 40（閾値以下 → 残米状态に）
        await svc.ConsumeAsync(no, 660m, "u");
        roll = await db.PaperRolls.SingleAsync();
        Assert.Equal(40m, roll.RemainingLengthM);
        Assert.Equal(PaperRollStatus.Remnant, roll.Status);
    }

    [Fact]
    public async Task PaperRoll_OverConsume_ShouldThrow()
    {
        var (db, seq, _) = Create();
        var svc = new PaperRollService(db, seq);
        var no = await svc.CreateAsync(new PaperRollDto
        {
            PaperGrade = "K", WidthMm = 100, OriginalLengthM = 100,
            WarehouseCd = "W01", LocationCd = "L",
        }, "u");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(no, 200m, "u"));
    }

    [Fact]
    public async Task PaperRoll_Match_ShouldPreferSmallestRemainingFit()
    {
        var (db, seq, _) = Create();
        var svc = new PaperRollService(db, seq);
        // 3 ロール：K280 巾 905 T 目、残米 1000 / 500 / 300
        await svc.CreateAsync(new PaperRollDto { PaperGrade = "K280", WidthMm = 905, GrainDirection = "T", OriginalLengthM = 1000, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new PaperRollDto { PaperGrade = "K280", WidthMm = 905, GrainDirection = "T", OriginalLengthM = 500, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new PaperRollDto { PaperGrade = "K280", WidthMm = 905, GrainDirection = "T", OriginalLengthM = 300, WarehouseCd = "W01", LocationCd = "L" }, "u");

        // 必要 250m → 残 300 のロールが選ばれる（端材最小）
        var matches = await svc.MatchAsync("K280", 905, "T", 250m);
        Assert.True(matches.Count >= 1);
        Assert.Equal(300m, matches[0].RemainingLengthM);
    }

    [Fact]
    public async Task PaperRoll_Slit_ShouldCreateChildrenAndDisposeParent()
    {
        var (db, seq, _) = Create();
        var svc = new PaperRollService(db, seq);
        var parent = await svc.CreateAsync(new PaperRollDto
        {
            PaperGrade = "K", WidthMm = 1310, OriginalLengthM = 500,
            GrainDirection = "T", WarehouseCd = "W01", LocationCd = "L",
        }, "u");

        // 1310 → 905 + 390 + 残端 15
        var children = await svc.SlitAsync(new SlitRequest
        {
            ParentRollNo = parent,
            ChildWidths = new() { 905, 390 },
            KeepRemnant = true,
        }, "u");

        Assert.Equal(3, children.Count); // 子2 + 残端1
        var allRolls = await db.PaperRolls.ToListAsync();
        Assert.Equal(4, allRolls.Count); // 親 + 子2 + 残端
        // 親は廃棄状态
        var p = allRolls.Single(r => r.RollNo == parent);
        Assert.Equal(PaperRollStatus.Disposed, p.Status);
        Assert.Equal(0m, p.RemainingLengthM);
        // 残端は Remnant 状态
        var remnant = allRolls.Single(r => r.ParentRollNo == parent && r.WidthMm == 15);
        Assert.Equal(PaperRollStatus.Remnant, remnant.Status);
    }

    // ═════════ Ink ═════════

    [Fact]
    public async Task Ink_CreateAndOpen_ShouldChangeOpenStatus()
    {
        var (db, seq, _) = Create();
        var svc = new InkService(db, seq);
        var no = await svc.CreateLotAsync(new InkLotDto
        {
            ColorCode = "DIC-100", InkType = "OFFSET",
            ExpiryDate = DateTime.Today.AddDays(180),
            Quantity = 10m, UnitCd = "kg",
        }, "u");

        var lot = await db.InkLots.SingleAsync();
        Assert.Equal(InkOpenStatus.Unopened, lot.OpenStatus);

        await svc.OpenLotAsync(no, DateTime.Today.AddDays(30), "u");
        var afterOpen = await db.InkLots.SingleAsync();
        Assert.Equal(InkOpenStatus.Opened, afterOpen.OpenStatus);
        Assert.Equal(DateTime.Today.AddDays(30), afterOpen.ExpiryDate);
    }

    [Fact]
    public async Task Ink_Mix_ShouldInheritEarliestExpiry()
    {
        var (db, seq, _) = Create();
        var svc = new InkService(db, seq);
        var a = await svc.CreateLotAsync(new InkLotDto
        {
            ColorCode = "DIC-100", ExpiryDate = DateTime.Today.AddDays(100), Quantity = 5,
        }, "u");
        var b = await svc.CreateLotAsync(new InkLotDto
        {
            ColorCode = "DIC-100", ExpiryDate = DateTime.Today.AddDays(50), Quantity = 8,
        }, "u");

        var newNo = await svc.MixLotsAsync(new MixInkRequest
        {
            ParentLotNoA = a, ParentQtyA = 3,
            ParentLotNoB = b, ParentQtyB = 4,
        }, "u");

        var newLot = await db.InkLots.SingleAsync(x => x.InkLotNo == newNo);
        Assert.Equal(7m, newLot.Quantity); // 3+4
        Assert.Equal(DateTime.Today.AddDays(50), newLot.ExpiryDate); // 早い方
        Assert.Equal(a, newLot.ParentLotNoA);
        Assert.Equal(b, newLot.ParentLotNoB);
        // 親が減算された
        var aAfter = await db.InkLots.SingleAsync(x => x.InkLotNo == a);
        Assert.Equal(2m, aAfter.Quantity); // 5-3
    }

    [Fact]
    public async Task Ink_RecordAndSearchMatch_ShouldFilterByCustomerAndColor()
    {
        var (db, seq, _) = Create();
        var svc = new InkService(db, seq);
        await svc.RecordMatchAsync(new InkColorMatchDto
        {
            CustomerCd = "C001", ColorCode = "DIC-100",
            FormulaJson = "{\"DIC-100\":0.5}", ConsumedQty = 5,
        }, "u");
        await svc.RecordMatchAsync(new InkColorMatchDto
        {
            CustomerCd = "C002", ColorCode = "DIC-200", ConsumedQty = 3,
        }, "u");

        var byCust = await svc.SearchMatchesAsync("C001", null);
        Assert.Single(byCust);
        Assert.Equal("DIC-100", byCust[0].ColorCode);
    }

    // ═════════ Pallet ═════════

    [Fact]
    public async Task Pallet_StatusTransition_BuildingToShipped()
    {
        var (db, seq, _) = Create();
        var svc = new PalletService(db, seq);
        var no = await svc.CreateAsync(new PalletDto
        {
            ProductCd = "P1", LotNo = "L1", CartonQty = 50,
            WarehouseCd = "W01", LocationCd = "RACK-A-01",
        }, "u");

        var p0 = await db.Pallets.SingleAsync();
        Assert.Equal(PalletStatus.Building, p0.Status);

        await svc.CompleteBuildingAsync(no, "u");
        Assert.Equal(PalletStatus.InStock, (await db.Pallets.SingleAsync()).Status);

        await svc.MoveToShippingWaitAsync(no, "DOCK-WAIT-01", "u");
        var p2 = await db.Pallets.SingleAsync();
        Assert.Equal(PalletStatus.WaitingShip, p2.Status);
        Assert.Equal("DOCK-WAIT-01", p2.LocationCd);

        await svc.MarkShippedAsync(no, "OUT001", "u");
        var p3 = await db.Pallets.SingleAsync();
        Assert.Equal(PalletStatus.Shipped, p3.Status);
        Assert.Equal("OUT001", p3.ShippedOutboundNo);
    }

    [Fact]
    public async Task Pallet_UpdateShipped_ShouldThrow()
    {
        var (db, seq, _) = Create();
        var svc = new PalletService(db, seq);
        var no = await svc.CreateAsync(new PalletDto
        {
            ProductCd = "P1", LotNo = "L1", CartonQty = 10, WarehouseCd = "W01", LocationCd = "L",
        }, "u");
        await svc.CompleteBuildingAsync(no, "u");
        await svc.MarkShippedAsync(no, "OUT001", "u");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync(no, new PalletDto
        {
            ProductCd = "P1", LotNo = "L1", CartonQty = 5, WarehouseCd = "W01", LocationCd = "L",
        }, "u"));
    }

    // ═════════ VMI ═════════

    [Fact]
    public async Task Vmi_SearchByCustomer_ShouldAggregate()
    {
        var (db, seq, stock) = Create();
        // 客先 CUST_A の VMI 在庫 2 SKU
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 100,
            OwnerType = StockOwnerType.Customer, OwnerCd = "CUST_A",
            UnitPrice = 10m,
        });
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L02",
            ProductCd = "P2", LotNo = "L", Qty = 50,
            OwnerType = StockOwnerType.Customer, OwnerCd = "CUST_A",
            UnitPrice = 5m,
        });
        // 自社在庫（除外されるはず）
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L03",
            ProductCd = "P3", LotNo = "L", Qty = 200,
        });

        var svc = new VmiService(db, seq);
        var sums = await svc.SearchByCustomerAsync(null);
        Assert.Single(sums);
        Assert.Equal("CUST_A", sums[0].CustomerCd);
        Assert.Equal(2, sums[0].SkuCount);
        Assert.Equal(150m, sums[0].TotalPhysicalQty);
        Assert.Equal(1250m, sums[0].EstimatedValue); // 100*10 + 50*5
    }

    [Fact]
    public async Task Vmi_CalculateMonthlyBilling_UpsertAndConfirm()
    {
        var (db, seq, stock) = Create();
        await stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN, WarehouseCd = "W01", LocationCd = "L01",
            ProductCd = "P1", LotNo = "L", Qty = 100,
            OwnerType = StockOwnerType.Customer, OwnerCd = "CUST_A",
        });

        var svc = new VmiService(db, seq);
        var n1 = await svc.CalculateMonthlyBillingAsync("202605", 0.5m, "u");
        Assert.Equal(1, n1);

        var bill = await db.VmiBillings.SingleAsync();
        Assert.Equal("CUST_A", bill.CustomerCd);
        Assert.Equal("202605", bill.YearMonth);
        Assert.Equal(0.5m * 100m * 31m, bill.BillingAmount); // 0.5 × 100 × 31日
        Assert.False(bill.Confirmed);

        // 2 回目 = Upsert（同じ行を更新）
        var n2 = await svc.CalculateMonthlyBillingAsync("202605", 1.0m, "u");
        Assert.Equal(1, n2);
        var bill2 = await db.VmiBillings.SingleAsync();
        Assert.Equal(1.0m * 100m * 31m, bill2.BillingAmount);

        // 確定後は更新スキップ
        await svc.ConfirmBillingAsync(bill2.BillingNo, "u");
        await svc.CalculateMonthlyBillingAsync("202605", 99m, "u");
        var bill3 = await db.VmiBillings.SingleAsync();
        Assert.Equal(1.0m * 100m * 31m, bill3.BillingAmount); // 99 で上書きされない
        Assert.True(bill3.Confirmed);
    }
}
