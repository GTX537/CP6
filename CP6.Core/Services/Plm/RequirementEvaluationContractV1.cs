using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

public sealed record RequirementEvaluationRuntimeV1(string EvidenceMode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementEvaluationRoleV1(string Code, int RoleId, int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementEvaluationPolicyInputV1(string State, string Basis,
    RequirementEvaluationRoleV1[] Roles, [property: JsonRequired] DateTimeOffset EffectiveFrom,
    [property: JsonRequired] DateTimeOffset EffectiveTo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementEvaluationCommandV1(Guid OperationId, Guid RequirementId, string Action,
    string? ExpectedEtag, string Reason, EngineeringSourceRefV1? Requirement = null,
    RequirementEvaluationPolicyInputV1? Policy = null);
public sealed record RequirementEvaluationScopeV1(Guid EnvironmentId, Guid TenantId, Guid ActorId, string EvidenceMode);
public sealed record RequirementEvaluationInputsV1(EngineeringRequirementSnapshotV1 Requirement,
    EngineeringRequirementSnapshotV1[] RelatedOriginals, UomProfileVersionV1[] Units, string[] Missing);
public sealed record RequirementEvaluationControlV1(Guid RequirementId, long Revision, string State,
    Guid? PolicyId, Guid? PacketId, Guid? ApprovalId, Guid ActorId, string Reason, DateTimeOffset RecordedAt);
public sealed record RequirementEvaluationPolicyV1(Guid Id, Guid RequirementId, Guid ActorId,
    RequirementEvaluationPolicyInputV1 Input, DateTimeOffset RecordedAt);
public sealed record RequirementEvaluationPacketV1(Guid Id, Guid RequirementId, EngineeringSourceRefV1 Requirement,
    EngineeringSourceRefV1 Policy, Guid AuthorId, Guid SubmitterId, string InputDigest,
    RequirementEvaluationInputsV1 Inputs, DateTimeOffset SubmittedAt, string Purpose = "DEFINITION_ONLY");
public sealed record RequirementEvaluationVoteV1(Guid Id, Guid PacketId, Guid ActorId, int[] RoleIds,
    bool Approved, string Comment, string PermissionVersion, string OrganizationVersion, DateTimeOffset SignedAt);
public sealed record RequirementEvaluationApprovalV1(Guid Id, EngineeringSourceRefV1 Requirement,
    EngineeringSourceRefV1 Packet, EngineeringSourceRefV1 Policy, EngineeringSourceRefV1[] Votes,
    string InputDigest, DateTimeOffset ApprovedAt, string Purpose = "DEFINITION_ONLY");
public sealed record RequirementEvaluationRecordV1<T>(EngineeringSourceRefV1 Reference, T Body);
public sealed record RequirementEvaluationViewV1(Guid EnvironmentId, Guid TenantId, string EvidenceMode,
    EngineeringRequirementSnapshotV1 Requirement, EngineeringSourceRefV1? ControlReference,
    RequirementEvaluationControlV1? Control, string Etag,
    RequirementEvaluationRecordV1<RequirementEvaluationPolicyV1>? Policy,
    RequirementEvaluationRecordV1<RequirementEvaluationPacketV1>? Packet,
    RequirementEvaluationRecordV1<RequirementEvaluationVoteV1>[] Votes,
    RequirementEvaluationRecordV1<RequirementEvaluationApprovalV1>? CurrentApproval, string[] Missing, bool ControlIsCurrent = true);
public sealed record RequirementEvaluationResultV1(Guid OperationId, Guid ObjectId, string Action,
    EngineeringSourceRefV1 Requirement, EngineeringSourceRefV1 Control, string State, Guid EnvironmentId, string EvidenceMode);
public sealed record RequirementEvaluationOperationV1(Guid OperationId, string State, RequirementEvaluationResultV1? Result = null);
public sealed record RequirementEvaluationSourceProofV1(Guid EnvironmentId, Guid TenantId, string EvidenceMode,
    EngineeringSourceRefV1 Requirement, EngineeringSourceRefV1? Control, EngineeringSourceRefV1? Approval,
    string Availability, string[] Missing, string InputDigest, string Observation = "READ_ONLY_OBSERVATION",
    string Purpose = "DEFINITION_ONLY");
