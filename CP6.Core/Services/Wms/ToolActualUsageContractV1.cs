using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

// Supplied by the host, never by an HTTP command. Missing configuration denies new actual-use commands.
public sealed record ToolActualRuntimeV1(Guid EnvironmentId, string Mode);
public sealed record ToolActualScopeV1(Guid EnvironmentId, Guid TenantId, Guid SiteId, string Mode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualEvidenceCommandV1(Guid OperationId, string DocumentKey, string LineKey, string EventOrdinal,
    int Shots, DateTimeOffset OccurredAt, string Evidence, ToolActualRefV1? Previous = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualRegisterV1(Guid OperationId, ToolActualRefV1 SourceEvidenceSelectionRef, int ReportedShots, DateTimeOffset OccurredAt, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualObservationV1(Guid OperationId, ToolActualRefV1 OccurrenceSelectionRef, int Shots, DateTimeOffset OccurredAt, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualPolicyCommandV1(Guid OperationId, string State, EngineeringSourceRefV1 Site, bool AllowLocalAttestation,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualRecognitionV1(Guid OperationId, string ExpectedObservationDigest, string Outcome,
    ToolMaintenanceRefV1? RecognizedCycleRef, ToolActualRefV1[] EvidenceRefs, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolActualApplyV1(Guid OperationId, string ExpectedObservationApplicationRevision, string ApplicationContextRef, ToolActualRefV1 DecisionRef);
public sealed record ToolUseOccurrenceIdentityV1(string ProducerOwner, string SourceKind, Guid SourceDocumentId,
    string SourceLineId, Guid OccurrenceLeafId, string Action = "TOOL_USE");
public sealed record ToolActualEvidenceBodyV1(Guid DocumentId, string DocumentKey, string LineKey, string EventOrdinal,
    int Shots, DateTimeOffset OccurredAt, string Evidence, ToolActualRefV1? Previous, string NativeSchema = "Main.Wms.ManualToolUseEvidence.schema.v1",
    string Provenance = "LOCAL_RECORDED_ATTESTATION");
public sealed record ToolActualOccurrenceBodyV1(ToolUseOccurrenceIdentityV1 Identity, ToolActualRefV1 EvidenceRef,
    int Shots, DateTimeOffset OccurredAt, string Reason, string Knowledge = "OBSERVATION_ONLY");
public sealed record ToolActualObservedBodyV1(ToolActualRefV1 OccurrenceRef, ToolUseOccurrenceIdentityV1 Identity,
    int Shots, DateTimeOffset OccurredAt, string Reason, string Kind = "USAGE_ACTUAL");
public sealed record ToolActualRecognizedBodyV1(ToolActualRefV1 Observation, ToolUseOccurrenceIdentityV1 Identity, string Outcome,
    ToolActualRefV1? Policy, ToolMaintenanceRefV1? TargetCycle, int Shots, DateTimeOffset OccurredAt,
    ToolActualRefV1[] EvidenceRefs, string Reason, string QualificationAtOccurrence, string EngineeringKnowledge = "UNPROVEN");
public sealed record ToolActualUseBodyV1(ToolActualRefV1 Observation, ToolActualRefV1 Decision, ToolActualRefV1 Occurrence,
    ToolUseOccurrenceIdentityV1 Identity, ToolMaintenanceRefV1 TargetCycle, int Shots, DateTimeOffset OccurredAt,
    ToolMaintenanceCycleV1 CurrentCycleBefore, ToolMaintenanceCycleV1 CurrentCycleAfter,
    string PreviousTargetShots, string NextTargetShots, string QualificationAtOccurrence,
    string Outcome = "APPLIED_ACTUAL_WITH_RESTRICTIONS");
public sealed record ToolActualApplicationBodyV1(ToolActualRefV1 Observation, ToolActualRefV1 Decision, ToolActualRefV1 Use,
    string ApplicationRevision, string Outcome = "APPLIED_ACTUAL_WITH_RESTRICTIONS", bool IsUsePermit = false);
public sealed record ToolActualFactV1(ToolActualScopeV1 Scope, Guid ToolId, Guid RecordId, string Kind, Guid? SubjectId,
    string IdentityKey, string Revision, Guid ActorId, DateTimeOffset RecordedAt, ToolInstanceRefV1 NativeBefore,
    ToolInstanceRefV1 NativeAfter, EngineeringSourceRefV1 Site, ToolMaintenanceAuthorityV1[] Authorities, JsonElement Body);
public sealed record ToolActualFactViewV1(ToolActualRefV1 Reference, ToolActualFactV1 Fact);
public sealed record ToolActualCycleV1(ToolMaintenanceRefV1 Reference, string? Number, string Shots, bool Current,
    DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt);
public sealed record ToolActualObservationViewV1(ToolActualFactViewV1 Original, string ApplicationState, string ApplicationRevision,
    ToolActualFactViewV1[] Decisions, ToolActualFactViewV1? Application);
public sealed record ToolActualContextV1(Guid TenantId, Guid ToolId, ToolActualScopeV1 Scope, string Etag, ToolInstanceRefV1 Native,
    EngineeringSourceRefV1 Site, string Knowledge, string CurrentEligibility, ToolActualCycleV1[] Cycles,
    string LifetimeKnowledge, string? LifetimeShots, ToolActualFactViewV1? Policy, string PolicyKnowledge,
    ToolActualFactViewV1[] Evidence, ToolActualFactViewV1[] Occurrences, ToolActualObservationViewV1[] Observations,
    ToolActualFactViewV1[] Uses, bool IsUsePermit = false, int Page = 1);
public sealed record ToolActualApplicationContextV1(ToolActualRefV1 Observation, string Revision, string ContextRef,
    ToolActualCycleV1[] Cycles, ToolActualFactViewV1? Policy, string[] Missing, ToolActualFactViewV1[] Evidence, bool IsUsePermit = false);
public sealed record ToolActualResultV1(Guid OperationId, Guid ObjectId, string Kind, ToolActualRefV1 Fact,
    string Outcome, bool IsUsePermit = false);
public sealed record ToolActualOperationV1(Guid OperationId, string State, ToolActualResultV1? Result = null);
