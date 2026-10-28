using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
using CP6.Entity.DTOs.Wms;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WarehouseDraftCommandV1(Guid OperationId, string AuthorityVersion, Guid? DepartmentId,
    Guid SiteId, string WarehouseCode, string WarehouseName, [property: JsonRequired] int WarehouseType,
    string? BaseCode, string? Address, string? ManagerCode, string? Remarks);
public sealed record WarehouseDraftDepartmentV1(Guid Id, string Code, string Name);
public sealed record WarehouseDraftCreationV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, IReadOnlyList<WarehouseDraftDepartmentV1> Departments);
public sealed record WarehouseDraftTypeV1(int Code, string Name);
public sealed record WarehouseDraftContextV1(WarehouseDraftCreationV1 Creation,
    IReadOnlyList<EngineeringSiteChoiceV1> Sites, string TypeOwner, string TypeVersion,
    IReadOnlyList<WarehouseDraftTypeV1> Types, string RelationshipState);
public sealed record WarehouseDraftViewV1(WarehouseObservationV1 Warehouse, EngineeringSourceRefV1 SiteRef,
    string Lifecycle, string BusinessRevision, string TypeOwner, string TypeVersion)
{
    public bool AdmissionGrant => false;
    public string Eligibility => "NOT_ACTIVATED";
}
public sealed record WarehouseDraftResultV1(Guid OperationId, Guid WarehouseId, Guid SiteId,
    string BusinessRevision, string Etag)
{
    public string Effect => "CREATED_DRAFT";
}
public sealed record WarehouseDraftOperationV1(Guid OperationId, string State, WarehouseDraftResultV1? Result = null);
