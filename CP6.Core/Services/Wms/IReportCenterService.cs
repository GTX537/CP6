namespace CP6.Core.Services.Wms;

/// <summary>
/// 帳票センターサービス（MSBBWM900）
/// </summary>
/// <remarks>
/// 仕様書 §50。集計レポート用の共通サービス。
/// データ源：T_Stock / T_StockTransaction を唯一の真実源とする。
/// CSV エクスポートは UTF-8 BOM 付（Excel で文字化け回避）。
/// </remarks>
public interface IReportCenterService
{
    /// <summary>在庫月報：年月末時点の在庫スナップショット（製品CD × 倉庫CD ごと）</summary>
    Task<List<MonthlyStockReportRow>> MonthlyStockReportAsync(string yearMonth, string? warehouseCd = null);

    /// <summary>ABC 分析：過去 N 日の出庫トランザクションを集計、80/15/5 ランク付け</summary>
    Task<List<AbcAnalysisRow>> AbcAnalysisAsync(int analysisDays = 90, string? warehouseCd = null);

    /// <summary>滞留品：N 日無動の在庫</summary>
    Task<List<DeadStockRow>> DeadStockAsync(int idleDays = 90, string? warehouseCd = null);

    /// <summary>入庫実績：期間内 IN トランザクション</summary>
    Task<List<InboundHistoryRow>> InboundHistoryAsync(DateTime fromDate, DateTime toDate, string? warehouseCd = null, string? productCd = null);

    /// <summary>出庫実績：期間内 OUT トランザクション</summary>
    Task<List<OutboundHistoryRow>> OutboundHistoryAsync(DateTime fromDate, DateTime toDate, string? warehouseCd = null, string? productCd = null);

    /// <summary>CSV エクスポート（汎用 — 任意の List&lt;T&gt; を CSV bytes へ）</summary>
    byte[] ExportCsv<T>(IEnumerable<T> rows);
}

/// <summary>在庫月報 行</summary>
public class MonthlyStockReportRow
{
    public string YearMonth { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; }
    public decimal AllocatedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal? EstimatedValue { get; set; }
    public int LotCount { get; set; }
}

/// <summary>ABC 分析 行</summary>
public class AbcAnalysisRow
{
    public string ProductCd { get; set; } = string.Empty;
    public int OutCount { get; set; }
    public decimal OutQty { get; set; }
    public decimal? CumulativeRatio { get; set; }
    public string AbcRank { get; set; } = "C";  // A / B / C
}

/// <summary>滞留品 行</summary>
public class DeadStockRow
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; }
    public DateTime? LastMovedAt { get; set; }
    public int IdleDays { get; set; }
    public decimal? EstimatedValue { get; set; }
}

/// <summary>入庫実績 行</summary>
public class InboundHistoryRow
{
    public string TxnNo { get; set; } = string.Empty;
    public DateTime TxnDateTime { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? OperatorCd { get; set; }
}

/// <summary>出庫実績 行</summary>
public class OutboundHistoryRow
{
    public string TxnNo { get; set; } = string.Empty;
    public DateTime TxnDateTime { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? OperatorCd { get; set; }
}
