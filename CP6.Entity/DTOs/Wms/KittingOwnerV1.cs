namespace CP6.Entity.DTOs.Wms;

public sealed class KitQueryV1
{
    public string? Keyword { get; set; }
    public string? WarehouseCd { get; set; }
    public string? Direction { get; set; }
    public int? Status { get; set; }
    public bool? Active { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
public sealed class KitPageV1<T>
{
    public string Version => "KITTING-OWNER/1";
    public string Coverage => "AUTHORIZED_LEGACY_OBSERVATION";
    public bool WritesAvailable => false;
    public string WriteBlocker => "KIT_OWNER_ADOPTION_UNPROVEN";
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<T> Items { get; init; } = [];
}
public sealed record KitMasterObservationV1(Guid Id, string KitSku, string? KitName, string? DefaultWarehouseCd,
    bool ActiveFlg, string? Remarks, bool KitNameReadable, bool RemarksReadable)
{
    public string Version => "KITTING-OWNER/1";
    public string RecipeState => "CURRENT_LEGACY_RECIPE";
}
public sealed record KitComponentObservationV1(Guid Id, int LineNo, string ComponentProductCd, string? ComponentName,
    string RequiredQty, string? UnitCd, string? Remarks, bool ComponentNameReadable, bool RemarksReadable);
public sealed record KitMasterDetailV1(KitMasterObservationV1 Master, List<KitComponentObservationV1> Components)
{
    public string Version => "KITTING-OWNER/1";
    public string HistoricalComposition => "UNPROVEN";
}
public sealed record KitOrderObservationV1(Guid Id, string KitOrderNo, string KitSku, string? KitName, string Qty,
    string Direction, string WarehouseCd, string KitLocationCd, string? KitLotNo, int LegacyStatus,
    DateTime? ExecutedAt, DateTime? CreateDate, string? OperatorCd, string? Remarks, string? ExecutedTxnNos,
    bool KitNameReadable, bool OperatorCdReadable, bool RemarksReadable, bool ExecutedTxnNosReadable, string TransactionReferencesState)
{
    public string Version => "KITTING-OWNER/1";
    public string CompositionState => "UNPROVEN";
    public string QuantityUnitState => "NOT_RECORDED";
    public string? UnitCd => null;
}
