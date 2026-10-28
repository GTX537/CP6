using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Mes;

public sealed record QualitySpecificationRuntimeV1(string EvidenceMode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityRequirementV1(string Id, string Characteristic, string Method, string Definition,
    EngineeringSourceRefV1 StandardDocument, UomProfileRefV1? Unit, string? LowerLimit, string? UpperLimit,
    string? Target, string Applicability, EngineeringSourceRefV1? ApplicabilityBasis,
    [property: JsonRequired] bool Mandatory, [property: JsonRequired] bool Waivable, EngineeringSourceRefV1? WaiverBasis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualitySpecificationBodyV1(string Name, EngineeringProductRefV1 Product,
    EngineeringSourceRefV1 Site, string Stage, string[] Purposes, EngineeringSourceRefV1 CompletenessDocument,
    string CompletenessStatement, QualityRequirementV1[] Requirements);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityReviewRoleV1(string Code, int RoleId, int MinimumApprovals);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityReviewPolicyInputV1(string State, QualityReviewRoleV1[] Roles,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualitySpecificationCommandV1(Guid OperationId, Guid SpecificationId, string Action,
    string? ExpectedEtag, string Reason, QualitySpecificationBodyV1? Body = null, QualityReviewPolicyInputV1? Policy = null);
public sealed record QualitySpecificationScopeV1(Guid TenantId, Guid ActorId, Guid EnvironmentId, string EvidenceMode);
public sealed record QualitySpecificationVersionV1(Guid EnvironmentId, Guid TenantId, EngineeringSourceRefV1 Reference,
    Guid CreatorId, Guid AuthorId, string EvidenceMode, QualitySpecificationBodyV1 Body,
    string? PreviousVersion, DateTimeOffset RecordedAt, string OriginalJson);
public sealed record QualitySpecificationControlV1(Guid SpecificationId, long Revision, string DraftVersion,
    string State, Guid? PolicyId, Guid? PacketId, Guid? ApprovalId, Guid ActorId, string Reason, DateTimeOffset RecordedAt);
public sealed record QualityReviewPolicyV1(Guid Id, Guid SpecificationId, Guid ActorId,
    QualityReviewPolicyInputV1 Input, DateTimeOffset RecordedAt);
public sealed record QualityReviewPacketV1(Guid Id, EngineeringSourceRefV1 Specification, EngineeringSourceRefV1 Policy,
    Guid AuthorId, Guid SubmitterId, string RequirementsDigest, DateTimeOffset SubmittedAt);
public sealed record QualityReviewVoteV1(Guid Id, Guid PacketId, Guid ActorId, int[] RoleIds,
    bool Approved, string Comment, string PermissionVersion, string OrganizationVersion, DateTimeOffset SignedAt);
public sealed record QualitySpecificationApprovalV1(Guid Id, EngineeringSourceRefV1 Specification,
    EngineeringSourceRefV1 Packet, EngineeringSourceRefV1 Policy, EngineeringSourceRefV1[] Votes,
    string RequirementsDigest, string CompletenessStatement, DateTimeOffset ApprovedAt);
public sealed record QualitySpecificationRecordV1<T>(EngineeringSourceRefV1 Reference, T Body);
public sealed record QualitySpecificationViewV1(QualitySpecificationVersionV1 Version,
    EngineeringSourceRefV1 ControlReference, QualitySpecificationControlV1 Control, string Etag,
    QualitySpecificationRecordV1<QualityReviewPolicyV1>? Policy,
    QualitySpecificationRecordV1<QualityReviewPacketV1>? Packet,
    QualitySpecificationRecordV1<QualityReviewVoteV1>[] Votes,
    QualitySpecificationRecordV1<QualitySpecificationApprovalV1>? CurrentApproval, string[] Missing);
public sealed record QualitySpecificationResultV1(Guid OperationId, Guid ObjectId, string Action,
    EngineeringSourceRefV1 Specification, EngineeringSourceRefV1 Control, string State);
public sealed record QualitySpecificationOperationV1(Guid OperationId, string State, QualitySpecificationResultV1? Result = null);
public sealed record QualitySpecificationSourceProofV1(EngineeringSourceRefV1 Specification,
    EngineeringSourceRefV1 Control, EngineeringSourceRefV1? Approval, string RequirementsDigest,
    string Availability, string[] Missing, QualityRequirementV1[] Requirements,
    string Observation = "READ_ONLY_OBSERVATION", string? OriginalSourceJson = null);
