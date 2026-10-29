using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

public sealed record RmaSourceCoordinateV1(string SourceOwner, FulfilmentRefV1 ShipmentLineRef,
    FulfilmentRefV1? InvoiceLineRef, FulfilmentRefV1? ScheduleRef, FulfilmentUomV1 BaseUom,
    FulfilmentRefV1 SourceCutRef, FulfilmentRefV1 SourceVersionRef, FulfilmentRangeV1[] EligibleRanges, FulfilmentQtyV1 EligibleQuantity);
public sealed record RmaEntitlementV1([property: JsonPropertyName("ref")] FulfilmentRefV1 Reference,
    OriginalFulfilmentV1 Original, FulfilmentRefV1 PolicyRef, string PolicyVersion, DateTimeOffset CapturedAt,
    FulfilmentQtyV1 AuthorizedQty, FulfilmentQtyV1 ReceivedQty, FulfilmentQtyV1 ActiveClaimQty, FulfilmentQtyV1 CreditedQty,
    FulfilmentQtyV1 RemainingAuthorizableQty, string Coverage, FulfilmentRefV1? ExceptionApprovalRef, FulfilmentRefV1 CurrentHeadRef);
public sealed record RmaRangeClaimV1(Guid ClaimId, RmaSourceCoordinateV1 Coordinate, Guid RmaId,
    FulfilmentRefV1 ApplicationLineRef, FulfilmentRefV1 AuthorizationDecisionRef, FulfilmentRangeV1[] ClaimRanges,
    FulfilmentQtyV1 ClaimedQuantity, string State, string ClaimDigest, FulfilmentRefV1? PredecessorClaimRef,
    FulfilmentRefV1? SuccessorClaimRef, FulfilmentRefV1 StateAuthorityRef, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo);
public sealed record RmaReturnShareV1(Guid ShareId, Guid RmaId, FulfilmentRefV1 AuthorizationDecisionRef,
    FulfilmentRefV1 ApplicationLineRef, FulfilmentRefV1 EntitlementSnapshotRef, FulfilmentRefV1 RangeClaimRef,
    OriginalFulfilmentV1 Original, FulfilmentRangeV1[] Ranges, FulfilmentQtyV1 Quantity, string State,
    FulfilmentRefV1? PreviousShareRef, FulfilmentRefV1? CorrectionOfRef);
public sealed record RmaAuthorizationDecisionV1(Guid DecisionId, int DecisionVersion, FulfilmentRefV1 DecisionRef,
    FulfilmentRefV1 ApplicationRef, string Outcome, RmaReturnShareV1[] AuthorizedShares, string[] ReasonCodes,
    FulfilmentRefV1 AuthorityRef, FulfilmentRefV1 DecidedBy, DateTimeOffset DecidedAt, DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo, FulfilmentRefV1? ExceptionApprovalRef, FulfilmentRefV1? PreviousDecisionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaAuthorizedShareInputV1([property: JsonRequired] Guid RequestedLineId,
    [property: JsonRequired] FulfilmentRefV1 EntitlementSnapshotRef, [property: JsonRequired] RmaDeliveryRangeV1[] Ranges,
    [property: JsonRequired] FulfilmentQtyV1 Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaAuthorizationDecideV1([property: JsonRequired] FulfilmentRefV1 ExpectedApplicationHeadRef,
    [property: JsonRequired] FulfilmentRefV1[] ExpectedEntitlementHeadRefs, [property: JsonRequired] string Outcome,
    [property: JsonRequired] RmaAuthorizedShareInputV1[] AuthorizedShares, [property: JsonRequired] FulfilmentRefV1 AuthorityRef,
    [property: JsonRequired] string[] ReasonCodes, [property: JsonRequired] DateTimeOffset ValidFrom,
    [property: JsonRequired] DateTimeOffset? ValidTo, [property: JsonRequired] FulfilmentRefV1? ExceptionApprovalRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaAuthorizationRevokeV1([property: JsonRequired] FulfilmentRefV1 ExpectedDecisionHeadRef,
    [property: JsonRequired] FulfilmentRefV1[] AffectedReturnShareRefs, [property: JsonRequired] FulfilmentRefV1 AuthorityRef,
    [property: JsonRequired] string Reason);
public sealed record RmaAuthorizationLineV1(Guid RequestedLineId, FulfilmentRefV1 ApplicationLineRef,
    RmaEntitlementV1 Entitlement, RmaSourceCoordinateV1 Coordinate, FulfilmentRangeV1[] AvailableRanges, string EvidenceMode);
public sealed record RmaAuthorizationContextV1(FulfilmentRefV1 ApplicationRef, FulfilmentRefV1 AuthorityRef,
    RmaPolicyVersionV1 Policy, RmaAuthorizationLineV1[] Lines, RmaAuthorizationDecisionV1? CurrentDecision, FulfilmentRefV1[] CurrentShareRefs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaAuthorizationQueryV1([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] Guid CommandId);
public sealed record RmaAuthorizationCommandResultV1(string Status, RmaAuthorizationDecisionV1? Value, bool CanInferNoEffect = false);
