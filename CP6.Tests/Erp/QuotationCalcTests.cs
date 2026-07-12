using CP6.Core.Services.Erp;
using CP6.Entity.DTOs.Erp;
using Xunit;

namespace CP6.Tests.Erp;

/// <summary>
/// 御見積書 報価計算「钱路」真値回帰（M-ERP 横切 T6 補網）。
///
/// 全局审计 T5/#6：QuotationService の 金額 = 数量 × 単価、合計 = Σ 行金額 の
/// 計算主路径に既存テストがゼロ。既存 <see cref="ErpAuditTests"/> は
/// 「Amount フィールドを直接書換え→審計行が出るか」だけを見ており、
/// 計算式そのものは一度も検証していない（本ファイルと非重複）。
///
/// 断言は全て手算期望値。サービスが誤算すれば真っ赤になる。
/// </summary>
public class QuotationCalcTests
{
    private static QuotationDto NewHeader() => new()
    {
        BaseCd = "B01",
        StaffCd = "S001",
        CustomerCd = "C001",
        CustomerName = "得意先A",
    };

    private static QuotationDetailDto Line(int no, decimal? qty, decimal? unitPrice) => new()
    {
        DetailNo = no,
        ItemName1 = $"品名{no}",
        Quantity = qty,
        UnitPrice = unitPrice,
    };

    // ═════════ ① 正常路径：行金額 = 数量 × 単価、合計 = Σ ═════════

    [Fact]
    public async Task Create_TwoDetails_ComputesLineAmountAndTotal()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new QuotationService(db);

        var dto = NewHeader();
        // 手算：行1 = 12 × 150 = 1800、行2 = 3 × 2000 = 6000、合計 = 7800
        dto.Details.Add(Line(1, 12m, 150m));
        dto.Details.Add(Line(2, 3m, 2000m));

        var qtnNo = await svc.CreateAsync(dto, "alice");

        var saved = await svc.GetByNoAsync(qtnNo);
        Assert.NotNull(saved);
        var d1 = saved!.Details.Single(d => d.DetailNo == 1);
        var d2 = saved.Details.Single(d => d.DetailNo == 2);
        Assert.Equal(1800m, d1.Amount);
        Assert.Equal(6000m, d2.Amount);
        Assert.Equal(7800m, saved.TotalAmount);
    }

    // ═════════ ② 边界：数量 null / 単価 0 の行は金額 0、合計から実質除外 ═════════

    [Fact]
    public async Task Create_NullQtyOrZeroPrice_LineAmountsZero_TotalCountsOnlyRealLines()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new QuotationService(db);

        var dto = NewHeader();
        // 手算：行1 = (null→0) × 500 = 0、行2 = 10 × 0 = 0、行3 = 5 × 100 = 500
        //       合計 = 0 + 0 + 500 = 500
        dto.Details.Add(Line(1, null, 500m));
        dto.Details.Add(Line(2, 10m, 0m));
        dto.Details.Add(Line(3, 5m, 100m));

        var qtnNo = await svc.CreateAsync(dto, "alice");

        var saved = await svc.GetByNoAsync(qtnNo);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved!.Details.Single(d => d.DetailNo == 1).Amount);
        Assert.Equal(0m, saved.Details.Single(d => d.DetailNo == 2).Amount);
        Assert.Equal(500m, saved.Details.Single(d => d.DetailNo == 3).Amount);
        Assert.Equal(500m, saved.TotalAmount);
    }

    // ═════════ ③ 訂正：明細削除＋数量変更で合計を再計算（削除行は合計から除外） ═════════

    [Fact]
    public async Task Update_RemoveDetailAndChangeQty_RecomputesTotal()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new QuotationService(db);

        // 初期：行1 = 10 × 100 = 1000、行2 = 2 × 250 = 500、合計 = 1500
        var create = NewHeader();
        create.Details.Add(Line(1, 10m, 100m));
        create.Details.Add(Line(2, 2m, 250m));
        var qtnNo = await svc.CreateAsync(create, "alice");
        Assert.Equal(1500m, (await svc.GetByNoAsync(qtnNo))!.TotalAmount);

        // 訂正：行2 を削除、行1 の数量 10→8（手算：8 × 100 = 800、合計 = 800）
        var update = NewHeader();
        update.QtnNo = qtnNo;
        update.Details.Add(Line(1, 8m, 100m));
        await svc.UpdateAsync(qtnNo, update, "bob");

        var saved = await svc.GetByNoAsync(qtnNo);
        Assert.NotNull(saved);
        Assert.Single(saved!.Details); // 行2 は論理削除され取得されない
        Assert.Equal(800m, saved.Details.Single(d => d.DetailNo == 1).Amount);
        Assert.Equal(800m, saved.TotalAmount);
    }
}
