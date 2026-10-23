namespace CP6.Entity.DTOs.Wms;

public sealed class LotTraceSnapshotV1
{
    public string Version => "LOT-TRACE-OWNER/1";
    public string Coverage => "AUTHORIZED_LOT_OBSERVATION";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public string Direction { get; init; } = "";
    public bool OperatorReadable { get; init; }
    public bool RemarkReadable { get; init; }
    public bool CustomerNameReadable { get; init; }
    public bool SupplierNameReadable { get; init; }
    public LotStockSnapshotV1? Stock { get; init; }
    public List<LotTraceNodeV1> Nodes { get; init; } = [];
    public List<LotCustomerV1> Customers { get; init; } = [];
    public List<LotSupplierV1> Suppliers { get; init; } = [];
}
public sealed class LotStockSnapshotV1
{
    public string RecallVersion { get; init; } = "";
    public string PhysicalQty { get; init; } = "0";
    public string AvailableQty { get; init; } = "0";
    public int StockCount { get; init; }
    public string RecallState { get; init; } = "NONE";
    public DateTime? ExpiryDate { get; init; }
}
public sealed class LotTraceNodeV1
{
    public string TxnNo { get; init; } = "";
    public string TxnType { get; init; } = "";
    public DateTime TxnAt { get; init; }
    public string WarehouseCd { get; init; } = "";
    public string LocationCd { get; init; } = "";
    public string Qty { get; init; } = "0";
    public string? RelatedNo { get; init; }
    public string? RelatedType { get; init; }
    public string? OperatorCd { get; init; }
    public string? Remark { get; init; }
}
public sealed class LotCustomerV1
{
    public string OutboundNo { get; init; } = "";
    public string? WebOrderNo { get; init; }
    public string? CustomerCd { get; init; }
    public string? CustomerName { get; init; }
    public string Qty { get; init; } = "0";
    public DateTime ShippedAt { get; init; }
}
public sealed class LotSupplierV1
{
    public string ReceiptNo { get; init; } = "";
    public string? InboundNo { get; init; }
    public string? SupplierCd { get; init; }
    public string? SupplierName { get; init; }
    public string Qty { get; init; } = "0";
    public DateTime ReceivedAt { get; init; }
}
public sealed class LotRecallCommandV1
{
    public string ProductCd { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string RecallVersion { get; set; } = "";
    public bool Flag { get; set; }
}
public sealed class LotRecallResultV1
{
    public string Version => "LOT-TRACE-OWNER/1";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public bool Flag { get; init; }
    public int AffectedStocks { get; init; }
}
