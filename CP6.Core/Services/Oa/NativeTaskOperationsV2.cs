using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeSendBackTargetV2(string TargetKind, string? TargetNodeId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeSendBackInputV2(string TargetKind, string? TargetNodeId, string PreviewDigest,
    string ExpectedTaskVersion, string Comment);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeTransferInputV2(Guid TaskId,Guid ToUserId,string Reason,string ExpectedTaskVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeAddSignInputV2(Guid TaskId,Guid ToUserId,string Source,string Reason,string ExpectedTaskVersion);
public sealed record NativeSendBackPreviewV2(Guid TaskId, Guid InstanceId, string TargetKind, string? TargetNodeId,
    string Scope, Guid[] AffectedTaskIds, Guid[] AffectedTokenIds, bool WholeInstanceRefill,
    string TaskVersion, string InstanceVersion, string PreviewDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnedFormEditV2(long ReturnRevision, string ExpectedFormDataVersion, JsonElement DataPatch, string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnedFormResubmitV2(long ReturnRevision, string ExpectedFormDataVersion, string? Comment);
public sealed record ReturnedFormContextV2(Guid InstanceId, Guid FormDataId, long ReturnRevision, Guid ExecutionCycleId,
    Guid FormDefVersionId, Guid FlowDefVersionId, string InstanceVersion, string FormDataVersion,
    string SourceSnapshotRef, string SourceSnapshotHash, string Name, string SchemaJson, string DataJson,
    Dictionary<string,string> FieldMask, bool CanEdit, bool CanResubmit, string[] Reasons,int InstanceStatus);
public sealed record ReturnedFormResultV2(Guid InstanceId, Guid FormDataId, long ReturnRevision, Guid ExecutionCycleId,
    int InstanceStatus, string InstanceVersion, string FormDataVersion, Guid[] TaskIds, string[] ValidationErrors,
    Guid FormDefVersionId,Guid FlowDefVersionId,Guid[] PendingResolutionIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveApproverInputV2(string ExpectedInstanceVersion,Guid ExecutionCycleId,string NodeId,Guid TokenId,
    int StageIndex,int StageRound,Guid[] TargetPrincipalIds,string RoutePolicyRef,string Reason);
public sealed record PendingResolutionContextV2(Guid ResolutionId,string ResolutionVersion,Guid InstanceId,string InstanceVersion,
    Guid ExecutionCycleId,string NodeId,Guid TokenId,int StageIndex,int StageRound,Guid FlowDefVersionId,string RoutePolicyRef,
    string Reason,string State,Guid[] CandidatePrincipalIds,bool CanAssign,string[] BlockingReasons,NativeTaskCandidateV2[] Candidates);
public sealed record NativeTaskCandidateV2(Guid UserId,string Name);
public sealed record NativeSendBackOptionV2(string TargetKind,string? TargetNodeId,string Name);
public sealed record NativeTaskOperationContextV2(ApprovalTaskDecisionViewV2 Task,string[] AllowedActions,
    NativeTaskCandidateV2[] Candidates,NativeSendBackOptionV2[] SendBackTargets);
