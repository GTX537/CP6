using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 出庫サービス（WM050 材料出庫 + WM070 出荷 共有 + WM080 梱包）。
/// </summary>
public interface IOutboundService
{
    // ───────── CRUD ─────────
    Task<List<OutboundOrder>> SearchOrdersAsync(OutboundOrderSearchQuery q);
    Task<OutboundOrderDto?> GetOrderAsync(string outboundNo);
    Task<string> CreateOrderAsync(OutboundOrderDto dto, string? userName);
    Task UpdateOrderAsync(string outboundNo, OutboundOrderDto dto, string? userName);
    Task DeleteOrderAsync(string outboundNo, string? userName);

    // ───────── 状態機 ─────────
    Task ConfirmOrderAsync(string outboundNo, string? userName);
    Task CancelOrderAsync(string outboundNo, string? userName);

    /// <summary>
    /// 引当（FIFO + 期限優先）。RSV トランザクションを発行。
    /// 第一版：1 明細 = 1 ロット引当（単一 Stock 行で必要量を確保できないと失敗）。
    /// </summary>
    Task AllocateAsync(string outboundNo, string? userName);

    /// <summary>
    /// ピッキング開始：状态 2 Allocated → 3 Picking。スキャン式作業画面の入口。
    /// </summary>
    Task StartPickingAsync(string outboundNo, string? userName);

    /// <summary>
    /// 出庫確定（OUT トランザクション発行 + ShippingPackage 自動生成（出荷区分時のみ））。
    /// </summary>
    /// <returns>出荷区分の場合は PackageNo、それ以外は null</returns>
    Task<string?> ShipAsync(string outboundNo, ShipRequest req, string? userName);

    // ───────── 自動展開 ─────────

    /// <summary>MES 製造指図 → 材料出庫指示を自動生成（OutboundType=1）。</summary>
    Task<string> CreateFromWorkOrderAsync(string workOrderNo, string? userName);

    /// <summary>PA 受注 → 出荷指示を自動生成（OutboundType=2）。</summary>
    Task<string> CreateFromOrderAsync(string webOrderNo, string? userName);
}

// ───────── DTOs ─────────

public class OutboundOrderSearchQuery
{
    public string? OutboundNo { get; set; }
    public int? OutboundType { get; set; }
    public int? Status { get; set; }
    public string? WorkOrderNo { get; set; }
    public string? WebOrderNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? WarehouseCd { get; set; }
    public DateTime? PlannedFrom { get; set; }
    public DateTime? PlannedTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class OutboundOrderDto
{
    public string? OutboundNo { get; set; }
    public int OutboundType { get; set; } = 1;
    public string? WorkOrderNo { get; set; }
    public string? WebOrderNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; } = DateTime.Today;
    public int Status { get; set; }
    public int Priority { get; set; } = 1;
    public string? ShipToAddress { get; set; }
    public string? CarrierCd { get; set; }
    public string? Remarks { get; set; }
    public List<OutboundOrderDetailDto> Details { get; set; } = new();
}

public class OutboundOrderDetailDto
{
    public int LineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal AllocatedQty { get; set; }
    public decimal ShippedQty { get; set; }
    public string? LotNo { get; set; }
    public string? LocationCd { get; set; }
    public string? UnitCd { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? AllocateTxnNo { get; set; }
    public string? ShipTxnNo { get; set; }
    public string? Remarks { get; set; }
}

public class ShipRequest
{
    /// <summary>ケース数（出荷区分時のみ意味あり）</summary>
    public int CaseQty { get; set; } = 1;
    /// <summary>総重量(kg)</summary>
    public decimal? TotalWeightKg { get; set; }
    /// <summary>総容積(m3)</summary>
    public decimal? TotalVolumeM3 { get; set; }
    /// <summary>配送業者CD</summary>
    public string? CarrierCd { get; set; }
    /// <summary>送り状追跡番号</summary>
    public string? TrackingNo { get; set; }
    /// <summary>備考</summary>
    public string? Remarks { get; set; }
}
