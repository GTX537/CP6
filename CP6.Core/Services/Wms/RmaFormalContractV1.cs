using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

public sealed record RmaTrustedScopeV1(string EnvironmentId, Guid SiteId, Guid WarehouseId, string Mode, FulfilmentRefV1 ScopeRef);
public sealed record RmaScopeOptionV1(FulfilmentRefV1 OptionRef, Guid SiteId, Guid WarehouseId, string Label,
    FulfilmentRefV1 ScopeHeadRef, string[] Capabilities, string Coverage);
public sealed record RmaContextLimitsV1(int MaxApplicationLines = 100, int MaxRangesPerLine = 100, int MaxRequestBytes = 1048576, int MaxPageSize = 100);
public sealed record RmaFormalContextV1(FulfilmentRefV1 ActorRef, RmaScopeOptionV1[] ScopeOptions, string[] Capabilities,
    RmaContextLimitsV1 Limits, DateTimeOffset CapturedAt);
public sealed record RmaFulfilmentOptionV1(FulfilmentRefV1 OptionRef, OriginalFulfilmentV1 Original,
    FulfilmentRefV1 EntitlementPreviewRef, FulfilmentQtyV1? RemainingAuthorizableQuantity, string Coverage, string[] Gates, DateTimeOffset ObservedAt);
public sealed record RmaFulfilmentOptionPageV1(RmaFulfilmentOptionV1[] Items, string? NextCursor, FulfilmentRefV1 SnapshotRef,
    DateTimeOffset CapturedAt, string Coverage);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaFulfilmentQueryV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] string? Keyword, [property: JsonRequired] FulfilmentRefV1? CustomerRef,
    [property: JsonRequired] FulfilmentRefV1? ShipmentRef, [property: JsonRequired] bool DeliveredOnly,
    [property: JsonRequired] string? Cursor, [property: JsonRequired] int PageSize);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaRequestedLineV1([property: JsonRequired] Guid LineId, [property: JsonRequired] FulfilmentRefV1 OriginalSelectionRef,
    [property: JsonRequired] FulfilmentQtyV1 RequestedQuantity, [property: JsonRequired] string? ReasonDetail,
    [property: JsonRequired] FulfilmentRefV1[] EvidenceRefs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaFormalCreateV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] Guid ClientApplicationId, [property: JsonRequired] FulfilmentRefV1 CustomerRef,
    [property: JsonRequired] string ReasonCode, [property: JsonRequired] string RequestedOutcome,
    [property: JsonRequired] RmaRequestedLineV1[] Lines, [property: JsonRequired] string? Remarks);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaFormalEditV1([property: JsonRequired] int ExpectedApplicationVersion,
    [property: JsonRequired] FulfilmentRefV1 ExpectedApplicationHeadRef, [property: JsonRequired] FulfilmentRefV1 CustomerRef,
    [property: JsonRequired] string ReasonCode, [property: JsonRequired] string RequestedOutcome,
    [property: JsonRequired] RmaRequestedLineV1[] Lines, [property: JsonRequired] string? Remarks, [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaFormalTransitionV1([property: JsonRequired] int ExpectedApplicationVersion,
    [property: JsonRequired] FulfilmentRefV1 ExpectedApplicationHeadRef, [property: JsonRequired] string Reason);
public sealed record RmaRequestedLineSnapshotV1(FulfilmentRefV1 ApplicationRef, int ApplicationVersion, Guid LineId,
    int LineVersion, FulfilmentRefV1 OriginalSelectionRef, OriginalFulfilmentV1 OriginalSnapshot,
    FulfilmentRangeV1[] RequestedRanges, FulfilmentQtyV1 RequestedQuantity, string? ReasonDetail, FulfilmentRefV1[] EvidenceRefs, string LineDigest);
public sealed record RmaFormalApplicationV1(Guid ApplicationId, int ApplicationVersion, FulfilmentRefV1 ApplicationRef,
    RmaTrustedScopeV1 Scope, FulfilmentRefV1 CustomerRef, string ReasonCode, string RequestedOutcome, DateTimeOffset RequestedAt,
    FulfilmentRefV1 RequestedBy, RmaRequestedLineSnapshotV1[] RequestedLines, FulfilmentRefV1[] EvidenceRefs,
    string State, DateTimeOffset? ExpiresAt, FulfilmentRefV1? PreviousVersionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaCommandQueryV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef, [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Method, [property: JsonRequired] string RouteTemplate);
public sealed record RmaCommandBindingV1(Guid CommandId, RmaTrustedScopeV1 Scope, FulfilmentRefV1 PrincipalRef, string Method,
    string RouteTemplate, string ActorCapability, string BusinessOperation, FulfilmentRefV1 BusinessTargetRef,
    FulfilmentRefV1[] SourceVersions, string BodyDigest, string[] EffectKeys, FulfilmentRefV1[] ExpectedHeadRefs, DateTimeOffset CreatedAt);
public sealed record RmaApplicationWorkV1(Guid WorkId, Guid RmaId, string Operation, int Revision, int LeaseEpoch, int WriterEpoch,
    string State, FulfilmentRefV1 BindingRef, object[] Checkpoints, string[] NextActions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record RmaApplicationReceiptV1(Guid ReceiptId, FulfilmentRefV1 CommandBindingRef, string Status,
    FulfilmentRefV1[] ResultRefs, string[] EffectKeys, FulfilmentRefV1? NoAdditionalEffectProofRef, DateTimeOffset RecordedAt, bool Replayed);
