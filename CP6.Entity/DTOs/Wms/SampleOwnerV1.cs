namespace CP6.Entity.DTOs.Wms;

public sealed record SampleQueryV1
{
    public string? SampleNo { get; set; }
    public string? SampleType { get; set; }
    public string? CustomerCd { get; set; }
    public string? ProductCd { get; set; }
    public int? LegacyStatus { get; set; }
    public bool? AwaitingReturnEvidence { get; set; }
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=50;
}
public sealed record SampleObservationV1(Guid Id,string SampleNo,string SampleType,string DeclaredQuantity,string UnitCd,
    int LegacyStatus,string? WarehouseCd,string? LocationCd,DateTime RegisteredAt,DateTime? LentAt,DateTime? ExpectedReturnDate,DateTime? ReturnedAt,
    string? CustomerCd,string? CustomerName,string? ProductCd,string? ProductName,string? LentTo,string? Remarks,
    bool CustomerCdReadable,bool CustomerNameReadable,bool ProductCdReadable,bool ProductNameReadable,bool BorrowerReadable,bool RemarksReadable,
    string ReturnEvidenceState,string Etag)
{
    public string SourceMode=>"LEGACY_OBSERVATION";
    public string StockSourceState=>"SOURCE_UNPROVEN";
    public string HistoryState=>"HISTORY_INCOMPLETE";
    public string SiteDateState=>"UNPROVEN";
    public string? OutstandingQuantity=>null;
}
public sealed record SamplePageV1(IReadOnlyList<SampleObservationV1> Items,int Total,int Page,int PageSize)
{
    public string Version=>"SAMPLE-OWNER/1";
    public string Coverage=>"CURRENT_AUTHORIZED_RECORDS";
    public bool FormalOperationsAvailable=>false;
}
