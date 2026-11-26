using System.Text.Json.Serialization;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringLegacyRegistrationV1(Guid OperationId, EngineeringProductRefV1 Product, string SourceRepositorySha,
    string SourceRecordRef, string? SourcePath, string SourceHash, string[] EvidenceGaps, string Reason);

public sealed record EngineeringLegacyOriginalV1(Guid TenantId, Guid Id, Guid ActorId, DateTimeOffset RecordedAt, EngineeringLegacyRegistrationV1 Registration);
public sealed record EngineeringLegacyReferenceV1(EngineeringSourceRefV1 Reference, EngineeringLegacyOriginalV1 Original, string EvidenceStatus = "EVIDENCE_INCOMPLETE");
public sealed record EngineeringLegacyOperationV1(Guid OperationId, string State, EngineeringLegacyReferenceV1? Result = null);
