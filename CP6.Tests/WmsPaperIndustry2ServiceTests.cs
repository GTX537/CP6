using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
// ERP にも同名 PlateMold があるため WMS 側へ明示エイリアス
using PlateMoldDto = CP6.Core.Services.Wms.PlateMoldDto;
using IPlateMoldService = CP6.Core.Services.Wms.IPlateMoldService;
using PlateMoldService = CP6.Core.Services.Wms.PlateMoldService;

namespace CP6.Tests;

/// <summary>
/// WMS 紙器業特化 第2弾 単体テスト
///
/// テスト観点：
/// Remnant（残材）:
///   1. Create + 状态遷移 0→1→0→2
///   2. Match：素材種別 + 最小サイズで適合検索（昇順）
///   3. 使用済/廃棄は訂正不可
/// PlateMold（印版・木型）:
///   4. RecordUsage 累計加算 + MaxShots 到達で自動 0→2
///   5. メンテ開始/完了サイクル + UsedShots リセット
///   6. WarningList：寿命到達 + 90% 超 を抽出
/// Sample（サンプル）:
///   7. Lend → Return 状态遷移
///   8. Overdue：返却期限超未返却の抽出
///   9. 貸出中削除拒否
/// </summary>
public class WmsPaperIndustry2ServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, WmsSequenceService seq) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        return (db, seq);
    }

    // ═════════ Remnant ═════════

    [Fact]
    public async Task Remnant_FullLifecycle_ShouldTransition()
    {
        var (db, seq) = Create();
        var svc = new RemnantService(db, seq);
        var no = await svc.CreateAsync(new RemnantDto
        {
            MaterialType = "PAPER", WidthMm = 500, LengthMm = 700,
            Quantity = 100m, UnitCd = "SHT",
            WarehouseCd = "W01", LocationCd = "REM-01",
        }, "u");

        // 0 → 1 予約
        await svc.ReserveAsync(no, "WO-2026-001", "u");
        var r = await db.RemnantMaterials.SingleAsync();
        Assert.Equal(RemnantStatus.Reserved, r.Status);
        Assert.Equal("WO-2026-001", r.ReservedFor);

        // 1 → 0 解除
        await svc.UnreserveAsync(no, "u");
        r = await db.RemnantMaterials.SingleAsync();
        Assert.Equal(RemnantStatus.Available, r.Status);
        Assert.Null(r.ReservedFor);

        // 0 → 2 使用済
        await svc.MarkUsedAsync(no, "u");
        Assert.Equal(RemnantStatus.Used, (await db.RemnantMaterials.SingleAsync()).Status);

        // 訂正不可
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateAsync(no, new RemnantDto
            {
                MaterialType = "PAPER", WidthMm = 1, LengthMm = 1, Quantity = 1,
                WarehouseCd = "W01", LocationCd = "X",
            }, "u"));
    }

    [Fact]
    public async Task Remnant_Match_ShouldFilterByTypeAndSize()
    {
        var (db, seq) = Create();
        var svc = new RemnantService(db, seq);
        // 候補3件 + 不利 PAPER小 + FILM
        await svc.CreateAsync(new RemnantDto { MaterialType = "PAPER", WidthMm = 1000, LengthMm = 1500, Quantity = 10, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new RemnantDto { MaterialType = "PAPER", WidthMm = 500, LengthMm = 700, Quantity = 5, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new RemnantDto { MaterialType = "PAPER", WidthMm = 800, LengthMm = 1200, Quantity = 8, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new RemnantDto { MaterialType = "PAPER", WidthMm = 100, LengthMm = 100, Quantity = 1, WarehouseCd = "W01", LocationCd = "L" }, "u");
        await svc.CreateAsync(new RemnantDto { MaterialType = "FILM",  WidthMm = 999, LengthMm = 999, Quantity = 1, WarehouseCd = "W01", LocationCd = "L" }, "u");

        var matches = await svc.MatchAsync("PAPER", 600, 800);
        Assert.Equal(2, matches.Count);
        // 昇順 (小さい順)
        Assert.Equal(800, matches[0].WidthMm);
        Assert.Equal(1000, matches[1].WidthMm);
    }

    // ═════════ PlateMold ═════════

    [Fact]
    public async Task PlateMold_RecordUsage_AutoLifeReached()
    {
        var (db, seq) = Create();
        var svc = new PlateMoldService(db, seq);
        var no = await svc.CreateAsync(new PlateMoldDto
        {
            PlateType = "PLATE", CustomerCd = "C001", ProductCd = "P001",
            MaxShots = 100_000,
        }, "u");

        await svc.RecordUsageAsync(no, 30_000, "u");
        var p = await db.PlateMoldStocks.SingleAsync();
        Assert.Equal(30_000, p.UsedShots);
        Assert.Equal(PlateMoldStatus.Usable, p.Status);

        // 寿命に到達
        await svc.RecordUsageAsync(no, 80_000, "u");
        p = await db.PlateMoldStocks.SingleAsync();
        Assert.Equal(110_000, p.UsedShots);
        Assert.Equal(PlateMoldStatus.LifeReached, p.Status);

        // 寿命到達以降は記録不可
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordUsageAsync(no, 100, "u"));
    }

    [Fact]
    public async Task PlateMold_MaintenanceCycle_ShouldResetCounter()
    {
        var (db, seq) = Create();
        var svc = new PlateMoldService(db, seq);
        var no = await svc.CreateAsync(new PlateMoldDto { PlateType = "PLATE", MaxShots = 1000 }, "u");
        await svc.RecordUsageAsync(no, 800, "u");

        await svc.StartMaintenanceAsync(no, DateTime.Today.AddDays(30), "u");
        Assert.Equal(PlateMoldStatus.Maintenance, (await db.PlateMoldStocks.SingleAsync()).Status);

        await svc.CompleteMaintenanceAsync(no, "u");
        var p = await db.PlateMoldStocks.SingleAsync();
        Assert.Equal(PlateMoldStatus.Usable, p.Status);
        Assert.Equal(0, p.UsedShots); // メンテで使用カウンタリセット
    }

    [Fact]
    public async Task PlateMold_WarningList_ShouldDetectThreshold()
    {
        var (db, seq) = Create();
        var svc = new PlateMoldService(db, seq);
        var n1 = await svc.CreateAsync(new PlateMoldDto { PlateType = "PLATE", MaxShots = 1000 }, "u"); // 95% used → warn
        var n2 = await svc.CreateAsync(new PlateMoldDto { PlateType = "PLATE", MaxShots = 1000 }, "u"); // 50% → not warn
        var n3 = await svc.CreateAsync(new PlateMoldDto { PlateType = "MOLD", MaxShots = null }, "u");  // no max → not warn
        await svc.RecordUsageAsync(n1, 950, "u");
        await svc.RecordUsageAsync(n2, 500, "u");

        var warns = await svc.WarningListAsync(0.9m);
        Assert.Single(warns);
        Assert.Equal(n1, warns[0].PlateNo);
    }

    // ═════════ Sample ═════════

    [Fact]
    public async Task Sample_LendReturnCycle_ShouldTransition()
    {
        var (db, seq) = Create();
        var svc = new SampleStockService(db, seq);
        var no = await svc.CreateAsync(new SampleDto
        {
            SampleType = "PROTO", CustomerCd = "C001",
            Quantity = 1, UnitCd = "PCS",
        }, "u");

        await svc.LendAsync(no, "山田 太郎", DateTime.Today.AddDays(14), "u");
        var s = await db.SampleStocks.SingleAsync();
        Assert.Equal(SampleStatus.LentOut, s.Status);
        Assert.Equal("山田 太郎", s.LentTo);
        Assert.NotNull(s.LentAt);
        Assert.NotNull(s.ExpectedReturnDate);
        Assert.Null(s.ReturnedAt);

        // 貸出中は削除不可
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(no, "u"));

        await svc.ReturnAsync(no, "u");
        s = await db.SampleStocks.SingleAsync();
        Assert.Equal(SampleStatus.Returned, s.Status);
        Assert.NotNull(s.ReturnedAt);

        // 返却済から再貸出も可
        await svc.LendAsync(no, "鈴木", null, "u");
        Assert.Equal(SampleStatus.LentOut, (await db.SampleStocks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Sample_OverdueList_ShouldDetectExpiredLends()
    {
        var (db, seq) = Create();
        var svc = new SampleStockService(db, seq);
        var n1 = await svc.CreateAsync(new SampleDto { SampleType = "PROTO", Quantity = 1 }, "u");
        var n2 = await svc.CreateAsync(new SampleDto { SampleType = "PROTO", Quantity = 1 }, "u");
        var n3 = await svc.CreateAsync(new SampleDto { SampleType = "PROTO", Quantity = 1 }, "u");

        await svc.LendAsync(n1, "x", DateTime.Today.AddDays(-5), "u"); // overdue
        await svc.LendAsync(n2, "y", DateTime.Today.AddDays(7), "u");  // not overdue
        // n3 not lent

        var overdue = await svc.OverdueAsync();
        Assert.Single(overdue);
        Assert.Equal(n1, overdue[0].SampleNo);
    }
}
