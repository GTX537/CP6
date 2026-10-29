using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPhysicalReturnLineInputV1([property: JsonRequired] Guid LineId,
    [property: JsonRequired] FulfilmentRefV1 ReturnShareRef, [property: JsonRequired] RmaDeliveryRangeV1[] Ranges,
    [property: JsonRequired] FulfilmentQtyV1 Quantity, [property: JsonRequired] FulfilmentRefV1 LocationOptionRef,
    [property: JsonRequired] FulfilmentRefV1[] LotRefs, [property: JsonRequired] FulfilmentRefV1[] SerialRefs,
    [property: JsonRequired] FulfilmentRefV1[] LpnRefs, [property: JsonRequired] DateTimeOffset OccurredAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaRegisterPhysicalReturnV1([property: JsonRequired] FulfilmentRefV1 ExpectedAuthorizationHeadRef,
    [property: JsonRequired] FulfilmentRefV1 ExpectedRmaHeadRef, [property: JsonRequired] RmaPhysicalReturnLineInputV1[] ReceiptLines,
    [property: JsonRequired] string? Remarks);
public sealed record RmaReceiptLocationV1(FulfilmentRefV1 OptionRef, Guid WarehouseId, Guid LocationId,
    string WarehouseCode, string LocationCode, string Label);
public sealed record RmaReceiptShareV1(FulfilmentRefV1 ShareRef, RmaReturnShareV1 Share, FulfilmentRangeV1[] AvailableRanges);
public sealed record RmaPhysicalReturnContextV1(FulfilmentRefV1 RmaRef, FulfilmentRefV1 HeadRef,
    RmaAuthorizationDecisionV1? Authorization, RmaReceiptShareV1[] Shares, RmaReceiptLocationV1[] Locations,
    RmaPhysicalWorkViewV1[] Works);
public sealed record RmaPhysicalLineV1(FulfilmentRefV1 Reference, RmaPhysicalReturnLineInputV1 Input,
    RmaReturnShareV1 Share, RmaReceiptLocationV1 Location);
public sealed record RmaPhysicalWorkOriginalV1(Guid WorkId, Guid ApplicationId, Guid ActorId,
    FulfilmentRefV1 AuthorizationRef, FulfilmentRefV1 ApplicationRef, RmaPhysicalLineV1[] Lines,
    string? Remarks, DateTimeOffset RecordedAt);
public sealed record RmaPhysicalWorkViewV1(FulfilmentRefV1 WorkRef, RmaApplicationWorkV1 Work,
    RmaPhysicalLineV1[] Lines, FulfilmentRefV1 RmaHeadRef, RmaInboundPreparationV1[] Preparations,
    RmaInboundReceiptResultV1[] Results, FulfilmentRefV1? NoEffectProofRef);
public sealed record RmaPhysicalReceiptV1(FulfilmentRefV1 ReceiptOperationRef, FulfilmentRefV1 ReturnShareRef,
    string ReceiptLineId, FulfilmentRefV1 MovementRef, Guid WarehouseId, Guid LocationId, FulfilmentRefV1 LocationEvidenceRef,
    FulfilmentRangeV1[] ReceivedRanges, FulfilmentQtyV1 Quantity, FulfilmentRefV1[] LotRefs, FulfilmentRefV1[] SerialRefs,
    FulfilmentRefV1[] LpnRefs, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, string ReceiptState,
    FulfilmentRefV1 StockApplicationReceiptRef, FulfilmentRefV1 InboundOwnerReceiptRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPhysicalWorkCommandV1([property: JsonRequired] FulfilmentRefV1 ExpectedWorkRef,
    [property: JsonRequired] FulfilmentRefV1 ExpectedRmaHeadRef, [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPhysicalCommandQueryV1([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] Guid CommandId);
public sealed record RmaPhysicalCommandResultV1(string Status, RmaPhysicalWorkViewV1? Value, bool CanInferNoEffect = false);
