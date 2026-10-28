using System.Text.Json;
using System.Text.Json.Serialization;
namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationCandidateCommandV1(Guid OperationId,Guid IterationId,Guid CandidateId,string Action,
    EngineeringSourceRefV1 ExpectedIteration,Guid ExpectedIterationEtag,string? ExpectedVersion,Guid? ExpectedEtag,
    string Title,string ChangeSummary,string Assumptions,EngineeringSourceRefV1 SourceCheck,
    EngineeringSourceRefV1? ParentCandidate,string SourceDecision,string DriftReason,string Reason);
public sealed record IterationCandidateDefinitionV1(string Title,string ChangeSummary,string Assumptions,
    EngineeringSourceRefV1? ParentCandidate,EngineeringSourceRefV1? ParentBaseline,
    EngineeringSourceRefV1 Iteration,string InputVersion,EngineeringSourceRefV1 RequirementSet,
    EngineeringProductRefV1 Product,EngineeringSourceRefV1 Site,EngineeringSourceRefV1 SourceCheck,
    EngineeringRevisionRefV1 EngineeringRevision,EngineeringRevisionRefV1 EngineeringCandidate,
    EngineeringCheckRunV1 TechnicalSource,string TechnicalSourceOriginal,string SourceDecision,string DriftReason);
public sealed record IterationCandidateChangeV1(string Path,JsonElement? Before,JsonElement? After);
public sealed record IterationCandidateStoredV1(Guid EnvironmentId,string EvidenceMode,Guid TenantId,Guid Id,Guid IterationId,
    string Version,Guid CreatorId,Guid ActorId,Guid Etag,Guid OperationId,string Action,string? PreviousVersion,
    IterationCandidateDefinitionV1 Definition,IterationCandidateChangeV1[] PreviousChanges,IterationCandidateChangeV1[] ParentChanges,
    string Reason,DateTimeOffset RecordedAt);
public sealed record IterationCandidateSnapshotV1(EngineeringSourceRefV1 Reference,bool IsCurrent,
    IterationCandidateStoredV1 Original,string OriginalJson,string SourceDrift,string[] Findings,
    string Review="NOT_SUBMITTED",string Freeze="NOT_REQUESTED");
public sealed record IterationCandidateResultV1(Guid OperationId,Guid IterationId,Guid CandidateId,string Version,string Digest);
public sealed record IterationCandidateOperationV1(Guid OperationId,string State,IterationCandidateResultV1? Result=null);
public sealed record IterationCandidateDiffV1(EngineeringSourceRefV1 Before,EngineeringSourceRefV1 After,IterationCandidateChangeV1[] Changes);
