using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 检收查询适配器（P-D1 真实实现）。检收基准下，采购按 WMS 入库号查 <c>QcInspection</c> 真实质检判定，
/// 替换 <see cref="StubWmsQcQuery"/>（空=全合格）。无判定済检品记录 → 返回空（GR 仍默认全合格，桩语义保留）。
/// 映射：保留量&gt;0→PENDING / 全不良→FAIL / 否则→PASS（RejectedQty 由 GR 从收货量扣除得合格量）。
/// </summary>
public class WmsQcQueryAdapter : IWmsQcQuery
{
    private const int JudgedStatus = 2;   // QcInspection.Status：0作成 1检品中 2判定済 9取消

    private readonly CP6Context _db;
    public WmsQcQueryAdapter(CP6Context db) => _db = db;

    /// <inheritdoc />
    public async Task<List<WmsQcVerdict>> QueryByReceiptAsync(string wmsInboundNo)
    {
        if (string.IsNullOrWhiteSpace(wmsInboundNo)) return new();

        // 按 InboundNo 查判定済检品（取最新）；无 → 空（GR 默认全合格，桩语义保留）
        var inspection = await _db.QcInspections
            .Where(q => q.InboundNo == wmsInboundNo && q.Status == JudgedStatus && !q.IsDeleted)
            .OrderByDescending(q => q.CreateDate).FirstOrDefaultAsync();
        if (inspection == null) return new();

        var items = await _db.QcInspectionItems
            .Where(i => i.InspectionNo == inspection.InspectionNo && !i.IsDeleted).ToListAsync();

        return items.Select(i => new WmsQcVerdict
        {
            PoLineNo = i.LineNo,
            QcStatus = i.PendingQty > 0 ? "PENDING"                          // 仍有保留 → 待检
                     : i.RejectedQty > 0 && i.AcceptedQty <= 0 ? "FAIL"      // 全不良 → 整行不合格
                     : "PASS",                                                // 否则合格（RejectedQty 由 GR 从收货量扣除）
            RejectedQty = i.RejectedQty,
        }).ToList();
    }
}
