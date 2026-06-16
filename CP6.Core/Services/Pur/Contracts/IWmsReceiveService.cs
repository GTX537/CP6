namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 物理入库委托契约（采购侧端口，P-D1）。采购 GR 确认时单向同步委托 WMS 落物理库存，
/// 库存唯一真相在 WMS——采购不双写。MVP 用 <see cref="StubWmsReceiveService"/> 桩（返回假入库号）；
/// WMS 落地后以适配器委托真实 InboundService（WMS InboundOrder 已留 PoNo 钩子）。
/// </summary>
public interface IWmsReceiveService
{
    /// <summary>请 WMS 入库，返回 WMS 入库单号 + 行引用。</summary>
    Task<WmsReceiveResult> ReceiveAsync(WmsReceiveRequest request, string? userName);
}

/// <summary>入库委托请求。</summary>
public class WmsReceiveRequest
{
    /// <summary>采购订单号（WMS 入库挂 PoNo 钩子）。</summary>
    public string PoNo { get; set; } = string.Empty;

    /// <summary>供应商 CD。</summary>
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>入库仓库 CD。</summary>
    public string? WarehouseCd { get; set; }

    /// <summary>入库明细。</summary>
    public List<WmsReceiveLine> Lines { get; set; } = new();
}

/// <summary>入库委托明细。</summary>
public class WmsReceiveLine
{
    /// <summary>对应 PO 行号。</summary>
    public int PoLineNo { get; set; }

    /// <summary>物料 CD。</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>本次入库量。</summary>
    public decimal Qty { get; set; }
}

/// <summary>入库委托结果。</summary>
public class WmsReceiveResult
{
    /// <summary>WMS 入库单号（回填 GR.WmsInboundNo）。</summary>
    public string WmsInboundNo { get; set; } = string.Empty;

    /// <summary>行引用（PO 行号 → WMS 入库明细引用）。</summary>
    public List<WmsReceiveLineRef> LineRefs { get; set; } = new();
}

/// <summary>入库行引用。</summary>
public class WmsReceiveLineRef
{
    /// <summary>PO 行号。</summary>
    public int PoLineNo { get; set; }

    /// <summary>WMS 入库明细引用键。</summary>
    public string WmsReceiptDetailRef { get; set; } = string.Empty;
}
