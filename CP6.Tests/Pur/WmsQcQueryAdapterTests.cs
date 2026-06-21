using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Wms;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购 WMS 检收查询适配器单测（P-D1 接桩→真实）。
/// 按 InboundNo 查判定済 QcInspection，映射每行判定为 WmsQcVerdict；无记录→空（GR 默认全合格，桩语义保留）。
/// </summary>
public class WmsQcQueryAdapterTests
{
    [Fact]
    public async Task Query_NoInspection_ReturnsEmpty()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var adapter = new WmsQcQueryAdapter(db);

        Assert.Empty(await adapter.QueryByReceiptAsync("IN-NONE"));   // 无质检 → 空（GR 默认全合格）
    }

    [Fact]
    public async Task Query_JudgedInspection_MapsPassFailPending()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.QcInspections.Add(new QcInspection { InspectionNo = "QC1", InboundNo = "IN1", Status = 2, FinalJudgement = "CONDITIONAL" });
        db.QcInspectionItems.Add(new QcInspectionItem { InspectionNo = "QC1", LineNo = 1, ProductCd = "A", ReceivedQty = 100m, AcceptedQty = 90m, RejectedQty = 10m });  // PASS 扣不良
        db.QcInspectionItems.Add(new QcInspectionItem { InspectionNo = "QC1", LineNo = 2, ProductCd = "B", ReceivedQty = 50m, AcceptedQty = 0m, RejectedQty = 50m });    // FAIL 全不良
        db.QcInspectionItems.Add(new QcInspectionItem { InspectionNo = "QC1", LineNo = 3, ProductCd = "C", ReceivedQty = 30m, PendingQty = 30m });                       // PENDING 待检
        await db.SaveChangesAsync();
        var adapter = new WmsQcQueryAdapter(db);

        var verdicts = await adapter.QueryByReceiptAsync("IN1");

        Assert.Equal(3, verdicts.Count);
        var l1 = verdicts.Single(v => v.PoLineNo == 1);
        Assert.Equal("PASS", l1.QcStatus);
        Assert.Equal(10m, l1.RejectedQty);
        Assert.Equal("FAIL", verdicts.Single(v => v.PoLineNo == 2).QcStatus);
        Assert.Equal("PENDING", verdicts.Single(v => v.PoLineNo == 3).QcStatus);
    }

    [Fact]
    public async Task Query_NotJudgedInspection_Ignored()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.QcInspections.Add(new QcInspection { InspectionNo = "QC2", InboundNo = "IN2", Status = 1 });   // 检品中，未判定
        await db.SaveChangesAsync();
        var adapter = new WmsQcQueryAdapter(db);

        Assert.Empty(await adapter.QueryByReceiptAsync("IN2"));   // 未判定 → 空
    }
}
