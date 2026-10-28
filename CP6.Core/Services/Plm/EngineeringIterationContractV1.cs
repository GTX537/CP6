using System.Text.Json.Serialization;
namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationMilestoneV1(Guid Id,string Name,string Criterion,DateOnly DueDate);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationProjectInputV1(string Name,Guid OwnerId,Guid[] Members,EngineeringSourceRefV1? Site,
    EngineeringProductRefV1? Product,string Objective,DateOnly? StartDate,DateOnly? EndDate,IterationMilestoneV1[] Milestones);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationPlanV1(string Name,Guid OwnerId,string Goal,DateOnly StartDate,DateOnly EndDate);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationTaskPlanV1(Guid Id,string Title,Guid OwnerId,Guid ConfirmerId,string Weight,DateOnly DueDate,string Acceptance);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringIterationCommandV1
{
    public Guid OperationId {get;init;}
    public Guid ObjectId {get;init;}
    public string Action {get;init;}="";
    public string? ExpectedVersion {get;init;}
    public Guid? ExpectedEtag {get;init;}
    public string? ExpectedAuthorityVersion {get;init;}
    public Guid? DepartmentId {get;init;}
    public string Reason {get;init;}="";
    public IterationProjectInputV1? Project {get;init;}
    public Guid? ProjectId {get;init;}
    public IterationPlanV1? Plan {get;init;}
    public EngineeringRevisionOriginV1? Origin {get;init;}
    public EngineeringSourceRefV1? RequirementSet {get;init;}
    public IterationTaskPlanV1? Task {get;init;}
    public Guid? TaskId {get;init;}
    public EngineeringSourceRefV1[]? Deliverables {get;init;}
}
public sealed record IterationTaskV1(IterationTaskPlanV1 Plan,string State,EngineeringSourceRefV1[] Deliverables,
    Guid? SubmittedBy,Guid? ConfirmedBy,DateTimeOffset? ConfirmedAt,string LastReason);
public sealed record IterationInputCaptureV1(RequirementSetSourceProofV1 Proof,string RequirementSetOriginal,DateTimeOffset AdoptedAt,Guid AdoptedBy);
public sealed record IterationProjectV1(IterationProjectInputV1 Definition,string State);
public sealed record IterationWorkV1(Guid ProjectId,EngineeringSourceRefV1 ProjectReference,IterationPlanV1 Plan,EngineeringRevisionOriginV1 Origin,
    string Stage,string HoldState,string PlanVersion,string InputVersion,IterationInputCaptureV1 Input,IterationTaskV1[] Tasks);
public sealed record EngineeringIterationBodyV1(string Kind,string Name,string Number,IterationProjectV1? Project,IterationWorkV1? Iteration);
public sealed record IterationProgressV1(string ConfirmedWeight,string PlannedWeight,string? Percent);
public sealed record EngineeringIterationSnapshotV1(Guid TenantId,EngineeringSourceRefV1 Reference,Guid Etag,bool IsCurrent,
    EngineeringIterationBodyV1 Body,Guid AuthorId,Guid ActorId,string? ParentVersion,string Reason,DateTimeOffset RecordedAt,
    string OriginalJson,string[] Missing,string SourceDrift="NOT_APPLICABLE",IterationProgressV1? Progress=null);
public sealed record EngineeringIterationResultV1(Guid OperationId,string Kind,Guid ObjectId,string Version,string Digest);
public sealed record EngineeringIterationOperationV1(Guid OperationId,string State,EngineeringIterationResultV1? Result=null);
public sealed record IterationMemberChoiceV1(Guid Id,string Name);
internal sealed record EngineeringIterationStoredV1(Guid TenantId,Guid Id,string Version,Guid CreatorId,Guid Etag,
    EngineeringIterationBodyV1 Body,Guid OperationId,string Action,Guid ActorId,string? ParentVersion,string Reason,DateTimeOffset RecordedAt);

internal sealed record EngineeringIterationSaveV1(Guid OperationId,Guid ObjectId,string Action,string? ExpectedVersion,Guid? ExpectedEtag,string Reason,EngineeringIterationBodyV1 Body);
