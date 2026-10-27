using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingMapV1(Guid MapId, string Kind, Guid[] EbomOccurrenceIds,
    Guid[] MbomOccurrenceIds, Guid[] OperationOccurrenceIds, string Reason, string QuantityBasis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingOutputV1(Guid OutputOccurrenceId, string Role, string Name,
    EngineeringProductRefV1? ProductRef, Guid? GeneratingOperationOccurrenceId,
    Guid[] InputOccurrenceIds, Guid[] FromOutputOccurrenceIds, EngineeringRevisionRefV1? ConfigurationRef,
    string QuantityKind, string? Quantity, string? Minimum, string? Maximum, UomProfileRefV1? UnitRef,
    string? Denominator, UomProfileRefV1? DenominatorUnitRef, string QuantityBasis,
    string SpecRequirement, string RelationEvidence, string NoInputBasis, string DispositionRequirement);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringManufacturingBodyV1(string Name, EngineeringRevisionRefV1 RevisionOrigin,
    EngineeringProductRefV1 ProductRef, EngineeringRevisionRefV1? ConfigurationRef,
    EngineeringSourceRefV1 EbomRef, EngineeringSourceRefV1 MbomRef, EngineeringSourceRefV1 RoutingRef,
    EngineeringSourceRefV1? SiteRef, ManufacturingMapV1[] Mappings, ManufacturingOutputV1[] Outputs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringManufacturingCommandV1(Guid OperationId, Guid ManufacturingId, string Action,
    string? ExpectedVersion, Guid? ExpectedEtag, EngineeringManufacturingBodyV1 Body, string Reason);
internal sealed record EngineeringManufacturingStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, EngineeringManufacturingBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt);
public sealed record EngineeringManufacturingSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference,
    Guid Etag, bool IsCurrent, EngineeringManufacturingBodyV1 Body, string Action, string Reason,
    string? ParentVersion, Guid ActorId, DateTimeOffset RecordedAt, string OriginalJson,
    string[]? Missing = null, string State = "DRAFT", string Readiness = "DRAFT_ONLY");
public sealed record EngineeringManufacturingResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringManufacturingOperationV1(Guid OperationId, string State, EngineeringManufacturingResultV1? Result = null);
public sealed record EngineeringManufacturingDiffV1(EngineeringSourceRefV1 Before, EngineeringSourceRefV1 After, EngineeringWorkingChangeV1[] Changes);
