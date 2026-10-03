using System.Collections.Frozen;

namespace CP6.Core.Persistence;

/// <summary>
/// Exact fixed-text domains audited for the four WP2 models. Values are ASCII
/// capacities, not a general SQL char/nchar compatibility rule. String identities
/// avoid a Core-to-Space.Domain dependency. Do not infer membership from a suffix.
/// </summary>
public static class PostgreSqlFixedTextDomainsV1
{
    public static IReadOnlyDictionary<(string EntityFullName, string Property), int> Core { get; } =
        new Dictionary<(string EntityFullName, string Property), int>
        {
            [("CP6.Entity.DomainModels.Space.Space_AuditEvent", "AfterHash")] = 64,
            [("CP6.Entity.DomainModels.Space.Space_AuditEvent", "BeforeHash")] = 64,
            [("CP6.Entity.DomainModels.Wms.SpaceDispatchApprovalRequest", "PayloadHash")] = 64,
            [("CP6.Entity.DomainModels.Wms.SpaceDispatchApprovalRequest", "RecommendationRequestHash")] = 64,
            [("CP6.Entity.DomainModels.Wms.SpaceDispatchExecutionAction", "PayloadHash")] = 64,
            [("CP6.Entity.DomainModels.Wms.SpaceWmsOperation", "PayloadHash")] = 64,
        }.ToFrozenDictionary();

    public static IReadOnlyDictionary<(string EntityFullName, string Property), int> Space { get; } =
        new Dictionary<(string EntityFullName, string Property), int>
        {
            [("CP6.Space.Domain.SpaceAiBudgetReservation", "Currency")] = 3,
            [("CP6.Space.Domain.SpaceAiBudgetReservation", "ProviderRequestKey")] = 64,
            [("CP6.Space.Domain.SpaceAiTenantPolicyConfiguration", "Currency")] = 3,
            [("CP6.Space.Domain.SpaceAiUsageRecord", "Currency")] = 3,
            [("CP6.Space.Domain.SpaceAiUsageRecord", "ProviderRequestIdHash")] = 64,
            [("CP6.Space.Domain.SpaceAssetVersion", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpaceCadMappingProfileVersion", "DefinitionHash")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "BaseContentHash")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "CoordinateTransformSha256")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "MappingDefinitionSha256")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "MappingPreviewSha256")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "SemanticPreviewSha256")] = 64,
            [("CP6.Space.Domain.SpaceCadParsePreparation", "SourceSha256")] = 64,
            [("CP6.Space.Domain.SpaceDeviceEvent", "PayloadHash")] = 64,
            [("CP6.Space.Domain.SpaceDispatchRecommendation", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpaceElementCommandBatch", "ChangesetSha256")] = 64,
            [("CP6.Space.Domain.SpaceElementCommandBatch", "ExpectedContentHash")] = 64,
            [("CP6.Space.Domain.SpaceElementCommandBatch", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpaceExcelMappingProfileVersion", "DefinitionHash")] = 64,
            [("CP6.Space.Domain.SpaceFile", "Sha256")] = 64,
            [("CP6.Space.Domain.SpaceGenerationLockedFact", "SourceHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationProposal", "SourceHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationRun", "ApplyPlanHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationRun", "ApplyReviewEtag")] = 64,
            [("CP6.Space.Domain.SpaceGenerationRun", "BusinessKeyHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationRun", "IdempotencyKeyHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationRun", "SourceHash")] = 64,
            [("CP6.Space.Domain.SpaceGenerationStagingElement", "ValidationHash")] = 64,
            [("CP6.Space.Domain.SpaceHistoricalRepublish", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpaceIdempotencyRecord", "IdempotencyKeyHash")] = 64,
            [("CP6.Space.Domain.SpaceIdempotencyRecord", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpaceJob", "BusinessKey")] = 64,
            [("CP6.Space.Domain.SpaceJob", "InputHash")] = 64,
            [("CP6.Space.Domain.SpaceJobAttempt", "InputHash")] = 64,
            [("CP6.Space.Domain.SpaceJobStep", "OutputHash")] = 64,
            [("CP6.Space.Domain.SpaceModel", "LastMaterializedHash")] = 64,
            [("CP6.Space.Domain.SpaceModelSource", "Sha256")] = 64,
            [("CP6.Space.Domain.SpaceModelVersion", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpaceModelVersion", "SourceTemplateContentHash")] = 64,
            [("CP6.Space.Domain.SpaceModelVersion", "ValidatedHash")] = 64,
            [("CP6.Space.Domain.SpaceModelVersion", "WmsCapabilityHash")] = 64,
            [("CP6.Space.Domain.SpacePersonnelEvent", "PayloadHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningComparison", "ComparisonHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningComparison", "CurrencyCode")] = 3,
            [("CP6.Space.Domain.SpacePlanningComparison", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningComparison", "SourceDatasetHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningComparisonEntry", "RunResultHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningDecisionRecord", "ComparisonHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningDecisionRecord", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningHistoricalDataset", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningHistoricalDataset", "SourceDatasetHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningHistoricalTask", "TaskToken")] = 64,
            [("CP6.Space.Domain.SpacePlanningHistoricalTask", "WorkerToken")] = 64,
            [("CP6.Space.Domain.SpacePlanningScenarioBranch", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningSimulationRun", "CurrencyCode")] = 3,
            [("CP6.Space.Domain.SpacePlanningSimulationRun", "DatasetRequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningSimulationRun", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePlanningSimulationRun", "ResultHash")] = 64,
            [("CP6.Space.Domain.SpacePublishAttempt", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpacePublishAuditEvent", "EventHash")] = 64,
            [("CP6.Space.Domain.SpacePublishAuditEvent", "EvidenceHash")] = 64,
            [("CP6.Space.Domain.SpacePublishAuditEvent", "PreviousEventHash")] = 64,
            [("CP6.Space.Domain.SpacePublishBatch", "PayloadHash")] = 64,
            [("CP6.Space.Domain.SpacePublishPlan", "CapabilityHash")] = 64,
            [("CP6.Space.Domain.SpacePublishPlan", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpacePublishPlan", "PlanHash")] = 64,
            [("CP6.Space.Domain.SpacePutawayRecommendation", "RequestHash")] = 64,
            [("CP6.Space.Domain.SpaceRackGenerationProfileVersion", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpaceReconciliationIssue", "ExpectedStateHash")] = 64,
            [("CP6.Space.Domain.SpaceReconciliationIssue", "RuntimeStateHash")] = 64,
            [("CP6.Space.Domain.SpaceReconciliationIssue", "WmsStateHash")] = 64,
            [("CP6.Space.Domain.SpaceRuntimeElement", "PayloadHash")] = 64,
            [("CP6.Space.Domain.SpaceValidationRun", "CapabilityHash")] = 64,
            [("CP6.Space.Domain.SpaceValidationRun", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpaceWarehouseTemplateVersion", "ContentHash")] = 64,
            [("CP6.Space.Domain.SpaceWmsAdoption", "WmsStateHash")] = 64,
            [("CP6.Space.Domain.SpaceWmsReceipt", "ResponseHash")] = 64,
        }.ToFrozenDictionary();

    public static IReadOnlyDictionary<(string EntityFullName, string Property), int> IdentityPriority { get; } =
        FrozenDictionary<(string EntityFullName, string Property), int>.Empty;

    public static IReadOnlyDictionary<(string EntityFullName, string Property), int> ErpIntegration { get; } =
        FrozenDictionary<(string EntityFullName, string Property), int>.Empty;
}
