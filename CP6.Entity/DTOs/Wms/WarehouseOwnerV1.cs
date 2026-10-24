namespace CP6.Entity.DTOs.Wms;

// Current legacy identities and original storage observations, not formal typed MASTER adoption.
public sealed record WarehouseQueryV1
{
    public string? WarehouseCd { get; set; }
    public int? WarehouseType { get; set; }
    public string? BaseCd { get; set; }
    public string? LocationCd { get; set; }
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=50;
}
public sealed record MasterPageV1<T>(IReadOnlyList<T> Items,int Total,int Page,int PageSize)
{
    public string Version => "WAREHOUSE-OWNER/1";
    public string Coverage => "CURRENT_AUTHORIZED_RECORDS";
    public string FormalMasterState => "UNPROVEN";
}
public sealed record WarehouseObservationV1(Guid Id,string WarehouseCd,string? WarehouseName,int WarehouseType,
    string? BaseCd,string? AddressText,string? ManagerCd,string? Remarks,bool LegacyAllowNegative,int LegacyOutboundPriority,
    string Etag,bool NameReadable,bool NameWritable,bool AddressReadable,bool ManagerReadable,bool RemarksReadable)
{
    public Guid? SiteId => null;
    public string SiteState => "NOT_RECORDED";
    public string LifecycleState => "UNPROVEN";
}
public sealed record LocationObservationV1(Guid Id,Guid WarehouseId,string WarehouseCd,string LocationCd,string? LocationName,
    int LocationLevel,Guid? ParentId,string? ParentLocationCd,string ParentState,int? XCoord,int? YCoord,int? ZCoord,
    string RawCapacityQty,string RawReservedCapacityQty,string? AreaCd,string? RawAllowedProductType,bool LegacyIsPickable,
    bool LegacyIsBlocked,string? Barcode,string Etag,bool NameReadable,bool NameWritable,bool BarcodeReadable)
{
    public string CapacityState => "UNKNOWN";
    public string? TypedCapacity => null;
    public string ReservationState => "UNPROVEN";
    public string? ConfirmedReservedQty => null;
    public string LifecycleState => "UNPROVEN";
    public string HierarchyState => "ORIGINAL_REFERENCES_ONLY";
}
public sealed record MasterNameResultV1(Guid OperationId,string Kind,Guid ObjectId,string Effect,string Etag);
public sealed record MasterOperationV1(Guid OperationId,string Kind,Guid ObjectId,string State,MasterNameResultV1? Result=null);
public sealed record MasterOperationScopeV1(Guid TenantId,Guid ActorId);
