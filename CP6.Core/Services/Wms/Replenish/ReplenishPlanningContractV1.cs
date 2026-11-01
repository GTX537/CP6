using System.Text.Json.Serialization;
namespace CP6.Core.Services.Wms.Replenish;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepRefV1(string Owner,string Id,string Version,string DigestDomain,string CodecVersion,string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepQuantityV1(string Value,RepRefV1 UomRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepScopeV1(RepRefV1 SiteRef,RepRefV1 WarehouseRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSpanV1(RepRefV1 RootRef,string From,string To);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepCommandV1(string CommandKey,[property:JsonRequired]string? ExpectedRevision,[property:JsonRequired]string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepRuleV1(RepRefV1 Ref,RepRefV1[] TargetLocationRefs,RepRefV1 ProductRef,RepRefV1 TechnicalRef,
    RepRefV1 OwnerRef,RepRefV1 BaseUomRef,RepQuantityV1 MinQuantity,RepQuantityV1 MaxQuantity,RepQuantityV1? LotMultiple,
    RepRefV1 SourceRuleRef,[property:JsonRequired]DateTimeOffset ValidFrom,[property:JsonRequired]DateTimeOffset? ValidTo,string Coverage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSourceV1(RepRefV1 SourceLocationRef,RepSpanV1[] Spans,RepQuantityV1 Quantity,
    RepRefV1 StockObservationRef,DateTimeOffset? ExpiryAt,DateTimeOffset? ReceivedAt,RepRefV1 MasterConditionRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepRowV1(string LineKey,RepRefV1 RuleRef,RepRefV1 TargetLocationRef,RepRefV1 ProductRef,
    RepRefV1 TechnicalRef,RepRefV1 OwnerRef,RepRefV1 BaseUomRef,RepQuantityV1? OnHand,RepQuantityV1? OpenIncoming,
    RepQuantityV1? EffectivePosition,RepQuantityV1? Needed,RepQuantityV1? Proposed,RepRefV1? AllocationPlanRef,
    RepSourceV1[] Sources,string Coverage,string[] Reasons);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSuggestionV1(RepRefV1 Ref,RepRefV1 CutRef,RepRowV1[] Rows,string Coverage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepCutV1(RepRefV1 Ref,RepRefV1 UpstreamCutRef,RepRefV1[] ActiveIntentRefs,DateTimeOffset ObservedAt,string DataKind);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSplitV1(string SplitKey,RepRefV1 SourceLocationRef,RepSourceV1 Source,RepQuantityV1 Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepPlanV1(RepRefV1 Ref,RepRefV1 SuggestionRef,string LineKey,RepRefV1 CutRef,string DemandSlotKey,
    RepRefV1 TargetLocationRef,RepQuantityV1 TotalQuantity,RepSplitV1[] Splits,string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSuggestInputV1(RepCommandV1 Command,RepScopeV1 Scope,RepRefV1[] RuleRefs,RepRefV1[] TargetLocationRefs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSelectionV1(string LineKey,RepRefV1 AllocationPlanRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepBatchInputV1(RepCommandV1 Command,RepRefV1 SuggestionRef,RepSelectionV1[] SelectedPlans,string SelectionDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepBatchSplitV1(string SplitKey,RepRefV1 OrderRef,RepQuantityV1 Quantity,string Outcome);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepBatchItemV1(string LineKey,RepRefV1 AllocationPlanRef,RepQuantityV1 TotalQuantity,
    RepBatchSplitV1[] Splits,string Outcome,string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepBatchV1(RepRefV1 Ref,RepBatchItemV1[] Items,string Registration,string DispatchSummary);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepOrderInputV1(RepScopeV1 Scope,RepRefV1 ProductRef,RepRefV1 TechnicalRef,RepRefV1 OwnerRef,
    RepRefV1 BaseUomRef,RepRefV1 ToLocationRef,RepSourceV1 Source,RepQuantityV1 Quantity,int Priority,string Trigger,
    RepRefV1? SuggestionRowRef,string Reason,string? Remarks);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepOrderV1(RepRefV1 Ref,RepOrderInputV1 Input,RepRefV1 SelectionRef,string Lifecycle,string Knowledge,
    RepQuantityV1 Planned,RepQuantityV1? Moved,RepQuantityV1? Remaining,object? ExecutionLink,RepRefV1[] StockReceiptRefs,
    string[] Capabilities,RepRefV1 CurrentCutRef,string Revision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepIntentSplitV1(string SplitKey,RepRefV1 OrderRef,RepQuantityV1 Planned,string State);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepIntentV1(RepRefV1 Ref,string DemandSlotKey,RepRefV1 AllocationPlanRef,string State,
    RepQuantityV1 OriginalQuantity,RepQuantityV1? OpenIncoming,RepQuantityV1? ConfirmedMoved,RepQuantityV1? ClosedUnexecuted,
    RepIntentSplitV1[] Splits,RepRefV1 CutRef,string Coverage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepOperationV1(string CommandKey,string State,object? Result);
public sealed class ReplenishSimulationOptionsV1 { public bool Enabled {get;set;} }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepSimulationInputV1(RepObservationV1 Observation);

// One complete upstream observation, published by its trusted reader in one transaction.
// These are read projections, never a second editable Stock ledger or a physical receipt.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepLocationMapV1(RepRefV1 Ref,Guid NativeLocationId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepCapacityDomainV1(RepRefV1 DomainRef,RepRefV1 MetricRef,RepRefV1 UomRef,string Mode,[property:JsonRequired]string? FreeValue);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepCapacityDeltaV1(RepRefV1 DomainRef,RepRefV1 MetricRef,RepRefV1 UomRef,string Numerator,string Denominator);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepRootObservationV1(RepRefV1 LocationRef,RepRefV1 ProductRef,RepRefV1 TechnicalRef,
    RepRefV1 OwnerRef,RepRefV1 BaseUomRef,RepSpanV1 Span,RepRefV1 StockObservationRef,RepRefV1 MasterConditionRef,
    [property:JsonRequired]DateTimeOffset? ExpiryAt,[property:JsonRequired]DateTimeOffset? ReceivedAt,RepRefV1[] SourceRuleRefs,RepCapacityDeltaV1[] CapacityDeltas,[property:JsonRequired]bool Movable,string[] Reasons);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepTargetObservationV1(RepRefV1 LocationRef,RepRefV1 ProductRef,RepRefV1 TechnicalRef,
    RepRefV1 OwnerRef,RepRefV1 BaseUomRef,[property:JsonRequired]RepQuantityV1? OnHand,[property:JsonRequired]bool Initialized,string Coverage,[property:JsonRequired]bool CapacityKnown,
    RepCapacityDomainV1[] CapacityDomains);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepIncomingObservationV1(string IntentKey,string DemandSlotKey,RepSpanV1[] OpenSpans,string Coverage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepObservationV1(RepRefV1 CutRef,RepScopeV1 Scope,[property:JsonRequired]Guid? DepartmentId,[property:JsonRequired]DateTimeOffset ObservedAt,
    string DataKind,string Coverage,RepLocationMapV1[] Locations,RepRuleV1[] Rules,RepTargetObservationV1[] Targets,
    RepRootObservationV1[] Roots,RepIncomingObservationV1[] Incoming);
