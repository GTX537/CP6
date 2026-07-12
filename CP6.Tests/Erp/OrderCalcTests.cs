using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;
using Xunit;

namespace CP6.Tests.Erp;

/// <summary>
/// Web 受注 建单算价「钱路」真値回帰（M-ERP 横切 T6 補網）。
///
/// 全局审计 T5/#6：受注金額の計算主路径に既存テストがゼロ。
///  ① 価格来源：SalesPriceDiv=="1"（個別売）→ 個別単価、それ以外（セット売）→ セット単価。
///  ② 金額 = 数量 × 選ばれた単価（<see cref="OrderService.CalcAmountAsync"/>）。
///  ③ 単価訂正保存（BatchUpdatePriceAsync）：金額再計算 + 承認差戻し
///     + セット単価の同一受注内一括伝播（明細横断の汇总）。
/// 既存 <see cref="ErpAuditTests"/>（Amount 直書換→審計行）/
/// <see cref="CP6.Tests.OrderServiceCancelTests"/>（取消状態機）とは非重複。
///
/// 断言は全て手算期望値。
/// </summary>
public class OrderCalcTests
{
    /// <summary>WF 起票は連携環境無し前提の No-Op（起票成功したことにする）。</summary>
    private sealed class FakePowerEgg : IPowerEggWorkflowService
    {
        public int Calls { get; private set; }
        public Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? applicantStaffCd, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }

    private static OrderService NewSvc(CP6Context db, IPowerEggWorkflowService? pe = null)
        => new(db, pe ?? new FakePowerEgg(), new NoOpWmsBridgeHook());

    private static OrderDetail SeedDetail(CP6Context db, string webOrderNo, int no,
        decimal? qty, string? salesPriceDiv, decimal? setPrice = null, decimal? amount = null)
    {
        var d = new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = no,
            ProductCd = $"P{no:D3}",
            Quantity = qty,
            SalesPriceDiv = salesPriceDiv,
            SetUnitPrice = setPrice,
            Amount = amount,
            ApprovalStatus = 0,
        };
        db.OrderDetails.Add(d);
        db.SaveChanges();
        return d;
    }

    // ═════════ ① 価格来源＝個別売（SalesPriceDiv="1"）→ 個別単価を採用 ═════════

    [Fact]
    public async Task CalcAmount_IndividualSalesDiv_UsesIndividualPrice()
    {
        using var db = TestHelper.CreateInMemoryContext();
        SeedDetail(db, "WO-IND", 1, qty: 100m, salesPriceDiv: "1");
        var svc = NewSvc(db);

        // 手算：個別売 → 100 × 25.5 = 2550（セット単価 999 は無視される）
        var amount = await svc.CalcAmountAsync("WO-IND", 1, newIndPrice: 25.5m, newSetPrice: 999m);

        Assert.Equal(2550m, amount);
    }

    // ═════════ ② 価格来源＝セット売（SalesPriceDiv≠"1"）→ セット単価を採用 ═════════

    [Fact]
    public async Task CalcAmount_SetSalesDiv_UsesSetPrice()
    {
        using var db = TestHelper.CreateInMemoryContext();
        SeedDetail(db, "WO-SET", 1, qty: 100m, salesPriceDiv: "2");
        var svc = NewSvc(db);

        // 手算：セット売 → 100 × 30 = 3000（個別単価 25.5 は無視される）
        var amount = await svc.CalcAmountAsync("WO-SET", 1, newIndPrice: 25.5m, newSetPrice: 30m);

        Assert.Equal(3000m, amount);
    }

    // ═════════ ③ 边界：明細不在→0、単価 null→数量×0=0 ═════════

    [Fact]
    public async Task CalcAmount_MissingDetailOrNullPrice_ReturnsZero()
    {
        using var db = TestHelper.CreateInMemoryContext();
        SeedDetail(db, "WO-B", 1, qty: 100m, salesPriceDiv: "2");
        var svc = NewSvc(db);

        // 明細不在（誤 NO）→ 0
        Assert.Equal(0m, await svc.CalcAmountAsync("WO-B", 999, newIndPrice: 10m, newSetPrice: 10m));
        // セット売で セット単価 null → 100 × 0 = 0
        Assert.Equal(0m, await svc.CalcAmountAsync("WO-B", 1, newIndPrice: 77m, newSetPrice: null));
    }

    // ═════════ ④ 単価訂正バッチ保存：各行の金額再計算（個別売）+ 承認差戻し集計 ═════════
    // 個別売（div="1"）路径のみ検証。セット単価一括伝播は ExecuteUpdate（relational 専用、
    // InMemory 非対応）に依存するため本ユニットテストの対象外（concerns 記載）。

    [Fact]
    public async Task BatchUpdatePrice_MultiLine_RecomputesEachAmount_AndAggregatesWfReset()
    {
        using var db = TestHelper.CreateInMemoryContext();
        // 同一受注 WO-BATCH に 2 明細（共に個別売、個別単価未設定）
        SeedDetail(db, "WO-BATCH", 1, qty: 100m, salesPriceDiv: "1", amount: 0m);
        SeedDetail(db, "WO-BATCH", 2, qty: 40m, salesPriceDiv: "1", amount: 0m);
        var pe = new FakePowerEgg();
        var svc = NewSvc(db, pe);

        var req = new OrderPriceCorrectionBatchUpdateDto
        {
            Items =
            {
                new OrderPriceCorrectionUpdateDto
                {
                    WebOrderNo = "WO-BATCH", WebOrderDetailNo = 1,
                    IndividualUnitPriceAfter = 25m, PriceChangeReason = "客先交渉",
                },
                new OrderPriceCorrectionUpdateDto
                {
                    WebOrderNo = "WO-BATCH", WebOrderDetailNo = 2,
                    IndividualUnitPriceAfter = 10m, PriceChangeReason = "客先交渉",
                },
            },
        };

        var result = await svc.BatchUpdatePriceAsync(req, "bob");

        // 集計：2 件更新、両行とも単価変更ありなので WF 2 件起票
        Assert.Equal(2, result.UpdatedCount);
        Assert.Equal(2, result.WfRequestedCount);
        Assert.Empty(result.ConflictedKeys);
        Assert.Equal(2, pe.Calls);

        // 手算：明細1 = 100 × 25 = 2500、明細2 = 40 × 10 = 400（各行独立に個別単価で再計算）
        var l1 = db.OrderDetails.Single(x => x.WebOrderNo == "WO-BATCH" && x.WebOrderDetailNo == 1);
        var l2 = db.OrderDetails.Single(x => x.WebOrderNo == "WO-BATCH" && x.WebOrderDetailNo == 2);
        Assert.Equal(2500m, l1.Amount);
        Assert.Equal(25m, l1.IndividualUnitPrice);
        Assert.Equal(1, l1.ApprovalStatus); // 単価変更 → 承認差戻し(=1)
        Assert.Equal(400m, l2.Amount);
        Assert.Equal(10m, l2.IndividualUnitPrice);
    }
}
