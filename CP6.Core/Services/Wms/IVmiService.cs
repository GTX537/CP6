using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// VMI 客先預り在庫サービス（MSBBWM250）
/// </summary>
/// <remarks>
/// 仕様書 §34 客先預り在庫（VMI：Vendor Managed Inventory / 客先預り）。
/// VMI 在庫自体は既存 Stock.OwnerType=CUSTOMER + OwnerCd=客先CD で識別。
/// 本サービスは：
///   1. 客先別 VMI 在庫サマリ
///   2. 月次保管料 計算（バッチ）
/// </remarks>
public interface IVmiService
{
    /// <summary>客先別 VMI 在庫サマリ</summary>
    Task<List<VmiCustomerSummary>> SearchByCustomerAsync(string? customerCd);

    /// <summary>客先 × 製品 詳細在庫</summary>
    Task<List<VmiStockDetail>> GetDetailsAsync(string customerCd);

    /// <summary>請求一覧</summary>
    Task<List<VmiBilling>> SearchBillingsAsync(string? customerCd, string? yearMonth, bool? confirmed);

    /// <summary>
    /// 月次バッチ：指定年月の保管料を計算 → VmiBilling テーブルに upsert
    /// 既存があれば上書き、無ければ追加
    /// </summary>
    Task<int> CalculateMonthlyBillingAsync(string yearMonth, decimal dailyStorageRate, string? userName);

    /// <summary>請求確定（送付完了マーク）</summary>
    Task ConfirmBillingAsync(string billingNo, string? userName);
}

public class VmiCustomerSummary
{
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public int SkuCount { get; set; }
    public decimal TotalPhysicalQty { get; set; }
    public decimal TotalAllocatedQty { get; set; }
    public decimal TotalAvailableQty { get; set; }
    public decimal? EstimatedValue { get; set; }
}

public class VmiStockDetail
{
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; }
    public decimal AvailableQty { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
