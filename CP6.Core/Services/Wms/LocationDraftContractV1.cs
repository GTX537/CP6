using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationDraftCommandV1(Guid OperationId,string AuthorityVersion,Guid? DepartmentId,
    string ExpectedPolicyVersion,string ExpectedHierarchyRevision,string LocationCode,string? LocationName,
    [property:JsonRequired] Guid? ParentLocationId,[property:JsonRequired] int LocationLevel,string? Barcode);
public sealed record LocationDraftScopeV1(Guid TenantId,Guid ActorId,Guid WarehouseId);
public sealed record LocationDraftContextV1(LocationDraftScopeV1 Scope,WarehouseDraftCreationV1 Creation,
    EngineeringSourceRefV1 SiteRef,LocationHierarchyPolicyV1 Policy,string HierarchyRevision,string FenceCapability)
{
    public string BarcodeSource=>"UNAVAILABLE";
}
public sealed record LocationDraftViewV1(Guid Id,Guid WarehouseId,string WarehouseCode,string Code,string? Name,
    int Level,Guid? ParentId,string ParentState,string Etag,string BusinessRevision,EngineeringSourceRefV1 SiteRef,bool CanReadName,bool CanWriteName)
{
    public string Lifecycle=>"DRAFT";
    public string SourceKind=>"MANUAL";
    public string ControlRevision{get;init;}="1";
    public string InitializationState=>"QUARANTINED";
    public string InitializationProvenance=>"MANUAL_CREATE";
    public string LegacyCapacityKind=>"TECHNICAL_PLACEHOLDER";
    public string CapacityKnowledge=>"UNKNOWN";
    public string ReservationKnowledge=>"UNKNOWN";
    public string? ReservedCapacityQty=>null;
    public string SelectorRevision=>"0";
    public string? CurrentRule=>null;
    public bool AdmissionGrant=>false;
    public string Eligibility=>"NOT_ACTIVATED";
}
public sealed record LocationDraftResultV1(Guid OperationId,Guid WarehouseId,Guid LocationId,Guid SiteId,
    string BusinessRevision,string HierarchyRevision,string Etag)
{
    public string Effect=>"CREATED_QUARANTINED_DRAFT";
}
public sealed record LocationDraftOperationV1(Guid OperationId,Guid WarehouseId,string State,LocationDraftResultV1? Result=null);
