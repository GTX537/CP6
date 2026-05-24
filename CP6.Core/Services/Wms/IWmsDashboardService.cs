namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS ダッシュボード集計サービス（WM-DASH）。
/// </summary>
public interface IWmsDashboardService
{
    Task<WmsKpiDto> GetKpiAsync();
    Task<List<WmsTrendPoint>> GetTransactionTrendAsync(int days = 30);
    Task<List<WmsWarehouseValueDto>> GetWarehouseValueAsync();
    Task<WmsAlertSummaryDto> GetAlertsAsync();
}

public class WmsKpiDto
{
    /// <summary>総在庫金額（円）</summary>
    public decimal TotalStockValue { get; set; }
    /// <summary>在庫品目数（残あり SKU 数）</summary>
    public int ActiveSkuCount { get; set; }
    /// <summary>総物理在庫数</summary>
    public decimal TotalPhysicalQty { get; set; }
    /// <summary>引当中数量合計</summary>
    public decimal TotalAllocatedQty { get; set; }
    /// <summary>滞留品（90日以上動きなし）SKU 数</summary>
    public int StagnantSkuCount { get; set; }
    /// <summary>今日の入庫予定件数（status 1/2）</summary>
    public int TodayInboundPlanCount { get; set; }
    /// <summary>今日の出荷予定件数（OutboundType=2、status 1/2/3）</summary>
    public int TodayShippingPlanCount { get; set; }
    /// <summary>未完了の棚卸件数</summary>
    public int OpenStockTakeCount { get; set; }
}

public class WmsTrendPoint
{
    public string Date { get; set; } = string.Empty; // YYYY-MM-DD
    public decimal InQty { get; set; }
    public decimal OutQty { get; set; }
    public decimal AdjQty { get; set; }
}

public class WmsWarehouseValueDto
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public decimal StockValue { get; set; }
    public int SkuCount { get; set; }
}

public class WmsAlertSummaryDto
{
    public List<ExpiryAlert> ExpiryAlerts { get; set; } = new();
    public List<DelayedInboundAlert> DelayedInbounds { get; set; } = new();
    public int PendingApprovalStockTakeCount { get; set; }
}

public class ExpiryAlert
{
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int DaysUntilExpiry { get; set; }
    public decimal PhysicalQty { get; set; }
}

public class DelayedInboundAlert
{
    public string InboundNo { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public DateTime ExpectedArrivalDate { get; set; }
    public int DelayDays { get; set; }
    public int Status { get; set; }
}
