using System.Text.Json;

namespace CP6.Core.Services.Wms;

// Stock-facing names mirror the accepted wire. RMA ranges/claims stay in RmaInboundBindingV1.
public sealed record ReturnStockCodeV1(string Id, string Version);
public sealed record ReturnStockReceiptRefV1(string Owner, string Id, string Version);
public sealed record ReturnStockPlanRefV1(Guid Id, string Version, string Digest);
public sealed record ReturnStockWireSourceV1(string Producer, string SourceKind, string DocumentId, string LineId, string ExecutionKey, string SourceVersion);
public sealed record ReturnStockOccurrenceV1(string Producer, string OccurrenceKey, string EffectKey);
public sealed record ReturnStockScopeV1(string TargetKey, FulfilmentRefV1? DefinitionRef, string MemberDigest);
public sealed record ReturnStockUseV1(string Action, ReturnStockCodeV1 PurposeRef, FulfilmentRefV1? DemandRef, FulfilmentRefV1? TechnicalRequirementRef);
public sealed record ReturnStockPartV1(string ClientLineKey, ReturnStockWireSourceV1 Source, ReturnStockOccurrenceV1 Occurrence,
    string InputQuantity, ReturnStockCodeV1 InputUomRef, string BaseQuantity, ReturnStockCodeV1 BaseUomRef,
    FulfilmentRefV1 AllowanceRef, string LineBusinessDigest);
public sealed record ReturnStockManifestV1(string Schema, ReturnStockWireSourceV1 Parent, string PostingKind, string RequestedAction,
    DateTimeOffset OccurredAt, ReturnStockPartV1[] Parts, FulfilmentRefV1 AuthorityRef, ReturnStockScopeV1 Scope);
public sealed record ReturnStockPoolVersionV1(Guid PoolId, string Revision);
public sealed record ReturnStockPlanRequestV1(ReturnStockWireSourceV1 Source, FulfilmentRefV1 ExecutionManifestRef,
    DateTimeOffset OccurredAt, ReturnStockUseV1 Use, ReturnStockPoolVersionV1[] ExpectedPools, JsonElement Data, string Reason);
public sealed record ReturnStockPostingV1(ReturnStockPlanRefV1 PlanRef, string Reason);
public sealed record ReturnStockSpanV1(Guid RootId, string From, string To);
public sealed record ReturnStockTechnicalV1(string Kind, FulfilmentRefV1? RevisionRef, FulfilmentRefV1? ApplicabilityRef);
public sealed record ReturnStockOwnerV1(string Kind, FulfilmentRefV1 Reference);
public sealed record ReturnStockLotV1(string Kind, string? LotId, string? LotCode, FulfilmentRefV1? PolicyRef);
public sealed record ReturnStockDimensionsV1(Guid SiteId, FulfilmentRefV1 ProductRef, ReturnStockTechnicalV1 TechnicalIdentity,
    ReturnStockOwnerV1 Owner, ReturnStockCodeV1 OriginPurposeRef, ReturnStockCodeV1 BaseUomRef, ReturnStockCodeV1 PrecisionPolicyRef,
    ReturnStockLotV1 Lot, FulfilmentRefV1? SerialRef, FulfilmentRefV1? HandlingUnitRef);
public sealed record ReturnStockPlaceV1(string Kind, Guid WarehouseId, Guid LocationId);
public sealed record ReturnStockSourceUseV1(ReturnStockWireSourceV1 Source, FulfilmentRefV1 AllowanceRef,
    ReturnStockCodeV1 InputUomRef, FulfilmentRefV1? ConversionRef);
public sealed record ReturnStockQualityV1(string Kind, FulfilmentRefV1 PolicyRef);
public sealed record ReturnStockReturnQualityV1(string Kind, FulfilmentRefV1 BasisRef);
public sealed record ReturnStockReceiptLineV1(string ClientLineKey, ReturnStockDimensionsV1 Dimensions, ReturnStockPlaceV1 Destination,
    string InputQuantity, ReturnStockSourceUseV1 SourceUse, ReturnStockQualityV1 QualityBasis);
public sealed record ReturnStockReturnLineV1(string ClientLineKey, ReturnStockSpanV1[] Spans, JsonElement SourcePlace,
    ReturnStockPlaceV1 Destination, string InputQuantity, ReturnStockSourceUseV1 SourceUse, Guid[] RetainedClaimIds,
    ReturnStockReturnQualityV1 ReturnQuality);
public sealed record ReturnStockLocatorV1(string Producer, string ExecutionKey, Guid CommandKey);
public sealed record ReturnStockLookupLocatorV1(string Producer, string ExecutionKey);
public sealed record ReturnStockLookupV1(ReturnStockLookupLocatorV1 Locator, string BusinessDigest, Guid? CommandKey);
public sealed record ReturnStockSealV1(ReturnStockLookupLocatorV1 Locator, string BusinessDigest, ReturnStockPlanRefV1 IntentRef,
    FulfilmentRefV1 SourceClosureAuthority, string Reason);
public sealed record ReturnStockLedgerV1(Guid PoolId, string Sequence);
public sealed record ReturnStockRootV1(string LineKey, Guid RootId, string Quantity);
public sealed record ReturnStockResultV1(ReturnStockReceiptRefV1 Receipt, Guid? MovementId, Guid? ClaimId, Guid? RestrictionId,
    Guid? CorrectionId, Guid? ResolutionId, Guid[] RelatedMovementIds, JsonElement? Closure, JsonElement? Reconciliation,
    ReturnStockLedgerV1[] LedgerEntries, ReturnStockRootV1[] Roots, ReturnStockPoolVersionV1[] PoolVersions, string SemanticDigest);
public sealed record ReturnStockResponseV1(Guid OperationId, ReturnStockLocatorV1 Locator, string Status, string CommitKnowledge,
    ReturnStockResultV1? Result, string ResultCode, bool Replayed, object[] ConsumerStates, object[] Attempts,
    string ExecutionRevision, string[] AllowedRecoverySteps, bool CurrentReadPermission, bool Simulation);
public sealed record RmaInboundBindingV1(FulfilmentRefV1 BindingRef, FulfilmentRefV1 RmaRef, FulfilmentRefV1 ApplicationRef,
    FulfilmentRefV1 AuthorizationDecisionRef, FulfilmentRefV1 ReturnShareRef, FulfilmentRefV1 RangeClaimRef,
    FulfilmentRangeV1[] RmaRanges, FulfilmentQtyV1 RmaQuantity, FulfilmentRefV1 SourceRangeMapRef,
    FulfilmentRefV1? ConversionRef, JsonElement RouteBinding);
public sealed record RmaInboundPrepareV1(Guid CommandId, FulfilmentRefV1 ScopeRef, FulfilmentRefV1 RmaRef,
    FulfilmentRefV1 ReceiptWorkRef, FulfilmentRefV1[] ReceiptLineRefs, int ExpectedWorkRevision,
    FulfilmentRefV1 ExpectedRmaHeadRef, DateTimeOffset OccurredAt, string Reason);
public sealed record RmaInboundPreparationV1(FulfilmentRefV1 AdapterContractRef, FulfilmentRefV1 WorkRef, int WorkRevision,
    RmaInboundBindingV1[] Bindings, FulfilmentRefV1 ManifestRef, ReturnStockManifestV1 ExecutionManifest,
    string ManifestContentDigest, ReturnStockPlanRequestV1 StockPostingPlanRequest, ReturnStockPlanRefV1 StockPostingPlanRef,
    ReturnStockPlanRefV1 FrozenIntentRef, string StockTargetKey, string Coverage, string[] BlockingReasons, bool CanCommit,
    FulfilmentRefV1 DatabaseBindingRef, FulfilmentRefV1 MainUowParticipantRef);
public sealed record RmaInboundCommitV1(Guid CommandId, FulfilmentRefV1 PreparationRef, int ExpectedWorkRevision,
    FulfilmentRefV1 ExpectedRmaHeadRef, ReturnStockPostingV1 StockPosting, string Reason);
public sealed record RmaInboundReceiptResultV1(string Owner, string ResultId, string ResultVersion, string ResultDigest,
    FulfilmentRefV1 CurrentHeadProof, JsonElement QueryIdentity, FulfilmentRefV1 PreparationRef, FulfilmentRefV1[] BindingRefs,
    FulfilmentRefV1 ManifestRef, string ManifestContentDigest, FulfilmentRefV1 ReceiptOperationRef,
    FulfilmentRefV1[] ReceiptLineRefs, RmaPhysicalReceiptV1[] PhysicalReceipts, ReturnStockResponseV1 StockResult,
    FulfilmentRefV1 SourceRangeMapRef, FulfilmentRefV1[] CorrectionLineageRefs, DateTimeOffset RecordedAt, DateTimeOffset TerminalAt);
public sealed record RmaInboundQueryV1(ReturnStockLookupV1 StockLookup, FulfilmentRefV1 PreparationRef);
public sealed record RmaInboundSealV1(Guid CommandId, RmaInboundQueryV1 Query, ReturnStockSealV1 StockSeal,
    FulfilmentRefV1 PreparationRef, string Reason);
