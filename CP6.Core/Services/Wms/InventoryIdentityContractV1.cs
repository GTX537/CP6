using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryIdentityRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryPartyV1(string Kind, string? LegalName, string? Jurisdiction, string? RegistrationNumber,
    BusinessPartnerIdentityRefV1? Customer);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryPurposeV1(string Category, string Meaning);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryProductV1(EngineeringSourceRefV1 Product, EngineeringSourceRefV1 UnitBinding,
    EngineeringMappingRefV1 Mapping, string TechnicalKind, EngineeringSourceRefV1? Baseline, string TechnicalBasis,
    InventoryIdentityRefV1 Party, InventoryIdentityRefV1 Purpose);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryIdentityDefinitionV1(string Kind, string Code, string Name, string Basis,
    InventoryPartyV1? Party, InventoryPurposeV1? Purpose, InventoryProductV1? Product);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryIdentityCommandV1(Guid OperationId, Guid Id, string Action, string ExpectedVersion,
    InventoryIdentityDefinitionV1? Definition, string Reason, string? ExpectedAuthorityVersion = null, Guid? DepartmentId = null);
public sealed record InventoryIdentitySourceV1(string Path, EngineeringSourceRefV1 Reference, string OriginalJson);
public sealed record InventoryIdentityOriginalV1(Guid EnvironmentId, Guid TenantId, Guid Id, string Version, Guid OperationId, string Action, Guid CreatorId,
    Guid AuthorId, Guid ActorId, DateTimeOffset RecordedAt, string EvidenceMode, string State, InventoryIdentityDefinitionV1 Definition,
    string Reason, InventoryIdentitySourceV1[] Sources, EngineeringOwnerConfirmationV1[] Confirmations);
public sealed record InventoryIdentityViewV1(InventoryIdentityRefV1 Reference, InventoryIdentityOriginalV1 Original,
    string OriginalJson, bool IsCurrent, bool CanSelect, string[] Reasons);
public sealed record InventoryIdentityResultV1(Guid OperationId, Guid ObjectId, string Version, string Digest, bool Changed);
public sealed record InventoryIdentityOperationV1(Guid OperationId, string State, InventoryIdentityResultV1? Result = null);
public sealed record InventoryIdentityScopeV1(Guid TenantId, Guid ActorId, Guid EnvironmentId, string EvidenceMode);
public sealed record InventoryIdentityDepartmentV1(Guid Id, string Code, string Name);
public sealed record InventoryIdentityCreationV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, InventoryIdentityDepartmentV1[] Departments);
public sealed record InventoryCodeRefV1(string Id, string Version);
public sealed record InventoryTechnicalIdentityV1(string Kind, EngineeringSourceRefV1? RevisionRef, EngineeringSourceRefV1? ApplicabilityRef);
public sealed record InventoryOwnerIdentityV1(string Kind, EngineeringSourceRefV1 Reference);
// Original Stock/Transfer product option shape. This catalogue does not grant a physical effect.
public sealed record InventoryProductOptionV1(EngineeringSourceRefV1 ProductRef, string Label, InventoryCodeRefV1 BaseUomRef,
    InventoryCodeRefV1 PrecisionPolicyRef, InventoryTechnicalIdentityV1 TechnicalIdentity,
    InventoryCodeRefV1 OriginPurposeRef, InventoryOwnerIdentityV1 Owner);
public sealed record InventoryProductCatalogueV1(InventoryIdentityRefV1 CatalogueRef, InventoryProductOptionV1 Option,
    InventoryIdentitySourceV1[] Sources, string PhysicalEligibility = "REQUIRES_FINAL_OWNER_GATES");
