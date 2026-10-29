using System.Text.Json.Serialization;
using CP6.Core.Services.Mes;

namespace CP6.Core.Services.Wms;

public sealed record ReturnCorrectionTargetV1(string Axis,Guid Id,FulfilmentRefV1 Reference,string Kind,string Resource,
    RmaQualitySourceV1 Source,Guid? DispositionWorkId,Guid OriginalMovementId,Guid[] MovementIds,
    FulfilmentRangeV1[] Ranges,RmaRootRangeMapV1[] RootMap,ReturnStockDimensionsV1 Dimensions,
    ReturnDispositionStockKeyV2 SourcePool,ReturnDispositionStockKeyV2? DestinationPool,FulfilmentRefV1? QualityReleaseRef);
public sealed record ReturnCorrectionPolicyV1(FulfilmentRefV1 Reference,string Label,FulfilmentRefV1[] ErrorEvidenceRefs,
    Guid ApproverId,DateTimeOffset ValidFrom,DateTimeOffset ValidTo,bool Simulation);
public sealed record ReturnCorrectionChoiceV1(ReturnCorrectionTargetV1 Target,FulfilmentRangeV1[] AvailableRanges,
    ReturnCorrectionPolicyV1? Policy);
public sealed record ReturnCorrectionCandidateV1(Guid Id,Guid ApplicationId,Guid CreatorId,int Version,
    ReturnCorrectionTargetV1 Target,FulfilmentRangeV1[] Ranges,FulfilmentQtyV1 Quantity,string Reason,
    FulfilmentRefV1 ErrorEvidenceRef,DateTimeOffset RequestedAt,string ContentDigest);
public sealed record ReturnCorrectionApprovalV1(FulfilmentRefV1 Reference,Guid ProposalId,int Version,string ContentDigest,
    FulfilmentRefV1 PolicyRef,Guid ApproverId,DateTimeOffset ValidFrom,DateTimeOffset ValidTo,bool Simulation);
public sealed record ReturnCorrectionPreparationV1(FulfilmentRefV1 Reference,string ExecutionKey,
    FulfilmentRefV1 SnapshotRef,ReturnStockPoolVersionV1[] ExpectedPools,ReturnStockSpanV1[] Spans,
    FulfilmentRefV1 ApprovalRef,FulfilmentRefV1 SourceClosureRef,DateTimeOffset PreparedAt);
public sealed record ReturnCorrectionReceiptV1(FulfilmentRefV1 Reference,Guid ProposalId,FulfilmentRefV1 OriginalEffectRef,
    FulfilmentRangeV1[] Ranges,FulfilmentQtyV1 Quantity,FulfilmentRefV1 SourceClosureRef,FulfilmentRefV1 SuccessorRef,
    FulfilmentRefV1? QualityHeadRef,FulfilmentRefV1? QualityReleaseCorrectionRef,ReturnStockResponseV1 StockResult,DateTimeOffset AppliedAt);
public sealed record ReturnCorrectionViewV1(ReturnCorrectionCandidateV1 Candidate,FulfilmentRefV1 HeadRef,string State,
    ReturnCorrectionApprovalV1? Approval,ReturnCorrectionPreparationV1? Preparation,ReturnCorrectionReceiptV1? Result,
    FulfilmentRefV1? NoEffectProof,FulfilmentRangeV1[] BudgetRemainingRanges,string[] AllowedActions);
public sealed record ReturnCorrectionContextV1(ReturnCorrectionChoiceV1[] Targets,ReturnCorrectionViewV1[] Proposals,string[] Capabilities);
public sealed record ReturnCorrectionCommandResultV1(string Action,ReturnCorrectionViewV1 Value);
public sealed record ReturnCorrectionQueryResultV1(string Status,ReturnCorrectionCommandResultV1? Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnCorrectionDraftV1([property:JsonRequired] string Axis,[property:JsonRequired] Guid TargetId,
    [property:JsonRequired] FulfilmentRefV1 ExpectedTargetRef,[property:JsonRequired] RmaDeliveryRangeV1[] Ranges,
    [property:JsonRequired] FulfilmentRefV1 ErrorEvidenceRef,[property:JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnCorrectionEditV1([property:JsonRequired] FulfilmentRefV1 ExpectedHeadRef,
    [property:JsonRequired] RmaDeliveryRangeV1[] Ranges,[property:JsonRequired] FulfilmentRefV1 ErrorEvidenceRef,[property:JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnCorrectionActionV1([property:JsonRequired] FulfilmentRefV1 ExpectedHeadRef,[property:JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnCorrectionQueryV1([property:JsonRequired] Guid ApplicationId,[property:JsonRequired] Guid CommandId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnCorrectionSimulationCaseV1([property:JsonRequired] Guid TenantId,[property:JsonRequired] Guid SiteId,
    [property:JsonRequired] FulfilmentRefV1 OriginalEffectRef,[property:JsonRequired] string Version,[property:JsonRequired] string Label,
    [property:JsonRequired] Guid IndependentApproverId,[property:JsonRequired] DateTimeOffset ValidFrom,[property:JsonRequired] DateTimeOffset ValidTo,
    [property:JsonRequired] bool RecordedPhysicalEffectDidNotOccur,[property:JsonRequired] FulfilmentRefV1[] ErrorEvidenceRefs);
public sealed class ReturnCorrectionSimulationOptionsV1
{
    public bool Enabled { get; set; }
    public string CasesJson { get; set; } = "[]";
}
