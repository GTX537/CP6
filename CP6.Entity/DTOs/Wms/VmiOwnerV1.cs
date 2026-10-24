namespace CP6.Entity.DTOs.Wms;

public sealed class VmiQueryV1
{
    public string? CustomerCd { get; set; }
    public string? YearMonth { get; set; }
    public bool? Confirmed { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
public sealed class VmiPageV1<T>
{
    public string Version => "VMI-OWNER/1";
    public string Coverage => "AUTHORIZED_LEGACY_OBSERVATION";
    public string Source { get; init; } = "";
    public bool WritesAvailable => false;
    public string WriteBlocker => "VMI_OWNER_ADOPTION_UNPROVEN";
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<T> Items { get; init; } = [];
}
public sealed class VmiCustomerObservationV1
{
    public string CustomerCd { get; init; } = "";
    public string? UnitCd { get; init; }
    public string UnitKnowledge => string.IsNullOrWhiteSpace(UnitCd) ? "UNKNOWN" : "RECORDED";
    public int StockRowCount { get; init; }
    public int SkuCount { get; init; }
    public string? PhysicalQty { get; init; }
    public string? LegacyAllocatedQty { get; init; }
    public string? LegacyAvailableQty { get; init; }
    public string AvailabilityKnowledge => "NOT_EVALUATED";
    public string? Free => null;
    public string ValuationKnowledge => "UNKNOWN";
    public string? EstimatedValue => null;
}
public sealed class VmiStockObservationV1
{
    public Guid Id { get; init; }
    public string CustomerCd { get; init; } = "";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public string WarehouseCd { get; init; } = "";
    public string LocationCd { get; init; } = "";
    public string? UnitCd { get; init; }
    public string PhysicalQty { get; init; } = "";
    public string LegacyAllocatedQty { get; init; } = "";
    public string LegacyAvailableQty { get; init; } = "";
    public string AvailabilityKnowledge => "NOT_EVALUATED";
    public string? Free => null;
    public DateTime? ReceiveDate { get; init; }
    public DateTime? ExpiryDate { get; init; }
}
public sealed class VmiBillingObservationV1
{
    public Guid Id { get; init; }
    public string BillingNo { get; init; } = "";
    public string CustomerCd { get; init; } = "";
    public string? CustomerName { get; init; }
    public bool CustomerNameReadable { get; init; }
    public string YearMonth { get; init; } = "";
    public int LegacySkuCount { get; init; }
    public string BeginQty { get; init; } = "";
    public string EndQty { get; init; } = "";
    public string AvgQty { get; init; } = "";
    public string? DailyStorageRate { get; init; }
    public string? BillingAmount { get; init; }
    public bool RateReadable { get; init; }
    public bool AmountReadable { get; init; }
    public bool LegacyConfirmedFlag { get; init; }
    public DateTime CalculatedAt { get; init; }
    public string? Remarks { get; init; }
    public bool RemarksReadable { get; init; }
    public string LegacySemantics => "POINT_IN_TIME_ESTIMATE";
    public string UnitKnowledge => "UNKNOWN";
    public string CurrencyKnowledge => "UNKNOWN";
    public string FinanceOutcome => "UNKNOWN";
}
