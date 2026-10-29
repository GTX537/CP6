using System.Text.Json.Serialization;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Mes;

// Scoped QA-NCR-01 return-quality adapter v1. Decision wire preserves the RMA canonical four-state contract.
public sealed record ReturnQualityPartitionV1(string Quality, FulfilmentRangeV1[] Ranges, FulfilmentQtyV1 Quantity,
    FulfilmentRefV1[] BasisRefs, string[] ReasonCodes);
public sealed record ReturnQualityDecisionV1(string Owner, FulfilmentRefV1 DecisionRef, string DecisionVersion,
    FulfilmentRefV1 ScopeRef, FulfilmentRefV1[] PhysicalReceiptRefs, ReturnQualityPartitionV1[] Partitions,
    FulfilmentRangeV1[] VoidRanges, FulfilmentRefV1 CurrentHeadProof, string ResultDigest, string DecisionState,
    FulfilmentRefV1? PreviousDecisionRef, DateTimeOffset RecordedAt, DateTimeOffset TerminalAt);
public sealed record ReturnInspectionRequestV1(string RequestKey, FulfilmentRefV1 ScopeRef, FulfilmentRefV1 OwnerContractRef,
    FulfilmentRefV1 RmaRef, FulfilmentRefV1[] PhysicalReceiptRefs, FulfilmentRangeV1[] ReceiptRanges,
    FulfilmentQtyV1 Quantity, FulfilmentRefV1 QualityPolicyRef, DateTimeOffset OccurredAt,
    FulfilmentRefV1[] SourceVersionRefs, FulfilmentRefV1? ExpectedOwnerHeadRef);
public sealed record RmaQualitySourceV1(Guid ApplicationId, Guid LineId, Guid RegistrarId, Guid SiteId, Guid OutboundId,
    FulfilmentRefV1 Reference, FulfilmentRefV1 RmaRef, FulfilmentRefV1 PhysicalHeadRef, RmaPhysicalReceiptV1 Receipt,
    FulfilmentRefV1 QualityPolicyRef);
public sealed record ReturnQualityInspectionV1(Guid Id, FulfilmentRefV1 Reference, Guid CreatorId,
    RmaQualitySourceV1 Source, ReturnInspectionRequestV1 Request, DateTimeOffset CreatedAt);
public sealed record ReturnQualityDecisionRecordV1(ReturnQualityDecisionV1 Result, Guid ActorId,
    FulfilmentRefV1 PolicyRef, string Reason);
public sealed record RmaQualityObservationV1(FulfilmentRefV1 Reference, Guid InspectionId,
    ReturnQualityDecisionV1 Decision, Guid ActorId, DateTimeOffset ObservedAt);
public sealed record ReturnQualityViewV1(ReturnQualityInspectionV1 Inspection, FulfilmentRefV1 HeadRef,
    ReturnQualityDecisionRecordV1? Decision, RmaQualityObservationV1? Observation, bool ObservationCurrent);
public sealed record ReturnQualityChoiceV1(RmaQualitySourceV1 Source, ReturnQualityViewV1? Inspection,
    ReturnQualityPolicyV1? Policy);
public sealed record ReturnQualityContextV1(Guid ApplicationId, ReturnQualityChoiceV1[] Receipts, string[] Capabilities);
public sealed record ReturnQualityCommandResultV1(string Action, ReturnQualityViewV1 Value);
public sealed record ReturnQualityQueryResultV1(string Status, ReturnQualityCommandResultV1? Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualityStartV1([property: JsonRequired] Guid LineId,
    [property: JsonRequired] FulfilmentRefV1 ExpectedSourceRef, [property: JsonRequired] FulfilmentRefV1 ExpectedRmaHeadRef,
    [property: JsonRequired] FulfilmentRefV1 ExpectedPolicyRef, [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualityPartitionInputV1([property: JsonRequired] string Quality,
    [property: JsonRequired] RmaDeliveryRangeV1[] Ranges, [property: JsonRequired] FulfilmentRefV1[] BasisRefs,
    [property: JsonRequired] string[] ReasonCodes);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualityDecideV1([property: JsonRequired] FulfilmentRefV1 ExpectedHeadRef,
    [property: JsonRequired] FulfilmentRefV1 ExpectedPolicyRef, [property: JsonRequired] ReturnQualityPartitionInputV1[] Partitions,
    [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualityWithdrawV1([property: JsonRequired] FulfilmentRefV1 ExpectedHeadRef,
    [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaObserveQualityV1([property: JsonRequired] FulfilmentRefV1 ExpectedDecisionRef,
    [property: JsonRequired] FulfilmentRefV1 ExpectedRmaHeadRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualityQueryV1([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] Guid CommandId);
public sealed record ReturnQualityPolicyV1(FulfilmentRefV1 Reference, bool RequireIndependentRegistrar,
    FulfilmentRefV1[] EvidenceRefs, bool Simulation);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnQualitySimulationCaseV1([property: JsonRequired] Guid TenantId, [property: JsonRequired] Guid SiteId,
    [property: JsonRequired] FulfilmentRefV1 QualityPolicyRef, [property: JsonRequired] string Version,
    [property: JsonRequired] bool RequireIndependentRegistrar, [property: JsonRequired] FulfilmentRefV1[] EvidenceRefs);
public sealed class ReturnQualitySimulationOptionsV1
{
    public bool Enabled { get; set; }
    public string CasesJson { get; set; } = "[]";
}
