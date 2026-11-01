using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms.Transfer;

// Native Stock pool adapter; full TF Root/Slice/Claim wire retains its separate contract.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferStageSelectionV1(Guid RequestLineId, Guid StockId, string RequestFrom, string Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferStagePrepareV1(Guid OperationId, string ExpectedRevision, string Etag, string ContentDigest,
    string Device, string ScanOccurrenceKey, TransferStageSelectionV1[] Lines, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferStageDecisionV1(Guid OperationId, string ExpectedRevision, string Etag, string StageDigest,
    [property: JsonRequired] bool PhysicalNotOccurred, string Reason);
public sealed record TransferStockChoiceV1(Guid RequestLineId, Guid StockId, string Lot, string Available, string UnitCode);
public sealed record TransferStageLineV1(TransferStageSelectionV1 Selection, string SourceWarehouse, string SourceLocation,
    string DestinationWarehouse, string DestinationLocation, string ProductCode, string Lot, string MetadataDigest);
public sealed record TransferStageMovementV1(Guid RequestLineId, Guid SourceStockId, string Quantity, Guid OutMovementId, Guid InMovementId);
public sealed record TransferStageV1(Guid Id, string State, string Device, string ScanOccurrenceKey, Guid PerformerId,
    string RequestVersion, string ContentDigest, string Digest, TransferStageLineV1[] Lines, TransferStageMovementV1[] Movements);
