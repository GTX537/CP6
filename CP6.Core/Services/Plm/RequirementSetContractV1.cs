using System.Text.Json.Serialization;
namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetMemberV1(EngineeringSourceRefV1 Requirement,
    EngineeringSourceRefV1? EvaluationControl, EngineeringSourceRefV1? Approval,
    string Disposition, string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetConflictV1(string Id,EngineeringSourceRefV1 Left,EngineeringSourceRefV1 Right,
    string Intersection,string Proposal,string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetBodyV1(string Kind,string Name,string Scope,string CompletenessBasis,
    RequirementSetMemberV1[] Members,RequirementSetConflictV1[] Conflicts);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetCommandV1
{
    public Guid OperationId {get;init;}
    public Guid ObjectId {get;init;}
    public string Action {get;init;}="";
    public string? ExpectedVersion {get;init;}
    public Guid? ExpectedEtag {get;init;}
    public string? ExpectedAuthorityVersion {get;init;}
    public Guid? DepartmentId {get;init;}
    public string Reason {get;init;}="";
    public RequirementSetBodyV1 Body {get;init;}=null!;
}
public sealed record RequirementSetSnapshotV1(Guid TenantId,EngineeringSourceRefV1 Reference,Guid Etag,
    bool IsCurrent,RequirementSetBodyV1 Body,Guid AuthorId,Guid ActorId,string? ParentVersion,string Reason,
    DateTimeOffset RecordedAt,string OriginalJson,string[] Missing,string State="DRAFT",
    RequirementSetMemberObservationV1[]? MemberStatus=null,RequirementSetDifferenceV1[]? Differences=null);
public sealed record RequirementSetScopeV1(Guid EnvironmentId,Guid TenantId,Guid ActorId,string EvidenceMode);
public sealed record RequirementSetMemberObservationV1(EngineeringRequirementSnapshotV1 Requirement,
    RequirementEvaluationSourceProofV1 Evaluation,EngineeringRequirementSnapshotV1 Source);
public sealed record RequirementSetDifferenceV1(Guid RequirementId,string Change,RequirementSetMemberV1? Previous,RequirementSetMemberV1? Current);
public sealed record RequirementSetResultV1(Guid OperationId,string Kind,Guid ObjectId,string Version,string Digest);
public sealed record RequirementSetOperationV1(Guid OperationId,string State,RequirementSetResultV1? Result=null);
internal sealed record RequirementSetStoredV1(Guid TenantId,Guid Id,string Version,Guid CreatorId,Guid Etag,
    RequirementSetBodyV1 Body,Guid OperationId,string Action,Guid ActorId,string? ParentVersion,string Reason,DateTimeOffset RecordedAt);
