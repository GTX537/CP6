using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 入庫サービス（WM030 + WM040）。
/// 仕様書 §4・§5 参照。
/// </summary>
public interface IInboundService
{
    // ───────── WM030 入庫予定 ─────────

    Task<List<InboundOrder>> SearchOrdersAsync(InboundOrderSearchQuery q);
    Task<InboundOrderDto?> GetOrderAsync(string inboundNo);
    Task<string> CreateOrderAsync(InboundOrderDto dto, string? userName);
    Task UpdateOrderAsync(string inboundNo, InboundOrderDto dto, string? userName);
    Task DeleteOrderAsync(string inboundNo, string? userName);
    Task ConfirmOrderAsync(string inboundNo, string? userName);
    Task CancelOrderAsync(string inboundNo, string? userName);

    // ───────── WM040 入庫実績 ─────────

    Task<List<InboundReceipt>> SearchReceiptsAsync(InboundReceiptSearchQuery q);
    Task<InboundReceiptDto?> GetReceiptAsync(string receiptNo);

    /// <summary>
    /// 入庫実績を登録 → StockMovementService.IN を明細件数分発行 → 在庫反映 + 予定のReceivedQty 累計更新。
    /// </summary>
    /// <returns>採番された ReceiptNo</returns>
    Task<string> ConfirmReceiptAsync(InboundReceiptDto dto, string? userName);
}

// ───────── DTOs ─────────

public class InboundOrderSearchQuery
{
    public string? InboundNo { get; set; }
    public string? SupplierCd { get; set; }
    public string? WarehouseCd { get; set; }
    public int? Status { get; set; }
    public DateTime? ArrivalFrom { get; set; }
    public DateTime? ArrivalTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class InboundReceiptSearchQuery
{
    public string? ReceiptNo { get; set; }
    public string? InboundNo { get; set; }
    public string? WorkOrderNo { get; set; }
    public string? WarehouseCd { get; set; }
    public int? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class InboundOrderDto
{
    public string? InboundNo { get; set; }
    public int InboundType { get; set; } = 1;
    public string? SupplierCd { get; set; }
    public string? SupplierName { get; set; }
    public string? PoNo { get; set; }
    public DateTime ExpectedArrivalDate { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Remarks { get; set; }
    public List<InboundOrderDetailDto> Details { get; set; } = new();
}

public class InboundOrderDetailDto
{
    public int LineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public string? UnitCd { get; set; }
    public string? ExpectedLocationCd { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Remarks { get; set; }
}

public class InboundReceiptDto
{
    public string? ReceiptNo { get; set; }
    public string? InboundNo { get; set; }
    public string SourceType { get; set; } = InboundSourceType.Purchase;
    public string? WorkOrderNo { get; set; }
    public DateTime ReceiveDateTime { get; set; } = DateTime.Now;
    public string? OperatorCd { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Remarks { get; set; }
    public List<InboundReceiptDetailDto> Details { get; set; } = new();
}

public class InboundReceiptDetailDto
{
    public int LineNo { get; set; }
    public int? RefOrderLineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public decimal ReceivedQty { get; set; }
    public string? UnitCd { get; set; }
    public string LocationCd { get; set; } = string.Empty;
    public decimal? UnitPrice { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? PaperRollNo { get; set; }
    public string? StockTxnNo { get; set; }
    public string? Remarks { get; set; }
}
