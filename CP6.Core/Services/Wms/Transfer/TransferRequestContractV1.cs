using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Wms.Transfer;

// Native Main DIRECT_MOVE request adapter. The complete TF v1.1 wire is a separate contract.
// Site assignment is explicit deployment input, not inferred from warehouse code or caller identity.
public sealed class TransferRequestOptionsV1
{
    public TransferWarehouseSiteV1[] WarehouseSites { get; set; } = [];
}
public sealed record TransferWarehouseSiteV1(Guid WarehouseId, Guid SiteId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferRequestLineInputV1(string ClientLineKey, InventoryIdentityRefV1 ProductCatalogueRef,
    UomProfileRefV1 InputUomRef, string InputQuantity, Guid SourceWarehouseId, Guid SourceLocationId,
    Guid DestinationWarehouseId, Guid DestinationLocationId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferRequestCreateV1(Guid OperationId, string DraftKey, Guid? DepartmentId,
    TransferRequestLineInputV1[] Lines, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferRequestEditV1(Guid OperationId, string ExpectedRevision, string Etag,
    TransferRequestLineInputV1[] Lines, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferRequestActionV1(Guid OperationId, string ExpectedRevision, string Etag,
    string ContentDigest, string Reason);
public sealed record TransferRequestLocationV1(Guid WarehouseId, string WarehouseCode, Guid LocationId,
    string LocationCode, string Label, EngineeringSourceRefV1 SiteRef);
public sealed record TransferRequestProductV1(InventoryIdentityRefV1 CatalogueRef, string Label);
public sealed record TransferRequestContextV1(Guid TenantId, Guid ActorId, TransferDeploymentV1 Deployment,
    bool AllowsUnbound, InventoryIdentityDepartmentV1[] Departments, TransferRequestLocationV1[] Locations,
    TransferRequestProductV1[] Products, UomProfileVersionV1[] Units);
public sealed record TransferRequestLineV1(Guid Id, TransferRequestLineInputV1 Input, InventoryProductOptionV1 Product,
    EngineeringSourceRefV1 SourceSiteRef, EngineeringSourceRefV1 DestinationSiteRef,
    string BaseQuantity, string BudgetKey, EngineeringSourceRefV1 DemandRef, string DemandOriginalJson, Guid BindingId, string BindingOriginalJson);
public sealed record TransferRequestApprovalV1(Guid Id, Guid ActorId, string RequestVersion, string ContentDigest,
    DateTimeOffset DecidedAt, string Decision, string Policy = "MAIN_TRANSFER_INDEPENDENT_APPROVAL_V1");
public sealed record TransferRequestViewV1(Guid Id, string DraftKey, Guid CreatorId, Guid AuthorId, Guid? DepartmentId,
    string Version, string Revision, string Etag, string State, TransferRequestLineV1[] Lines, string ContentDigest,
    Guid? SubmitterId, TransferRequestApprovalV1? Approval, TransferDeploymentV1 Deployment,
    string[] AllowedActions, string ExecutionState = "NOT_DISPATCHED", TransferStageV1[]? Stages = null);
public sealed record TransferRequestResultV1(Guid OperationId, TransferRequestViewV1 Request, bool Changed);
public sealed record TransferRequestOperationV1(Guid OperationId, string State, TransferRequestResultV1? Result = null);
