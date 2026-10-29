using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Mes;

namespace CP6.Core.Services.Wms;

// Executable successor to the historical R3 REQUIRED_FENCED disposition projections.
public sealed record ReturnDispositionDefinitionV2(string Kind,string Resource,string Owner,string Adapter,string PostingKind,string Action);
public sealed record ReturnDispositionOptionV2(FulfilmentRefV1 Reference,string Disposition,string Label,FulfilmentRefV1 BusinessRef,
    FulfilmentRefV1 AuthorityRef,Guid? DestinationLocationId,FulfilmentRefV1? PartyRef,FulfilmentRefV1? ExternalCustodyRef,FulfilmentRefV1[] EvidenceRefs,bool Simulation);
public sealed record ReturnDispositionProposalV2(FulfilmentRefV1 ProposalRef,FulfilmentRefV1 SelectionRef,FulfilmentRefV1 RmaRef,
    FulfilmentRefV1 SourceReceiptRef,FulfilmentRefV1 QualityDecisionRef,string Quality,FulfilmentRangeV1[] EligibleRanges,
    FulfilmentQtyV1 EligibleQuantity,string[] AllowedDispositions,FulfilmentRefV1[] OptionRefs,FulfilmentRefV1[] SourceHeadRefs,
    DateTimeOffset ExpiresAt,string Coverage,string[] BlockingReasons);
public sealed record ReturnDispositionChoiceV2(Guid LineId,Guid InspectionId,ReturnDispositionProposalV2 Proposal,ReturnDispositionOptionV2[] Options);
public sealed record ReturnDispositionContextV2(ReturnDispositionDefinitionV2 Definition,ReturnDispositionChoiceV2[] Choices,
    ReturnDispositionViewV2[] Works,string[] Capabilities);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnDispositionPrepareV2([property: JsonRequired] FulfilmentRefV1 ProposalRef,
    [property: JsonRequired] FulfilmentRefV1 SelectionRef,[property: JsonRequired] FulfilmentRefV1 OptionRef,
    [property: JsonRequired] RmaDeliveryRangeV1[] Ranges,[property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnDispositionActionV2([property: JsonRequired] FulfilmentRefV1 ExpectedWorkRef,[property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnDispositionQueryV2([property: JsonRequired] Guid ApplicationId,[property: JsonRequired] Guid CommandId);
public sealed record ReturnDispositionStockKeyV2(Guid PoolId,Guid WarehouseId,Guid LocationId,string Warehouse,string Location,string Product,string Lot);
public sealed record ReturnDispositionStockPlanV2(ReturnDispositionStockKeyV2 Source,ReturnDispositionStockKeyV2? Destination,
    ReturnStockDimensionsV1 Dimensions,ReturnStockSpanV1[] Spans,FulfilmentRefV1 PoolSnapshotRef,ReturnStockManifestV1 Manifest,
    ReturnStockPlanRequestV1 Request,ReturnStockPlanRefV1 PlanRef,ReturnStockPlanRefV1 IntentRef);
public sealed record ReturnDispositionPreparationV2(Guid WorkId,Guid ApplicationId,Guid ActorId,DateTimeOffset PreparedAt,
    ReturnDispositionDefinitionV2 Definition,FulfilmentRefV1 ContractRef,ReturnDispositionProposalV2 Proposal,
    ReturnDispositionOptionV2 Option,RmaQualitySourceV1 Source,ReturnQualityDecisionV1 Decision,FulfilmentRangeV1[] Ranges,
    FulfilmentQtyV1 Quantity,ReturnDispositionStockPlanV2 Stock,string Reason);
public sealed record ReturnDispositionReceiptV2(FulfilmentRefV1 Reference,FulfilmentRefV1 OwnerContractRef,
    FulfilmentRefV1 BusinessRef,FulfilmentRefV1? PartyRef,Guid WorkId,string Disposition,FulfilmentRangeV1[] Ranges,
    FulfilmentQtyV1 Quantity,FulfilmentRefV1 QualityDecisionRef,FulfilmentRefV1? QualityReleaseRef,
    ReturnStockResponseV1 StockResult,Guid ActorId,DateTimeOffset AppliedAt);
public sealed record ReturnDispositionViewV2(ReturnDispositionPreparationV2 Preparation,FulfilmentRefV1 WorkRef,string State,
    ReturnDispositionReceiptV2? Result,FulfilmentRefV1? NoEffectProof,bool QualityCurrent);
public sealed record ReturnDispositionCommandResultV2(string Action,ReturnDispositionViewV2 Value);
public sealed record ReturnDispositionQueryResultV2(string Status,ReturnDispositionCommandResultV2? Value);
public sealed record ReturnDispositionOutcomeV2(string Status,ReturnDispositionReceiptV2? Result,FulfilmentRefV1 WorkRef,
    FulfilmentRefV1? NoEffectProof,bool CanInferNoEffect);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnDispositionSimulationCaseV2([property: JsonRequired] Guid TenantId,[property: JsonRequired] Guid SiteId,
    [property: JsonRequired] FulfilmentRefV1 QualityPolicyRef,[property: JsonRequired] string Disposition,
    [property: JsonRequired] string Version,[property: JsonRequired] string Label,[property: JsonRequired] string[] AllowedQualities,
    [property: JsonRequired] Guid? DestinationLocationId,[property: JsonRequired] FulfilmentRefV1 BusinessRef,
    [property: JsonRequired] FulfilmentRefV1 AuthorityRef,[property: JsonRequired] FulfilmentRefV1? PartyRef,[property: JsonRequired] FulfilmentRefV1? ExternalCustodyRef,
    [property: JsonRequired] FulfilmentRefV1[] EvidenceRefs);
public sealed class ReturnDispositionSimulationOptionsV2
{
    public bool Enabled { get; set; }
    public string CasesJson { get; set; } = "[]";
}
