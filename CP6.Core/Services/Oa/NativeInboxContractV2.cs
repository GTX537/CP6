namespace CP6.Core.Services.Oa;

public sealed record NativeInboxItemV2(Guid InstanceId,Guid RootInstanceId,string ContextKind,string FlowKey,
    int InstanceStatus,string? Title,string? BusinessType,Guid[] TaskIds,Guid[] CcIds,int UnreadCount,DateTime CreatedAt);
public sealed record NativeInboxPageV2(NativeInboxItemV2[] Items,int TotalItems,int TotalGroups,int Page,int PageSize,
    string RowMode,Guid ActualUserId,Guid EffectiveUserId,string AuthorizationProfile="CURRENT_DIRECT_PARTICIPANT_V1");
public sealed record NativeInboxCountsV2(int Pending,int Cc,int Running,int Done,int UnreadTasks,int UnreadCc);
public sealed record NativeInstancePinV2(Guid InstanceId,Guid? ParentInstanceId,Guid FlowVersionId,Guid? FormVersionId,
    string FlowKey,int Status,string InstanceRowVersion);
public sealed record NativeReadFactV2(string Kind,Guid TargetId,Guid PrincipalId,Guid ActualUserId,
    DateTimeOffset FirstReadAtUtc,string FirstCommandId,Guid? GrantId=null,string? GrantRevision=null);
public sealed record NativeTaskReadV2(Guid TaskId,Guid InstanceId,string NodeId,Guid? TokenId,int StageIndex,int StageRound,
    Guid PrincipalId,int Status,string TaskRowVersion,NativeReadFactV2? Read,bool CanRequestDecisionContext,Guid? CreatedPrincipalId=null);
public sealed record NativeCcReadV2(Guid CcId,Guid InstanceId,Guid RecipientId,string? NodeId,NativeReadFactV2? Read);
public sealed record NativeWorkflowEventV2(Guid EventId,Guid InstanceId,string NodeId,string Action,Guid ActorId,
    string ActorKind,string? Comment,DateTime RecordedAt);
public sealed record NativeVoteReadV2(Guid VoteId,Guid TaskId,Guid InstanceId,bool Approved,string Comment,Guid PrincipalId,
    Guid ActualUserId,Guid? OnBehalfOfId,DateTimeOffset OccurredAtUtc,string EvidenceKind="HUMAN");
public sealed record NativeBusinessReadV2(string Owner,string BusinessType,string SubjectKind,string SubjectId,
    string BusinessRevision,Guid SubmissionId,string? DetailRoute,string ApplicationState="UNPROVEN",
    Guid? ApplicationReceiptId=null,string? ReceiptRevision=null,ApprovalRefV2? BusinessResultRef=null,bool PendingReceiptGap=false);
public sealed record NativeInstanceReadV2(Guid InstanceId,Guid RootInstanceId,string ContextKind,Guid ActualUserId,
    Guid EffectiveUserId,NativeInstancePinV2[] Graph,NativeTaskReadV2[] Tasks,NativeCcReadV2[] Cc,
    NativeWorkflowEventV2[] History,NativeVoteReadV2[] Votes,NativeFormContentV2? Form,NativeBusinessReadV2? Business,
    NativeTimeoutDiagnosticsV2? Timeout=null);
public sealed record NativeReadCommandResultV2(string CommandId,string State,NativeReadFactV2? Result,string QueryRef);
