using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceOccurrenceRegisterV1(Guid OperationId, Guid MaintenanceId,
    ToolMaintenanceRefV1 SourceEvidenceSelectionRef, string ReportedOutcome, DateTimeOffset OccurredAt, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceActualObservationV1(Guid OperationId, ToolActualRefV1 OccurrenceSelectionRef,
    string Outcome, DateTimeOffset OccurredAt, string Reason, string Kind);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceActualPolicyV1(Guid OperationId, string State, EngineeringSourceRefV1 Site,
    string[] RequiredChecks, DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceActualRecognitionV1(Guid OperationId, string ExpectedObservationDigest,
    string Outcome, ToolMaintenanceRefV1 EvidenceRef, string Reason);
public sealed record ToolMaintenanceOccurrenceIdentityV1(string ProducerOwner, string SourceKind, Guid SourceDocumentId,
    string SourceLineId, Guid OccurrenceLeafId, string Action = "MAINTENANCE_RESULT");
// A native REPORT is immutable and contains exactly one result. Its record id is the source event identity;
// different reports are not silently treated as revisions of one another.
public sealed record ToolMaintenanceOccurrenceBodyV1(ToolMaintenanceOccurrenceIdentityV1 Identity,
    ToolMaintenanceRefV1 EvidenceRef, string EvidenceLineId, string EventOrdinal, string Outcome,
    DateTimeOffset OccurredAt, string Reason, string Knowledge = "OBSERVATION_ONLY");
public sealed record ToolMaintenanceObservedBodyV1(ToolActualRefV1 OccurrenceRef, ToolMaintenanceOccurrenceIdentityV1 Identity,
    ToolMaintenanceRefV1 EvidenceRef, string Outcome, DateTimeOffset OccurredAt, string Reason,
    string Kind = "MAINTENANCE_RESULT_ACTUAL");
public sealed record ToolMaintenanceRecognizedBodyV1(ToolActualRefV1 Observation, ToolMaintenanceOccurrenceIdentityV1 Identity,
    ToolMaintenanceRefV1 EvidenceRef, string Outcome, string MaintenanceOutcome, DateTimeOffset OccurredAt,
    ToolActualRefV1? Policy, string Reason, string QualificationAtOccurrence = "UNPROVEN");
public sealed record ToolMaintenanceActualApplicationV1(ToolActualRefV1 Observation, ToolActualRefV1 Decision,
    ToolMaintenanceRefV1 Completion, string MaintenanceOutcome, string ApplicationRevision = "2", bool IsUsePermit = false);
public sealed record ToolMaintenanceActualContextV1(Guid ToolId, ToolActualScopeV1 Scope, string Etag,
    EngineeringSourceRefV1 Site, string Knowledge, ToolActualFactViewV1? Policy, string PolicyKnowledge,
    ToolMaintenanceFactViewV1[] Evidence, ToolActualFactViewV1[] Occurrences,
    ToolActualObservationViewV1[] Observations, ToolMaintenanceEntryV1[] Maintenance, int Page = 1, bool IsUsePermit = false);
public sealed record ToolMaintenanceActualApplicationContextV1(ToolActualRefV1 Observation, string Revision,
    string ContextRef, ToolActualFactViewV1? Policy, string[] Missing, ToolMaintenanceFactViewV1 Evidence, bool IsUsePermit = false);
