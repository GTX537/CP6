using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 賞味期限管理サービス（MSBBWM170）
/// </summary>
/// <remarks>
/// 仕様書 §27 FEFO + 期限管理。
/// FEFO 引当ロジック自体は <see cref="OutboundService.AllocateAsync"/> で実装済
/// （Stock を ExpiryDate ASC, ReceiveDate ASC で取得）。
/// 本サービスは 期限切迫 Stock の照会と一括廃棄（ADJ -Qty）を提供する。
/// </remarks>
public interface IExpiryService
{
    /// <summary>期限切迫 Stock 一覧（残日数昇順）</summary>
    Task<List<ExpiryStockDto>> GetExpiringStocksAsync(int days = 30, string? warehouseCd = null);

    /// <summary>指定 Stock を廃棄（ADJ -PhysicalQty 発行で在庫除去）</summary>
    Task<int> DisposeAsync(DisposeRequest req, string? userName);
}

public class ExpiryStockDto
{
    public Guid StockId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal? UnitPrice { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int DaysUntilExpiry { get; set; }
    public string? UnitCd { get; set; }
    /// <summary>金額損失（廃棄時の評価減）</summary>
    public decimal? LossAmount { get; set; }
}

public class DisposeRequest
{
    /// <summary>廃棄対象 Stock ID リスト</summary>
    public List<Guid> StockIds { get; set; } = new();
    /// <summary>廃棄理由</summary>
    public string? Reason { get; set; }
}
