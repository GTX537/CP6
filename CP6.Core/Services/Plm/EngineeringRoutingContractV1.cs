using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingParameterV1(string Name, string? Value, string? Minimum, string? Maximum,
    UomProfileRefV1? UnitRef, string Method);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingOperationV1(Guid OccurrenceId, string ProcessCode, string Name, int SemanticOrder,
    Guid[] DependsOn, string? AlternativeGroup, string Applicability, string Reason, string Instructions,
    ResourceSourceRefV1[] Resources, EngineeringSourceRefV1[] Tools, RoutingParameterV1[] Parameters,
    string DocumentRequirement, Guid? LegacyRowId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceCapabilityRefV1[]? ResourceCapabilities = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRoutingBodyV1(string Name, EngineeringRevisionRefV1 RevisionOrigin,
    EngineeringProductRefV1 ProductRef, EngineeringSourceRefV1? SiteRef, string ManufacturingViewRequirement,
    RoutingOperationV1[] Operations);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRoutingCommandV1(Guid OperationId, Guid RoutingId, string Action,
    string? ExpectedVersion, Guid? ExpectedEtag, EngineeringRoutingBodyV1 Body, string Reason,
    EngineeringMappingRefV1? LegacyMappingRef = null, string? LegacyDigest = null);
public sealed record EngineeringRoutingLegacyV1(EngineeringMappingRefV1 MappingRef, EngineeringSourceRefV1 ProductRef,
    string Digest, JsonElement[] Rows, DateTimeOffset CapturedAt, RoutingOperationV1[] SuggestedOperations);
internal sealed record EngineeringRoutingStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, EngineeringRoutingBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt, EngineeringRoutingLegacyV1? Legacy);
public sealed record EngineeringRoutingSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference, Guid Etag,
    bool IsCurrent, EngineeringRoutingBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt, EngineeringRoutingLegacyV1? Legacy, string OriginalJson,
    string[]? Missing = null, string State = "DRAFT", string Readiness = "DRAFT_ONLY");
public sealed record EngineeringRoutingResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringRoutingOperationV1(Guid OperationId, string State, EngineeringRoutingResultV1? Result = null);
public sealed record EngineeringRoutingDiffV1(EngineeringSourceRefV1 Before, EngineeringSourceRefV1 After, EngineeringWorkingChangeV1[] Changes);
