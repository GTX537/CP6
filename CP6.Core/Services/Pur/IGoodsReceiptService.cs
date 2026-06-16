using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 收货服务（采购 章03）。双基准收货（着荷/检收）+ 委托 WMS 物理入库 + 回写 PO 三累计锚。
/// </summary>
public interface IGoodsReceiptService
{
    /// <summary>取收货单（含行）。</summary>
    Task<GoodsReceipt?> GetAsync(string grNo);

    /// <summary>列出收货单（可按 PO/供应商过滤）。</summary>
    Task<List<GoodsReceipt>> ListAsync(string? poNo = null, string? supplierId = null);

    /// <summary>
    /// 确认收货（章03 §4）：委托 WMS 入库；PO 行 ReceivedQty 累加；
    /// 着荷基准→AcceptedQty 同步累加(免检)；检收基准→只收货 QcStatus=待检；超收挡。回写后刷新 PO 状态。
    /// </summary>
    Task<GoodsReceipt> ConfirmReceiveAsync(GrCreateDto dto, string? userName);

    /// <summary>
    /// 应用检收结果（章03 §5，仅检收基准）：查 WMS 质检→合格累加 PO 行 AcceptedQty / 不良记 RejectedQty；
    /// 全部判定完则收货单完成。回写后刷新 PO 状态。
    /// </summary>
    Task<GoodsReceipt> ApplyQcResultAsync(string grNo, string? userName);
}

/// <summary>确认收货入参。</summary>
public class GrCreateDto
{
    /// <summary>来源采购订单号</summary>
    public string PoNo { get; set; } = string.Empty;

    /// <summary>收货日期（缺省取当天）</summary>
    public DateTime? ReceiptDate { get; set; }

    /// <summary>入库仓库 CD</summary>
    public string? WarehouseCd { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>收货明细</summary>
    public List<GrLineCreateDto> Lines { get; set; } = new();
}

/// <summary>确认收货明细行入参。</summary>
public class GrLineCreateDto
{
    /// <summary>对应 PO 行号</summary>
    public int PoLineNo { get; set; }

    /// <summary>本次收货量</summary>
    public decimal ReceivedQty { get; set; }
}
