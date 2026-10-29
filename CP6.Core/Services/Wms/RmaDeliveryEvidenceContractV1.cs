using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaDeliveryRangeV1([property: JsonRequired] string From, [property: JsonRequired] string To);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaDeliveryEvidenceRecordV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] FulfilmentRefV1 ShipmentRef, [property: JsonRequired] Guid ShipmentLineId,
    [property: JsonRequired] Guid EvidenceId,
    [property: JsonRequired] string DocumentReference, [property: JsonRequired] DateTimeOffset OccurredAt,
    [property: JsonRequired] RmaDeliveryRangeV1[] Ranges, [property: JsonRequired] FulfilmentRefV1[] FileRefs,
    [property: JsonRequired] string? Notes);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaDeliveryEvidenceWithdrawV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] FulfilmentRefV1 EvidenceRef, [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaDeliveryEvidenceQueryV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] FulfilmentRefV1 ShipmentRef, [property: JsonRequired] Guid ShipmentLineId,
    [property: JsonRequired] int Page, [property: JsonRequired] int PageSize);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaEvidenceFileQueryV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] string? Keyword, [property: JsonRequired] int Page);
public sealed record RmaDeliveryEvidenceV1(FulfilmentRefV1 Reference, RmaTrustedScopeV1 Scope, Guid OutboundId,
    Guid ActorId, OriginalFulfilmentV1 Original, string DocumentReference, DateTimeOffset OccurredAt,
    FulfilmentRangeV1[] Ranges, FulfilmentQtyV1 ReportedQuantity, FulfilmentRefV1[] FileRefs, string? Notes,
    DateTimeOffset RecordedAt, FulfilmentRefV1 RecordedBy);
public sealed record RmaDeliveryEvidenceWithdrawalV1(FulfilmentRefV1 EvidenceRef, string Reason,
    DateTimeOffset RecordedAt, FulfilmentRefV1 RecordedBy);
public sealed record RmaDeliveryEvidenceViewV1(RmaDeliveryEvidenceV1 Evidence, RmaDeliveryEvidenceWithdrawalV1? Withdrawal);
public sealed record RmaDeliveryEvidencePageV1(RmaDeliveryEvidenceViewV1[] Items, bool HasMore,
    string Coverage = "COLLECTED_EVIDENCE_ONLY", bool EstablishesDeliveryProof = false);
public sealed record RmaDeliveryEvidenceCommandV1(string Status, RmaDeliveryEvidenceViewV1? Value, bool CanInferNoEffect = false);
public sealed record RmaEvidenceFileV1(FulfilmentRefV1 Reference, string Name, string MediaType, string Availability);
public sealed record RmaEvidenceFilePageV1(RmaEvidenceFileV1[] Items, bool HasMore);
