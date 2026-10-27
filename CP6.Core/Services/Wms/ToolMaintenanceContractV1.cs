using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenancePolicyCommandV1(Guid OperationId, string State, EngineeringSourceRefV1 Site,
    string[] RequiredChecks, bool AllowReset, bool AllowUnserviceableRepair, bool IndependentInspector,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceStartV1(Guid OperationId, string ReasonCode, string Reason, string? PlannedCompletionDate = null,
    Guid? PredecessorMaintenanceId = null, ToolMaintenanceRefV1? RepairPolicy = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceCheckV1(string Code, string Result, string Evidence);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceReportCommandV1(Guid OperationId, string Outcome, DateTimeOffset OccurredAt,
    ToolMaintenanceCheckV1[] Checks, string Evidence, ToolMaintenanceRefV1? Policy = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceCompleteV1(Guid OperationId, ToolMaintenanceRefV1 EvidenceRef, ToolMaintenanceRefV1 Policy);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceAssessmentCommandV1(Guid OperationId, ToolMaintenanceRefV1 CompletionRef,
    ToolMaintenanceRefV1 Policy, string Decision, DateTimeOffset ValidUntil, string Evidence);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolMaintenanceResetV1(Guid OperationId, ToolMaintenanceRefV1 CompletionRef, ToolMaintenanceRefV1 ResetPolicySelectionRef,
    ToolMaintenanceRefV1 QualificationRef, ToolMaintenanceRefV1 ExpectedCycleRef);
public sealed record ToolMaintenanceCycleV1(Guid Id, string? Number, int Shots, string LifetimeKnowledge, string? LifetimeShots,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProjectionRevision = null);
public sealed record ToolMaintenanceCycleViewV1(ToolMaintenanceRefV1 Reference, string? Number, string? Shots, string Knowledge,
    string LifetimeKnowledge, string? LifetimeShots);
public sealed record ToolMaintenanceAuthorityV1(string Resource, string Action, string PermissionVersion, string OrganizationVersion);
public sealed record ToolMaintenanceFactV1(Guid TenantId, Guid ToolId, Guid RecordId, string Kind, Guid? MaintenanceId,
    Guid ActorId, DateTimeOffset RecordedAt, ToolInstanceRefV1 NativeBefore, ToolInstanceRefV1 NativeAfter,
    EngineeringSourceRefV1 Site, ToolMaintenanceAuthorityV1[] Authorities, JsonElement Body, ToolMaintenanceCycleV1? CycleAfter = null);
public sealed record ToolMaintenanceFactViewV1(ToolMaintenanceRefV1 Reference, ToolMaintenanceFactV1 Fact);
public sealed record ToolMaintenanceEntryV1(Guid MaintenanceId, string Status, ToolMaintenanceFactViewV1 Start,
    ToolMaintenanceFactViewV1? Completion, ToolMaintenanceFactViewV1? Reset, ToolMaintenanceFactViewV1[] Reports);
public sealed record ToolMaintenanceContextV1(Guid TenantId, Guid ToolId, string Etag, ToolInstanceRefV1 Native,
    EngineeringSourceRefV1 Site, bool SiteEnabled, ToolMaintenanceCycleViewV1 Cycle, ToolMaintenanceFactViewV1? Policy,
    string PolicyKnowledge, ToolMaintenanceEntryV1[] Maintenance, ToolMaintenanceFactViewV1[] Assessments,
    string CurrentEligibility, bool NotExecutionPermit = true, int Page = 1);
public sealed record ToolMaintenanceResultV1(Guid OperationId, Guid ObjectId, string Kind, ToolMaintenanceRefV1 Fact,
    Guid? MaintenanceId, string Outcome, bool NotExecutionPermit = true);
public sealed record ToolMaintenanceOperationV1(Guid OperationId, string State, ToolMaintenanceResultV1? Result = null);
