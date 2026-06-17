namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 物理出库委托契约（采购侧端口，P2-D3）。外注发支給材时单向同步委托 WMS 出库——
/// 库存唯一真相在 WMS，采购不双写。<see cref="WmsIssueRequest.Purpose"/>=subcontract 让 WMS 把它
/// 和销售出库/生产领料区分开（记为"在外协"而非"已消耗/已卖"）。MVP 用 <see cref="StubWmsIssueService"/> 桩；
/// WMS 落地后以适配器委托真实 OutboundService（与 <see cref="IWmsReceiveService"/>/<see cref="IWmsQcQuery"/> 同族）。
/// </summary>
public interface IWmsIssueService
{
    /// <summary>请 WMS 出库支給材，返回 WMS 出库单号 + 实出数量。</summary>
    Task<WmsIssueResult> IssueAsync(WmsIssueRequest request, string? userName);
}

/// <summary>出库委托请求。</summary>
public class WmsIssueRequest
{
    /// <summary>支給材物料 CD。</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>本次出库量。</summary>
    public decimal Qty { get; set; }

    /// <summary>出库用途（subcontract=外注支給；非销售出库/非生产领料，库存记为"在外协仍属你"）。</summary>
    public string Purpose { get; set; } = "subcontract";

    /// <summary>业务参照号（"{PoNo}-{LineNo}"，WMS 出库挂钩）。</summary>
    public string RefNo { get; set; } = string.Empty;

    /// <summary>发料仓库 CD。</summary>
    public string? WarehouseCd { get; set; }
}

/// <summary>出库委托结果。</summary>
public class WmsIssueResult
{
    /// <summary>WMS 出库单号（回填 PoConsignMaterial.WmsIssueNo）。</summary>
    public string IssueNo { get; set; } = string.Empty;

    /// <summary>实际出库量（可能少于请求量；IssuedQty 按实出累加）。</summary>
    public decimal IssuedQty { get; set; }
}
