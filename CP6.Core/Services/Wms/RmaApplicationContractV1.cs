using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

// This native contract does not represent the formal RMA OriginalFulfilment/entitlement contract.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaApplicationLineV1(Guid OutboundLineId, string Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaApplicationCreateV1(Guid OperationId, Guid ApplicationId, Guid OutboundId,
    string SourceVersion, string Reason, string RequestedOutcome, IReadOnlyList<RmaApplicationLineV1> Lines, string? Remarks);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaApplicationSubmitV1(Guid OperationId, int ExpectedVersion);
public sealed record RmaApplicationSourceLineV1(Guid Id, int LineNumber, string ProductCode,
    string ShippedQuantity, string UnitCode, string WarehouseCode, string? LocationCode, string? LotNumber, string ShipmentTransaction)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OutboundOrderLineSnapshotV1? OrderLine { get; init; }
}
public sealed record RmaApplicationSourceV1(Guid Id, string Number, string CustomerCode, string? OrderNumber,
    string Version, IReadOnlyList<RmaApplicationSourceLineV1> Lines);
public sealed record RmaApplicationSourceChoiceV1(Guid Id, string Number, string? CustomerCode);
public sealed record RmaApplicationRequestedLineV1(RmaApplicationSourceLineV1 Source, string RequestedQuantity);
public sealed record RmaApplicationViewV1(Guid Id, int Version, string State, Guid? RequestedBy, DateTimeOffset RequestedAt,
    Guid OutboundId, string OutboundNumber, string CustomerCode, string SourceVersion, string? Reason,
    string RequestedOutcome, IReadOnlyList<RmaApplicationRequestedLineV1> Lines, string? Remarks, DateTimeOffset? SubmittedAt)
{
    public string Etag => $"\"rma-application-{Id:N}-{Version}\"";
    public string Authorization => "NOT_GRANTED";
}
public sealed record RmaApplicationResultV1(Guid OperationId, Guid ApplicationId, int Version, string State, string Etag);
public sealed record RmaApplicationOperationV1(Guid OperationId, string State, RmaApplicationResultV1? Result = null);
