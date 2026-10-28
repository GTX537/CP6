using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

// Trusted deployment configuration, never accepted from an HTTP request. A declared error bound
// is an operator's source attestation; reading PostgreSQL alone does not establish UTC accuracy.
public sealed class WarehouseClockOptionsV1
{
    public bool Enabled { get; set; }
    public WarehouseClockAuthorityV1? Authority { get; set; }
    public Guid[] Tenants { get; set; } = [];
}
public sealed record WarehouseClockAuthorityV1(Guid EnvironmentId, Guid Id, string Version, string Provider,
    string EvidenceMode, string AttestationRef, string ValidFrom, string ValidTo, int MaxErrorMilliseconds, int MaxReadMilliseconds);
public sealed record WarehouseClockSourceV1(EngineeringSourceRefV1 Reference, WarehouseClockAuthorityV1 Authority);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WarehouseClockDefinitionV1([property:JsonRequired] bool Enabled, EngineeringSourceRefV1 Source,
    string ValidFrom, string ValidTo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WarehouseClockCommandV1(Guid OperationId, string ExpectedVersion, WarehouseClockDefinitionV1 Definition, string Reason);
public sealed record WarehouseClockOriginalV1(Guid TenantId, Guid Id, Guid WarehouseId, string Version, Guid OperationId,
    Guid ActorId, DateTimeOffset RecordedAt, Guid EnvironmentId, WarehouseClockDefinitionV1 Definition, WarehouseClockSourceV1 Source, string Reason);
public sealed record WarehouseClockPolicyV1(EngineeringSourceRefV1 Reference, WarehouseClockOriginalV1 Original, string OriginalJson);
public sealed record WarehouseClockContextV1(Guid TenantId, Guid ActorId, Guid WarehouseId, string WarehouseCode, string Version,
    WarehouseClockPolicyV1? Policy, WarehouseClockSourceV1? Source)
{
    public string Qualification => "CLOCK_CONFIGURATION_ONLY";
    public bool AdmissionGrant => false;
}
public sealed record WarehouseClockResultV1(Guid OperationId, Guid WarehouseId, EngineeringSourceRefV1 Reference, bool Changed);
public sealed record WarehouseClockOperationV1(Guid OperationId, Guid WarehouseId, string State, WarehouseClockResultV1? Result = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleTimeRequestV1(EngineeringSourceRefV1 Proposal, string Kind, [property:JsonRequired] string? At);
public sealed record WarehouseClockObservationV1(Guid TenantId, Guid EnvironmentId, Guid WarehouseId, Guid Id,
    EngineeringSourceRefV1 Policy, EngineeringSourceRefV1 Source, string EvidenceMode, DateTimeOffset ObservedAt,
    DateTimeOffset Earliest, DateTimeOffset Latest, long ReadElapsedTicks);
public sealed record WarehouseClockEvidenceV1(EngineeringSourceRefV1 Reference, WarehouseClockObservationV1 Original);
public sealed record WarehouseClockReadingV1(string State, string Reason, WarehouseClockEvidenceV1? Evidence);
public sealed record RuleTimeAssessmentV1(Guid TenantId, Guid ActorId, Guid LocationId, Guid WarehouseId,
    EngineeringSourceRefV1 Proposal, string Etag, RuleTimeRequestV1 Requested, string Outcome, string Reason,
    WarehouseClockEvidenceV1? Clock, string? EvaluatedAt, bool IsPrediction)
{
    public string Qualification => "RULE_TIME_ONLY";
    public bool AdmissionGrant => false;
    public bool RequiresFinalEffectRecheck => true;
    public string[] RemainingGates => ["CURRENT_APPROVAL", "OCCUPANCY", "EFFECT_OWNER_PROTECTION", "SELECTOR_AND_APPLICATION"];
}
