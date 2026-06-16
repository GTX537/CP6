namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 检收结果查询契约（采购侧端口，P-D1）。检收基准下 GR 的合格/不良由 WMS 质检裁决，
/// 采购按 WMS 入库号查回判定再回写验收锚。MVP 用 <see cref="StubWmsQcQuery"/> 桩（空结果=全合格）；
/// WMS 落地后以适配器委托真实 QcInspectionService。
/// </summary>
public interface IWmsQcQuery
{
    /// <summary>
    /// 按 WMS 入库号查每 PO 行的检收判定。返回空（或缺某行判定）表示"无质检信息 → 默认全合格"（桩语义）。
    /// </summary>
    Task<List<WmsQcVerdict>> QueryByReceiptAsync(string wmsInboundNo);
}

/// <summary>单 PO 行的检收判定。</summary>
public class WmsQcVerdict
{
    /// <summary>PO 行号。</summary>
    public int PoLineNo { get; set; }

    /// <summary>判定：PASS=合格 / FAIL=不合格 / PENDING=待检（暂不累加验收）。</summary>
    public string QcStatus { get; set; } = "PASS";

    /// <summary>不良量（PASS 时从收货量扣除得合格量；FAIL 时整行不良）。</summary>
    public decimal RejectedQty { get; set; }
}
