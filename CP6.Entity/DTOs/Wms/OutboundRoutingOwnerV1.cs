namespace CP6.Entity.DTOs.Wms;

public sealed record RoutingQueryV1
{
    public Guid? TargetWarehouseId { get; set; }
    public bool? Enabled { get; set; }
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=50;
}
public sealed record RoutingObservationV1(Guid Id,string? RuleName,int SortOrder,string? CustomerCd,
    string CustomerCondition,string? ProductCdPrefix,string ProductCondition,int? OutboundType,
    Guid TargetWarehouseId,string TargetWarehouseCd,bool LegacyEnabled,string? Remarks,string Etag,
    bool NameReadable,bool CustomerReadable,bool ProductReadable,bool RemarksReadable)
{
    public string FormalPolicyState=>"UNPROVEN";
    public string DomainState=>"NOT_RECORDED";
    public string SelectorState=>"NOT_ADOPTED";
}
public sealed record RoutingPageV1(IReadOnlyList<RoutingObservationV1> Items,int Total,int Page,int PageSize,bool LegacyResolverEnabled)
{
    public string Version=>"ROUTING-OWNER/1";
    public string Coverage=>"CURRENT_AUTHORIZED_RECORDS";
    public bool FormalPreviewAvailable=>false;
}
