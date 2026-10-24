namespace CP6.Entity.DTOs.Wms;

public sealed class PackagingQueryV1
{
    public string? PackageNo { get; set; }
    public string? OutboundNo { get; set; }
    public string? CarrierCd { get; set; }
    public string? TrackingNo { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=50;
}
public sealed record PackagingRecordV1(Guid Id,string PackageNo,Guid OutboundId,string OutboundNo,int CaseQty,
    string? TotalWeightKg,string? TotalVolumeM3,string? CarrierCd,string? TrackingNo,DateTime? DepartureTime,string? Remarks,
    bool CarrierReadable,bool TrackingReadable,bool RemarksReadable)
{
    public string RecordKind=>"LEGACY_SHIPPING_PACKAGE";
    public string FormalPackageState=>"UNPROVEN";
    public string DeliveryProofState=>"NOT_RECORDED";
    public string TimeBasis=>"LEGACY_UNSPECIFIED";
}
public sealed record PackagingPageV1(IReadOnlyList<PackagingRecordV1> Items,int Total,int Page,int PageSize)
{
    public string Version=>"PACKAGING-OWNER/1";
    public string Coverage=>"CURRENT_AUTHORIZED_RECORDS";
}
