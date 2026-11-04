using System.Text.Json.Serialization;
namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementImpactRequestCommandV1(Guid OperationId,EngineeringSourceRefV1 Candidate,
    string Action,string ExpectedEtag,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationImpactAssessmentCommandV1(Guid OperationId,Guid CaseId,EngineeringSourceRefV1 Iteration,
    Guid? ExpectedAssessmentId,string Decision,string Reason);
public sealed record RequirementImpactReceiptV1(Guid RequestId,string Action,Guid[] CreatedCases);
public sealed record RequirementImpactRequestV1(Guid Id,EngineeringSourceRefV1 Candidate,string Action,
    Guid ActorId,string Reason,DateTimeOffset RecordedAt,Guid? PreviousId);
public sealed record IterationImpactAssessmentV1(Guid Id,Guid CaseId,EngineeringSourceRefV1 Iteration,string InputVersion,
    string PlanVersion,string Decision,Guid ActorId,string Reason,DateTimeOffset RecordedAt,Guid? PreviousId);
public sealed record RequirementImpactCaseV1(Guid Id,Guid RequestId,EngineeringSourceRefV1 Candidate,
    EngineeringSourceRefV1 CapturedIteration,EngineeringSourceRefV1 Adopted,string InputVersion,string CapturedPlanVersion,
    string CapturedStage,Guid ActorId,DateTimeOffset RecordedAt,IterationImpactAssessmentV1? Assessment,
    IterationImpactAssessmentV1[]? AssessmentHistory=null);
public sealed record RequirementImpactConsumerV1(EngineeringSourceRefV1 Iteration,string Name,EngineeringSourceRefV1 Adopted,
    string InputVersion,string PlanVersion,string Stage,string State,RequirementImpactCaseV1? Case);
public sealed record RequirementImpactContextV1(EngineeringSourceRefV1 Candidate,string Coverage,string Etag,
    RequirementImpactRequestV1? Request,RequirementImpactConsumerV1[] Consumers,string CandidateState="DRAFT",bool IsCurrent=true);
public sealed record IterationImpactContextV1(EngineeringSourceRefV1 Iteration,RequirementImpactConsumerV1[] Cases);
public sealed record RequirementSuccessorChangeV1(Guid? RequirementId,string Area,string Item,string Change,string? Before,string? After);
public sealed record RequirementSuccessorDifferenceV1(EngineeringSourceRefV1 Before,EngineeringSourceRefV1 After,
    RequirementSuccessorChangeV1[] Changes);
