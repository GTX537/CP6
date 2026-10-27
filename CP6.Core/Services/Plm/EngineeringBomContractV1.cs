using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomOccurrenceV1(Guid OccurrenceId, Guid? ParentOccurrenceId, string Path, int SemanticOrder,
    string Name, EngineeringSourceRefV1? ItemRef, string? Quantity, UomProfileRefV1? UnitRef, string QuantityBasis,
    string? Loss, string LossBasis, string Applicability, string Reason, string ConfigurationCondition,
    string SubstitutionRequirement, Guid? RoutingOccurrenceId, string Note, Guid? LegacyRowId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringBomBodyV1(string Name, string Kind, EngineeringRevisionRefV1 RevisionOrigin,
    EngineeringProductRefV1 ProductRef, EngineeringSourceRefV1? SiteRef, EngineeringSourceRefV1? RoutingRef,
    BomOccurrenceV1[] Occurrences);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringBomCommandV1(Guid OperationId, Guid BomId, string Action,
    string? ExpectedVersion, Guid? ExpectedEtag, EngineeringBomBodyV1 Body, string Reason,
    EngineeringMappingRefV1? LegacyMappingRef = null, string? LegacyDigest = null);
public sealed record EngineeringBomLegacyV1(EngineeringMappingRefV1 MappingRef, EngineeringSourceRefV1 ProductRef,
    string Digest, JsonElement[] Rows, DateTimeOffset CapturedAt, BomOccurrenceV1[] SuggestedOccurrences);
internal sealed record EngineeringBomStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, EngineeringBomBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt, EngineeringBomLegacyV1? Legacy);
public sealed record EngineeringBomSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference, Guid Etag,
    bool IsCurrent, EngineeringBomBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt, EngineeringBomLegacyV1? Legacy, string OriginalJson,
    string[]? Missing = null, string State = "DRAFT", string Readiness = "DRAFT_ONLY");
public sealed record EngineeringBomResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringBomOperationV1(Guid OperationId, string State, EngineeringBomResultV1? Result = null);
public sealed record EngineeringBomDiffV1(EngineeringSourceRefV1 Before, EngineeringSourceRefV1 After, EngineeringWorkingChangeV1[] Changes);
public sealed record EngineeringBomUsageV1(EngineeringSourceRefV1 BomRef, string Name, string Kind, Guid OccurrenceId, string Path);
public sealed record EngineeringBomItemChoiceV1(EngineeringSourceRefV1 Reference, string Code, string Name);
