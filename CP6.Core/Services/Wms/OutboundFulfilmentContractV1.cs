using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FulfilmentRefV1([property: JsonRequired] string Owner, [property: JsonRequired] string Id,
    [property: JsonRequired] string Version, [property: JsonRequired] string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FulfilmentUomV1([property: JsonRequired] string Code, [property: JsonRequired] string Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FulfilmentQtyV1([property: JsonRequired] string Value, [property: JsonRequired] FulfilmentUomV1 Uom);
public sealed record FulfilmentRangeV1(string From, string To);

// Field shape of WMS-RMA-01 OriginalFulfilmentRef. The enclosing source reports its actual coverage.
public sealed record OriginalFulfilmentV1(FulfilmentRefV1 ShipmentRef, string ShipmentLineId, string ShipmentLineVersion,
    FulfilmentRefV1 OrderRef, string OrderLineId, FulfilmentRefV1? ScheduleRef, FulfilmentRefV1? DeliveryProofRef,
    FulfilmentRefV1? InvoiceLineRef, FulfilmentRefV1? CommercialSnapshotRef, FulfilmentRefV1 ProductRef,
    IReadOnlyList<FulfilmentRangeV1> SourceShareRanges, FulfilmentQtyV1 BaseQty,
    IReadOnlyList<FulfilmentRefV1> LotRefs, IReadOnlyList<FulfilmentRefV1> SerialRefs, IReadOnlyList<FulfilmentRefV1> LpnRefs);

// OriginalJson is the exact source-owned body whose digest is in Reference; it is not re-encoded on read.
public sealed record FulfilmentFactV1(FulfilmentRefV1 Reference, string OriginalJson);
public sealed record OutboundFulfilmentSnapshotV1(Guid OutboundId, string OutboundNumber, string CustomerCode,
    DateTimeOffset CapturedAt, IReadOnlyList<OriginalFulfilmentV1> Lines, IReadOnlyList<FulfilmentFactV1> Facts,
    string Coverage, IReadOnlyList<string> Unobserved)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FulfilmentRefV1? CustomerRef { get; init; }
}
