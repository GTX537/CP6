namespace CP6.Entity.DTOs.Wms;

public sealed class CrossDockPageV1
{
    public string Version => "CROSS-DOCK-OWNER/1";
    public string Coverage => "AUTHORIZED_LEGACY_OBSERVATION";
    public bool WritesAvailable => false;
    public string WriteBlocker => "CROSS_DOCK_OWNER_ADOPTION_UNPROVEN";
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<CrossDockObservationV1> Items { get; init; } = [];
}

// Stored legacy values, not accepted staged XDOCK receipts or current quantity cuts.
public sealed class CrossDockObservationV1
{
    public string Version => "CROSS-DOCK-OWNER/1";
    public string Knowledge => "UNKNOWN";
    public string SourceState => "UNRESOLVED_SOURCE";
    public Guid Id { get; init; }
    public string XDockNo { get; init; } = "";
    public string ProductCd { get; init; } = "";
    public string? ProductName { get; init; }
    public string Qty { get; init; } = "";
    public string WarehouseCd { get; init; } = "";
    public string TempLocationCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public int LegacyStatus { get; init; }
    public string? InboundNo { get; init; }
    public string? OutboundNo { get; init; }
    public string? InTxnNo { get; init; }
    public string? OutTxnNo { get; init; }
    public string? FromDock { get; init; }
    public string? ToDock { get; init; }
    public DateTime? ExecutedAt { get; init; }
    public DateTime CreateDate { get; init; }
    public bool SupplierCdReadable { get; init; }
    public bool CustomerCdReadable { get; init; }
    public bool OperatorCdReadable { get; init; }
    public bool RemarksReadable { get; init; }
    public string? SupplierCd { get; init; }
    public string? CustomerCd { get; init; }
    public string? OperatorCd { get; init; }
    public string? Remarks { get; init; }
}
