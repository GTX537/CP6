namespace CP6.Core.Services.Wms;

/// <summary>
/// ロット追溯サービス（MSBBWM160）
/// </summary>
/// <remarks>
/// 仕様書 §26 ロット追溯・回収管理。
/// 純クエリ：T_StockTransaction の再帰トラバース、専用テーブル不要。
///
/// 追溯方向：
///   順追溯（Forward）：仕入ロット → 製造で使用 → 出荷した顧客一覧
///   逆追溯（Backward）：出荷した製品ロット → 使用した仕入ロット
///   両方向：1 つのロットの上下流すべて
///
/// リコール時：
///   - 順追溯で出荷顧客を特定 → 影響範囲計算
///   - <c>SetRecallFlagAsync</c> で当該ロットの T_Stock.RecallFlag を ON
///   - 以後の引当が自動除外される（OutboundService.AllocateAsync の RecallFlag フィルタ済）
/// </remarks>
public interface ILotTraceService
{
    /// <summary>順追溯：指定ロットがどこに流れたか</summary>
    Task<LotTraceResult> TraceForwardAsync(string productCd, string lotNo);

    /// <summary>逆追溯：指定ロットがどこから来たか</summary>
    Task<LotTraceResult> TraceBackwardAsync(string productCd, string lotNo);

    /// <summary>当該ロットを回収対象としてマーク（T_Stock.RecallFlag = true）</summary>
    Task<int> SetRecallFlagAsync(string productCd, string lotNo, bool flag, string? userName);

    /// <summary>現在の在庫数（複数倉庫合計）</summary>
    Task<LotStockSummary?> GetStockSummaryAsync(string productCd, string lotNo);
}

public class LotTraceResult
{
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    /// <summary>FORWARD / BACKWARD</summary>
    public string Direction { get; set; } = string.Empty;
    /// <summary>関連トランザクション一覧（時系列）</summary>
    public List<LotTraceNode> Nodes { get; set; } = new();
    /// <summary>影響顧客（FORWARD 時のみ。RelatedNo から OutboundOrder → WebOrderNo/CustomerCd 解決）</summary>
    public List<LotAffectedCustomer> AffectedCustomers { get; set; } = new();
    /// <summary>影響仕入先（BACKWARD 時のみ）</summary>
    public List<LotAffectedSupplier> AffectedSuppliers { get; set; } = new();
}

public class LotTraceNode
{
    public string TxnNo { get; set; } = string.Empty;
    public string TxnType { get; set; } = string.Empty;
    public DateTime TxnAt { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remark { get; set; }
}

public class LotAffectedCustomer
{
    public string OutboundNo { get; set; } = string.Empty;
    public string? WebOrderNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public decimal Qty { get; set; }
    public DateTime ShippedAt { get; set; }
}

public class LotAffectedSupplier
{
    public string InboundNo { get; set; } = string.Empty;
    public string? SupplierCd { get; set; }
    public string? SupplierName { get; set; }
    public decimal Qty { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class LotStockSummary
{
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal TotalPhysicalQty { get; set; }
    public decimal TotalAvailableQty { get; set; }
    public int LocationCount { get; set; }
    public bool RecallFlag { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
