using CP6.Space.Contracts;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CP6.WebApi.OpenApi;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SpaceDesignV1ContractAttribute : Attribute
{
}

public static class SpaceDesignV1OpenApi
{
    public const string DocumentName = "space-design-v1";

    public static void Configure(SwaggerGenOptions options)
    {
        options.CustomOperationIds(
            description =>
                IsContractController(description) &&
                description.TryGetMethodInfo(out var methodInfo)
                    ? methodInfo.Name
                    : null);
        options.CustomSchemaIds(GetSchemaId);
        options.SchemaFilter<SpaceWmsRuntimeSchemaFilter>();
        options.OperationFilter<SpaceDesignV1OperationFilter>();
        options.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "CP6 API",
                Version = "v1",
            });
        options.SwaggerDoc(
            DocumentName,
            new OpenApiInfo
            {
                Title = "CP6 Space Design API",
                Version = "v1",
                Description =
                    "Authoritative OpenAPI contract for Space Design API v1.",
            });
        options.DocInclusionPredicate(
            (documentName, description) =>
                documentName == "v1" ||
                (documentName == DocumentName &&
                 IsContractController(description)));
    }

    private static bool IsContractController(
        Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description) =>
        description.ActionDescriptor.RouteValues["controller"] ==
        "SpaceDesignV1" ||
        description.ActionDescriptor.EndpointMetadata
            .OfType<SpaceDesignV1ContractAttribute>()
            .Any();

    private static string GetSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return (type.FullName ?? type.Name).Replace("+", ".");

        var genericName = type.Name.Split('`')[0];
        var argumentNames = string.Join(
            "And",
            type.GetGenericArguments().Select(GetShortSchemaId));
        return $"{genericName}Of{argumentNames}";
    }

    private static string GetShortSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var genericName = type.Name.Split('`')[0];
        var argumentNames = string.Join(
            "And",
            type.GetGenericArguments().Select(GetShortSchemaId));
        return $"{genericName}Of{argumentNames}";
    }
}

public sealed class SpaceWmsRuntimeSchemaFilter : ISchemaFilter
{
    private static readonly IReadOnlyDictionary<Type, string[]>
        RequiredProperties = new Dictionary<Type, string[]>
        {
            [typeof(SpaceRackGenerationProfileLevelDto)] =
            [
                "levelNo",
                "bottomZMillimeters",
                "clearHeightMillimeters",
                "binCount",
                "depthCount",
                "cellWidthMillimeters",
                "cellDepthMillimeters",
            ],
            [typeof(SpaceRackGenerationProfileVersionDto)] =
            [
                "id",
                "profileId",
                "scope",
                "versionNo",
                "rackWidthMillimeters",
                "rackDepthMillimeters",
                "rackHeightMillimeters",
                "levels",
                "locationCount",
                "contentHash",
                "status",
                "rowVersion",
            ],
            [typeof(SpaceRackGenerationProfileDto)] =
            [
                "id",
                "scope",
                "profileCode",
                "name",
                "status",
                "latestVersion",
                "rowVersion",
            ],
            [typeof(CreateSpaceRackGenerationProfileRequest)] =
            [
                "profileCode",
                "name",
                "rackWidthMillimeters",
                "rackDepthMillimeters",
                "rackHeightMillimeters",
                "levels",
            ],
            [typeof(CreateSpaceRackGenerationProfileResponse)] =
            [
                "profile",
                "idempotentReplay",
            ],
            [typeof(ApplySpaceElementCommandBatchRequest)] =
            [
                "schemaVersion",
                "commandBatchId",
                "clientInstanceId",
                "leaseId",
                "expectedFloorRevision",
                "commands",
            ],
            [typeof(ApplySpaceLayoutCommandBatchRequest)] =
            [
                "schemaVersion",
                "commandBatchId",
                "clientInstanceId",
                "leaseId",
                "expectedFloorRevision",
                "expectedContentRevision",
                "commands",
            ],
            [typeof(SpaceLayoutCommandDto)] =
            [
                "commandId",
                "type",
                "targetLogicalId",
            ],
            [typeof(SpaceCreateLayoutZoneDto)] =
            [
                "zoneCode",
                "zoneType",
                "polygonJson",
            ],
            [typeof(SpaceCreateLayoutAisleDto)] =
            [
                "zoneLogicalId",
                "aisleCode",
                "direction",
                "polygonJson",
                "centerlineJson",
            ],
            [typeof(SpaceCreateLayoutRackDto)] =
            [
                "zoneLogicalId",
                "rackCode",
                "x",
                "y",
                "z",
                "rotationZ",
                "width",
                "depth",
                "height",
                "levels",
            ],
            [typeof(SpaceCreateLayoutRackLevelDto)] =
            [
                "levelNo",
                "bottomZ",
                "clearHeight",
                "binCount",
                "depthCount",
                "cellWidth",
                "cellDepth",
                "beamHeight",
            ],
            [typeof(SpaceUpdateLayoutZoneDto)] =
            [
                "zoneCode",
                "zoneType",
                "polygonJson",
            ],
            [typeof(SpaceUpdateLayoutAisleDto)] =
            [
                "zoneLogicalId",
                "aisleCode",
                "direction",
                "polygonJson",
                "centerlineJson",
            ],
            [typeof(SpaceUpdateLayoutRackDto)] =
            [
                "zoneLogicalId",
                "rackCode",
                "x",
                "y",
                "z",
                "rotationZ",
                "width",
                "depth",
                "height",
                "levels",
            ],
            [typeof(SpaceUpdateLayoutRackLevelDto)] =
            [
                "levelNo",
                "bottomZ",
                "clearHeight",
                "binCount",
                "depthCount",
                "cellWidth",
                "cellDepth",
                "beamHeight",
            ],
            [typeof(SpaceDeleteLayoutObjectDto)] =
            [
                "cascade",
            ],
            [typeof(SpaceLocationCodeSegmentDto)] =
            [
                "key",
                "name",
                "source",
                "width",
                "pad",
                "start",
                "step",
                "separator",
                "upper",
                "fixedValue",
                "optional",
            ],
            [typeof(PreviewSpaceLocationCodesRequest)] =
            [
                "schemaVersion",
                "mode",
                "expectedFloorRevision",
                "expectedContentRevision",
            ],
            [typeof(SpaceLocationCodingRuleDto)] =
            [
                "ruleId",
                "ruleName",
                "scopeType",
                "ruleHash",
            ],
            [typeof(SpaceLocationCodeProposalItemDto)] =
            [
                "locationLogicalId",
                "rackLogicalId",
                "rackCode",
                "columnNo",
                "levelNo",
                "depthNo",
                "decision",
                "reason",
            ],
            [typeof(PreviewSpaceLocationCodesResponse)] =
            [
                "schemaVersion",
                "modelVersionId",
                "floorLogicalId",
                "mode",
                "baseFloorRevision",
                "baseContentRevision",
                "proposalHash",
                "ruleSetHash",
                "changedCount",
                "unchangedCount",
                "protectedCount",
                "rules",
                "items",
            ],
            [typeof(ApplySpaceLocationCodesRequest)] =
            [
                "schemaVersion",
                "commandBatchId",
                "clientInstanceId",
                "leaseId",
                "mode",
                "expectedFloorRevision",
                "expectedContentRevision",
                "proposalHash",
            ],
            [typeof(ApplySpaceLocationCodesResponse)] =
            [
                "commandBatchId",
                "floorRevision",
                "versionContentRevision",
                "proposalHash",
                "appliedCount",
                "appliedItems",
                "idempotentReplay",
            ],
            [typeof(SpaceLayoutCommandResultDto)] =
            [
                "commandId",
                "type",
                "targetLogicalId",
            ],
            [typeof(ApplySpaceLayoutCommandBatchResponse)] =
            [
                "commandBatchId",
                "floorRevision",
                "versionContentRevision",
                "appliedCommands",
                "affectedZones",
                "affectedAisles",
                "affectedRacks",
                "affectedRackLevels",
                "affectedLocations",
                "idempotentReplay",
            ],
            [typeof(SpaceElementCommandDto)] =
            [
                "commandId",
                "type",
                "targetLogicalId",
            ],
            [typeof(SpaceCreateElementDto)] =
            [
                "elementType",
                "geometryJson",
                "x",
                "y",
                "z",
                "rotationZ",
                "width",
                "height",
                "depth",
                "attributes",
            ],
            [typeof(ApplySpaceCadChangesetRequest)] =
            [
                "commandBatchId",
                "clientInstanceId",
                "leaseId",
                "expectedFloorRevision",
                "expectedContentRevision",
                "workspaceSha256",
                "changeIds",
            ],
            [typeof(ApplySpaceCadChangesetResponse)] =
            [
                "commandBatchId",
                "floorRevision",
                "versionContentRevision",
                "appliedChangeCount",
                "workspaceSha256",
                "idempotentReplay",
            ],
            [typeof(SpaceCadChangeV1)] =
            [
                "changeId",
                "kind",
                "logicalId",
                "sourceRef",
                "objectType",
                "isSelected",
                "canApply",
            ],
            [typeof(SpaceCadChangeSummaryV1)] =
            [
                "totalCount",
                "addCount",
                "modifyCount",
                "deleteCount",
                "conflictCount",
                "lowConfidenceCount",
                "unrecognizedCount",
                "selectedCount",
                "applyEligibleCount",
            ],
            [typeof(AcquireSpaceEditLeaseRequest)] =
            [
                "clientInstanceId",
            ],
            [typeof(ContinueSpaceEditLeaseRequest)] =
            [
                "clientInstanceId",
            ],
            [typeof(TakeoverSpaceEditLeaseRequest)] =
            [
                "clientInstanceId",
                "reason",
            ],
            [typeof(SpaceWmsRuntimeInventoryResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "warehouseCode",
                "source",
                "items",
            ],
            [typeof(SpaceWmsRuntimeInventoryLocateResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "warehouseCode",
                "source",
                "criteria",
                "locationCount",
                "floorCount",
                "items",
            ],
            [typeof(SpaceWmsRuntimeTaskResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "warehouseCode",
                "source",
                "items",
            ],
            [typeof(SpaceWmsRuntimeTaskPathResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "warehouseCode",
                "source",
                "taskId",
                "stopCount",
                "locatedStopCount",
                "floorCount",
                "zoneCount",
                "floorTransitionCount",
                "zoneTransitionCount",
                "totalQuantity",
                "crossFloor",
                "crossZone",
                "actualStops",
                "floors",
                "workloads",
                "aisles",
            ],
            [typeof(SpaceWmsRuntimeSourceDto)] =
            [
                "kind",
                "adapterId",
                "dataSourceId",
                "observedAtUtc",
                "receivedAtUtc",
                "delayMilliseconds",
                "clockSkewMilliseconds",
                "isSimulated",
                "isAvailable",
            ],
            [typeof(SpaceWmsRuntimeInventoryItemDto)] =
            [
                "locationLogicalId",
                "wmsLogicalId",
                "spaceLocationCode",
                "wmsLocationCode",
                "codeMatches",
                "floorLogicalId",
                "floorCode",
                "floorName",
                "floorLevel",
                "physicalQuantity",
                "allocatedQuantity",
            ],
            [typeof(SpaceWmsRuntimeInventoryLocateCriteriaDto)] =
            [
                "materialNumber",
                "lotNumber",
                "containerNumber",
                "ownerId",
            ],
            [typeof(SpaceWmsRuntimeInventoryLocateHitDto)] =
            [
                "locationLogicalId",
                "wmsLogicalId",
                "spaceLocationCode",
                "wmsLocationCode",
                "codeMatches",
                "floorLogicalId",
                "floorCode",
                "floorName",
                "floorLevel",
                "physicalQuantity",
                "allocatedQuantity",
                "materialNumbers",
                "lotNumbers",
                "containerNumbers",
                "ownerIds",
            ],
            [typeof(SpaceWmsRuntimeTaskItemDto)] =
            [
                "taskId",
                "taskType",
                "status",
                "sequenceNo",
                "locationLogicalId",
                "wmsLogicalId",
                "spaceLocationCode",
                "wmsLocationCode",
                "codeMatches",
                "floorLogicalId",
                "floorCode",
                "floorName",
                "floorLevel",
            ],
            [typeof(SpaceWmsRuntimeTaskFloorDto)] =
            [
                "floorLogicalId",
                "floorCode",
                "floorName",
                "floorLevel",
                "elevationMillimeters",
                "heightMillimeters",
                "stopCount",
                "totalQuantity",
            ],
            [typeof(SpaceWmsRuntimeTaskWorkloadDto)] =
            [
                "floorLogicalId",
                "floorCode",
                "stopCount",
                "totalQuantity",
            ],
            [typeof(SpaceWmsRuntimeTaskAisleDto)] =
            [
                "floorLogicalId",
                "zoneLogicalId",
                "aisleLogicalId",
                "aisleCode",
                "centerlineJson",
            ],
            [typeof(SpaceWmsRuntimeWarehouseOverviewResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "warehouseCode",
                "capturedAtUtc",
                "isRuntimeComplete",
                "model",
                "inventory",
                "tasks",
                "anomalies",
                "abc",
                "floors",
            ],
            [typeof(SpaceWmsRuntimeWarehouseModelKpiDto)] =
            [
                "floorCount",
                "areaAvailableFloorCount",
                "areaMissingFloorCount",
                "totalFloorAreaSquareMeters",
                "zoneCount",
                "rackCount",
                "rackFootprintSquareMeters",
                "rackFootprintRatePercent",
                "activeLocationCount",
            ],
            [typeof(SpaceWmsRuntimeWarehouseInventoryKpiDto)] =
            [
                "source",
                "inventoryLineCount",
                "occupiedLocationCount",
                "unoccupiedLocationCount",
                "occupiedLocationRatePercent",
                "occupiedLocationRateMethod",
                "capacityUtilizationPercent",
                "capacityUtilizationStatus",
                "capacityUtilizationReason",
                "distinctOwnerCount",
                "distinctMaterialCount",
                "distinctLotCount",
                "distinctContainerCount",
            ],
            [typeof(SpaceWmsRuntimeWarehouseTaskKpiDto)] =
            [
                "source",
                "activeTaskCount",
                "activeTaskStopCount",
            ],
            [typeof(SpaceWmsRuntimeWarehouseAnomalyKpiDto)] =
            [
                "activeDeviceAlarmCount",
                "criticalDeviceAlarmCount",
                "codeMismatchLocationCount",
                "overAllocatedInventoryLineCount",
                "areaMissingFloorCount",
                "unclassifiedAbcMaterialCount",
            ],
            [typeof(SpaceWmsRuntimeWarehouseAbcMaterialDto)] =
            [
                "materialNumber",
                "outboundMovementCount",
                "outboundQuantity",
                "previousCumulativeSharePercent",
                "cumulativeSharePercent",
                "rank",
                "occupiedLocationCount",
                "floorCount",
            ],
            [typeof(SpaceWmsRuntimeWarehouseAbcLocationMaterialDto)] =
            [
                "materialNumber",
                "rank",
            ],
            [typeof(SpaceWmsRuntimeWarehouseAbcLocationDto)] =
            [
                "locationLogicalId",
                "spaceLocationCode",
                "floorLogicalId",
                "floorCode",
                "rank",
                "materials",
            ],
            [typeof(SpaceWmsRuntimeWarehouseAbcDto)] =
            [
                "source",
                "windowDays",
                "windowStartDate",
                "windowEndDateExclusive",
                "transactionTimeBasis",
                "rankingMethod",
                "aThresholdPercent",
                "bThresholdPercent",
                "spatialMappingAvailable",
                "materialCount",
                "aCount",
                "bCount",
                "cCount",
                "unclassifiedCount",
                "materials",
                "locations",
            ],
            [typeof(SpaceWmsRuntimeWarehouseFloorKpiDto)] =
            [
                "floorLogicalId",
                "floorCode",
                "floorName",
                "floorLevel",
                "areaSquareMeters",
                "activeLocationCount",
                "occupiedLocationCount",
                "occupiedLocationRatePercent",
                "aLocationCount",
                "bLocationCount",
                "cLocationCount",
                "unclassifiedLocationCount",
            ],
            [typeof(CreateSpaceFieldPolicyRequest)] =
            [
                "name",
                "audienceType",
                "fields",
            ],
            [typeof(UpdateSpaceFieldPolicyRequest)] =
            [
                "name",
                "fields",
                "canExport",
                "status",
            ],
            [typeof(SpaceFieldPolicyFieldRequest)] =
            [
                "resourceType",
                "fieldName",
            ],
            [typeof(SpaceFieldPolicyFieldDto)] =
            [
                "resourceType",
                "fieldName",
                "maskingRule",
            ],
            [typeof(SpaceFieldPolicyDto)] =
            [
                "id",
                "name",
                "audienceType",
                "canExport",
                "status",
                "policyVersion",
                "fields",
                "createdAtUtc",
            ],
            [typeof(SaveSpaceExcelMappingProfileRequest)] =
            [
                "name",
                "definition",
            ],
            [typeof(SaveSpaceExcelMappingProfileResponse)] =
            [
                "profile",
                "created",
                "idempotentReplay",
            ],
            [typeof(PreviewSpaceExcelMappingRequest)] =
            [
                "definition",
                "workbook",
            ],
            [typeof(SpaceExcelHeaderSampleDto)] =
            [
                "sheetName",
                "headers",
            ],
            [typeof(SpaceExcelMappingDefinitionDto)] =
            [
                "schemaVersion",
                "unknownColumnPolicy",
                "emptyValuePolicy",
                "duplicateRowPolicy",
                "sheets",
            ],
            [typeof(SpaceExcelSheetMappingDto)] =
            [
                "targetSheet",
                "sourceSheet",
                "sheetMatchMode",
                "headerRow",
                "dataStartRow",
                "columns",
            ],
            [typeof(SpaceExcelColumnMappingDto)] =
            [
                "targetField",
                "dataType",
                "isBusinessKey",
            ],
            [typeof(SpaceExcelEnumConversionDto)] =
            [
                "sourceValue",
                "targetValue",
            ],
            [typeof(SpaceExcelMappingProfileDto)] =
            [
                "id",
                "name",
                "scope",
                "version",
                "isReadOnly",
                "definitionHash",
                "definition",
            ],
            [typeof(SpaceExcelMappingPreviewDto)] =
            [
                "canSave",
                "normalizedDefinition",
                "sheets",
                "issues",
            ],
            [typeof(SpaceExcelSheetPreviewDto)] =
            [
                "targetSheet",
                "sourceSheetPattern",
                "status",
                "columns",
                "unknownHeaders",
            ],
            [typeof(SpaceExcelColumnPreviewDto)] =
            [
                "targetField",
                "required",
                "status",
            ],
            [typeof(SpaceExcelMappingIssueDto)] =
            [
                "code",
                "severity",
                "message",
                "fixHint",
            ],
            [typeof(UploadSpaceExcelSourceResponse)] =
            [
                "file",
                "source",
                "reused",
            ],
            [typeof(StartSpaceExcelPreflightRequest)] =
            [
                "mappingProfileId",
                "mappingProfileVersion",
            ],
            [typeof(StartSpaceExcelPreflightResponse)] =
            [
                "jobId",
                "jobStatus",
                "jobStatusUrl",
                "previewUrl",
                "errorReportUrl",
                "mappingProfileId",
                "mappingProfileVersion",
                "mappingDefinitionHash",
                "source",
                "idempotentReplay",
            ],
            [typeof(SpaceExcelPreflightDto)] =
            [
                "jobId",
                "modelVersionId",
                "sourceId",
                "status",
                "sourceState",
                "mappingProfileId",
                "mappingProfileVersion",
                "mappingDefinitionHash",
                "parserVersion",
                "canConfirm",
                "infoCount",
                "warningCount",
                "blockingCount",
                "sheetCount",
                "dataRowCount",
                "validRowCount",
                "returnedIssueCount",
                "issuesTruncated",
                "errorReportUrl",
                "issues",
            ],
            [typeof(SpaceExcelPreflightIssueDto)] =
            [
                "id",
                "severity",
                "code",
                "messageArgsJson",
                "createdAtUtc",
            ],
            [typeof(SpaceAiApprovedProviderDto)] =
            [
                "alias",
                "kind",
            ],
            [typeof(SpaceAiPolicyDto)] =
            [
                "version",
                "dataPolicy",
                "allowedSiteIds",
                "allowedProviderAliases",
                "maxConcurrentRuns",
                "externalProviderEnabled",
                "approvedProviders",
            ],
            [typeof(UpdateSpaceAiPolicyRequest)] =
            [
                "expectedVersion",
                "dataPolicy",
                "allowedSiteIds",
                "allowedProviderAliases",
                "maxConcurrentRuns",
                "externalProviderEnabled",
            ],
            [typeof(UpdateSpaceAiPolicyResponse)] =
            [
                "policy",
                "idempotentReplay",
            ],
            [typeof(CreateSpaceAiAtomicApplyRequest)] =
            [
                "expectedContentRevision",
                "expectedRunRowVersion",
                "reviewEtag",
            ],
            [typeof(SpaceAiRunActionRequest)] =
            [
                "expectedRunRowVersion",
            ],
            [typeof(CreateSpaceAiGenerationRecoveryRequest)] =
            [
                "basedOnRunId",
                "expectedContentRevision",
                "expectedBasedOnRunRowVersion",
                "mode",
            ],
            [typeof(CreateSpaceAiGenerationRunRequest)] =
            [
                "sourceId",
                "mappingProfileVersionId",
                "rackGenerationProfileVersionId",
                "mode",
                "expectedContentRevision",
            ],
            [typeof(SpaceAiGenerationRunLinksDto)] =
            [
                "self",
                "proposals",
            ],
            [typeof(SpaceAiGenerationRunAcceptedDto)] =
            [
                "schemaVersion",
                "runId",
                "jobId",
                "status",
                "baseContentRevision",
                "sourceId",
                "sourceHash",
                "mode",
                "policy",
                "links",
                "reused",
                "idempotentReplay",
            ],
            [typeof(SpaceAiGenerationRunActionDto)] =
            [
                "schemaVersion",
                "runId",
                "status",
                "recoveryAction",
                "retryable",
                "cancellationPending",
                "idempotentReplay",
            ],
            [typeof(SpaceAiAtomicApplyAcceptedDto)] =
            [
                "schemaVersion",
                "runId",
                "jobId",
                "status",
                "expectedContentRevision",
                "reviewEtag",
                "idempotentReplay",
            ],
            [typeof(SpaceAiGenerationRunDto)] =
            [
                "schemaVersion",
                "runId",
                "siteId",
                "modelVersionId",
                "sourceId",
                "status",
                "progress",
                "baseContentRevision",
                "cancellationPending",
                "retryable",
                "recoveryAction",
                "applyCommitState",
                "rowVersion",
            ],
            [typeof(SpaceAiAppliedCountsDto)] =
            [
                "floors",
                "zones",
                "aisles",
                "racks",
                "rackLevels",
                "locations",
                "elements",
                "proposals",
            ],
            [typeof(SpaceAiUsageItemDto)] =
            [
                "id",
                "runId",
                "providerAlias",
                "providerModel",
                "inputUnits",
                "outputUnits",
                "estimatedCostMinor",
                "latencyMs",
                "outcome",
                "recordedAtUtc",
            ],
            [typeof(SpaceAiBudgetBalanceDto)] =
            [
                "consumedMinor",
            ],
            [typeof(SpaceAiUsageSummaryDto)] =
            [
                "totalRuns",
                "inputUnits",
                "outputUnits",
                "estimatedCostMinor",
                "actualCostMinor",
                "hasUnpricedUsage",
                "dailyBudget",
                "monthlyBudget",
            ],
            [typeof(SpaceAiUsagePageDto)] =
            [
                "items",
                "total",
                "page",
                "pageSize",
                "summary",
            ],
            [typeof(IngestSpacePersonnelEventsRequest)] =
            [
                "contractVersion",
                "sourceId",
                "sourceKind",
                "events",
            ],
            [typeof(SpacePersonnelEventInput)] =
            [
                "sourceEventId",
                "personExternalId",
                "eventKind",
                "occurredAtUtc",
            ],
            [typeof(IngestSpacePersonnelEventsResponse)] =
            [
                "contractVersion",
                "siteId",
                "sourceId",
                "sourceKind",
                "receivedAtUtc",
                "receivedCount",
                "acceptedCount",
                "duplicateCount",
                "staleCount",
                "receipts",
            ],
            [typeof(SpacePersonnelEventReceipt)] =
            [
                "eventId",
                "sourceEventId",
                "outcome",
                "projectionApplied",
            ],
            [typeof(SpacePersonnelCurrentPageDto)] =
            [
                "siteId",
                "asOfUtc",
                "freshnessThresholdSeconds",
                "items",
                "nextCursor",
            ],
            [typeof(SpacePersonnelCurrentDto)] =
            [
                "sourceId",
                "sourceKind",
                "personExternalId",
                "workState",
                "floorLogicalId",
                "locationLogicalId",
                "xMillimeters",
                "yMillimeters",
                "zMillimeters",
                "accuracyMillimeters",
                "positionOccurredAtUtc",
                "positionReceivedAtUtc",
                "positionEventId",
                "positionSourceEventId",
                "workStateOccurredAtUtc",
                "workStateReceivedAtUtc",
                "workStateEventId",
                "workStateSourceEventId",
                "positionAgeMilliseconds",
                "workStateAgeMilliseconds",
                "hasPosition",
                "positionIsStale",
                "workStateIsStale",
                "isSimulated",
            ],
            [typeof(SpacePersonnelTrajectoryResponse)] =
            [
                "siteId",
                "sourceId",
                "sourceKind",
                "personExternalId",
                "fromUtc",
                "toUtc",
                "retentionCutoffUtc",
                "items",
                "nextCursor",
            ],
            [typeof(SpacePersonnelTrajectoryPointDto)] =
            [
                "eventId",
                "sourceEventId",
                "floorLogicalId",
                "locationLogicalId",
                "xMillimeters",
                "yMillimeters",
                "zMillimeters",
                "accuracyMillimeters",
                "sourceSequence",
                "occurredAtUtc",
                "receivedAtUtc",
                "ingestDelayMilliseconds",
            ],
            [typeof(CreateSpaceDeviceMappingRequest)] =
            [
                "sourceId",
                "sourceKind",
                "deviceExternalId",
                "deviceKind",
                "elementLogicalId",
            ],
            [typeof(UpdateSpaceDeviceMappingRequest)] =
            [
                "deviceKind",
                "elementLogicalId",
                "expectedRowVersion",
            ],
            [typeof(SpaceDeviceMappingDto)] =
            [
                "id",
                "siteId",
                "sourceId",
                "sourceKind",
                "deviceExternalId",
                "deviceKind",
                "elementLogicalId",
                "elementType",
                "validatedModelVersionId",
                "validatedFloorLogicalId",
                "rowVersion",
            ],
            [typeof(SpaceDeviceMappingPageDto)] =
            [
                "items",
                "nextCursor",
            ],
            [typeof(IngestSpaceDeviceEventsRequest)] =
            [
                "contractVersion",
                "sourceId",
                "sourceKind",
                "events",
            ],
            [typeof(SpaceDeviceEventInput)] =
            [
                "sourceEventId",
                "deviceExternalId",
                "eventKind",
                "occurredAtUtc",
            ],
            [typeof(IngestSpaceDeviceEventsResponse)] =
            [
                "contractVersion",
                "siteId",
                "sourceId",
                "sourceKind",
                "receivedAtUtc",
                "receivedCount",
                "acceptedCount",
                "duplicateCount",
                "staleCount",
                "receipts",
            ],
            [typeof(SpaceDeviceEventReceipt)] =
            [
                "eventId",
                "sourceEventId",
                "deviceExternalId",
                "outcome",
                "projectionApplied",
            ],
            [typeof(SpaceDeviceCurrentPageDto)] =
            [
                "siteId",
                "publishedVersionId",
                "asOfUtc",
                "freshnessThresholdSeconds",
                "items",
                "nextCursor",
            ],
            [typeof(SpaceDeviceCurrentDto)] =
            [
                "mappingId",
                "sourceId",
                "sourceKind",
                "deviceExternalId",
                "deviceKind",
                "elementLogicalId",
                "elementType",
                "mappingIsCurrent",
                "mappedFloorLogicalId",
                "mappedXMillimeters",
                "mappedYMillimeters",
                "mappedZMillimeters",
                "operatingState",
                "floorLogicalId",
                "locationLogicalId",
                "xMillimeters",
                "yMillimeters",
                "zMillimeters",
                "accuracyMillimeters",
                "positionOccurredAtUtc",
                "positionReceivedAtUtc",
                "positionEventId",
                "positionSourceEventId",
                "operatingStateOccurredAtUtc",
                "operatingStateReceivedAtUtc",
                "operatingStateEventId",
                "operatingStateSourceEventId",
                "positionAgeMilliseconds",
                "operatingStateAgeMilliseconds",
                "hasPosition",
                "positionIsStale",
                "operatingStateIsStale",
                "isSimulated",
                "hasActiveAlarm",
                "activeAlarmCount",
                "maximumActiveAlarmSeverity",
                "activeAlarms",
            ],
            [typeof(SpaceDeviceActiveAlarmDto)] =
            [
                "alarmExternalId",
                "alarmCode",
                "alarmSeverity",
                "alarmMessage",
                "occurredAtUtc",
                "receivedAtUtc",
                "eventId",
                "sourceEventId",
                "ageMilliseconds",
            ],
            [typeof(SpacePortalOrganizationDto)] =
            [
                "organizationId",
                "type",
                "code",
                "name",
                "role",
                "validFromUtc",
                "organizationSecurityStamp",
                "membershipSecurityStamp",
            ],
            [typeof(SpacePortalSiteDto)] =
            [
                "siteId",
                "publishedVersionId",
                "canViewScene",
                "canViewStock",
                "canViewTasks",
                "canExport",
                "authorizationVersion",
            ],
            [typeof(SpacePortalPublishedSceneDto)] =
            [
                "siteId",
                "publishedVersionId",
                "authorizationVersion",
                "floors",
            ],
            [typeof(SpacePortalFloorDto)] =
            [
                "logicalId",
                "zones",
                "aisles",
                "racks",
                "rackLevels",
                "locations",
                "elements",
            ],
            [typeof(SpacePortalZoneDto)] =
            [
                "logicalId",
                "floorLogicalId",
            ],
            [typeof(SpacePortalAisleDto)] =
            [
                "logicalId",
                "zoneLogicalId",
            ],
            [typeof(SpacePortalRackDto)] =
            [
                "logicalId",
                "floorLogicalId",
                "zoneLogicalId",
            ],
            [typeof(SpacePortalRackLevelDto)] =
            [
                "logicalId",
                "rackLogicalId",
            ],
            [typeof(SpacePortalLocationDto)] =
            [
                "logicalId",
                "floorLogicalId",
            ],
            [typeof(SpacePortalElementDto)] =
            [
                "logicalId",
                "floorLogicalId",
            ],
            [typeof(SpacePortalRuntimeSourceDto)] =
            [
                "observedAtUtc",
                "receivedAtUtc",
                "delayMilliseconds",
                "isAvailable",
            ],
            [typeof(SpacePortalStockResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "authorizationVersion",
                "source",
                "items",
            ],
            [typeof(SpacePortalStockItemDto)] =
            [
                "locationLogicalId",
                "floorLogicalId",
            ],
            [typeof(SpacePortalTaskResponse)] =
            [
                "siteId",
                "publishedVersionId",
                "authorizationVersion",
                "source",
                "items",
            ],
            [typeof(SpacePortalTaskItemDto)] =
            [
                "locationLogicalId",
                "floorLogicalId",
                "zoneLogicalId",
            ],
        };

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!RequiredProperties.TryGetValue(
                context.Type,
                out var requiredProperties))
            return;

        schema.Required.Clear();
        foreach (var propertyName in requiredProperties)
        {
            schema.Required.Add(propertyName);
            Property(schema, propertyName).Nullable = false;
        }

        if (context.Type == typeof(SpaceWmsRuntimeInventoryItemDto))
        {
            SetNullable(
                schema,
                true,
                "materialNumber",
                "lotNumber",
                "containerNumber",
                "ownerId");
            SetNumberFormat(schema, "physicalQuantity", "decimal", false);
            SetNumberFormat(schema, "allocatedQuantity", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeInventoryLocateCriteriaDto))
        {
            SetNullable(
                schema,
                true,
                "materialNumber",
                "lotNumber",
                "containerNumber",
                "ownerId");
        }
        else if (context.Type == typeof(SpaceWmsRuntimeInventoryLocateHitDto))
        {
            SetNumberFormat(schema, "physicalQuantity", "decimal", false);
            SetNumberFormat(schema, "allocatedQuantity", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeTaskItemDto))
        {
            SetNullable(
                schema,
                true,
                "zoneLogicalId",
                "zoneCode",
                "rackLogicalId",
                "rackCode",
                "anchorXMillimeters",
                "anchorYMillimeters",
                "anchorZMillimeters",
                "quantity",
                "materialNumber");
            SetNumberFormat(schema, "quantity", "decimal", true);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeTaskPathResponse))
        {
            SetNumberFormat(schema, "totalQuantity", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeTaskFloorDto))
        {
            SetNumberFormat(schema, "totalQuantity", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeTaskWorkloadDto))
        {
            SetNullable(schema, true, "zoneLogicalId", "zoneCode");
            SetNumberFormat(schema, "totalQuantity", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseModelKpiDto))
        {
            SetNullable(
                schema,
                true,
                "totalFloorAreaSquareMeters",
                "rackFootprintRatePercent");
            SetNumberFormat(schema, "totalFloorAreaSquareMeters", "decimal", true);
            SetNumberFormat(schema, "rackFootprintSquareMeters", "decimal", false);
            SetNumberFormat(schema, "rackFootprintRatePercent", "decimal", true);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseInventoryKpiDto))
        {
            SetNullable(
                schema,
                true,
                "inventoryLineCount",
                "occupiedLocationCount",
                "unoccupiedLocationCount",
                "occupiedLocationRatePercent",
                "capacityUtilizationPercent",
                "distinctOwnerCount",
                "distinctMaterialCount",
                "distinctLotCount",
                "distinctContainerCount");
            SetNumberFormat(schema, "occupiedLocationRatePercent", "decimal", true);
            SetNumberFormat(schema, "capacityUtilizationPercent", "decimal", true);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseTaskKpiDto))
        {
            SetNullable(schema, true, "activeTaskCount", "activeTaskStopCount");
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseAnomalyKpiDto))
        {
            SetNullable(
                schema,
                true,
                "codeMismatchLocationCount",
                "overAllocatedInventoryLineCount",
                "unclassifiedAbcMaterialCount");
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseAbcMaterialDto))
        {
            SetNullable(
                schema,
                true,
                "previousCumulativeSharePercent",
                "cumulativeSharePercent");
            SetNumberFormat(schema, "outboundQuantity", "decimal", false);
            SetNumberFormat(schema, "previousCumulativeSharePercent", "decimal", true);
            SetNumberFormat(schema, "cumulativeSharePercent", "decimal", true);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseAbcDto))
        {
            SetNullable(
                schema,
                true,
                "materialCount",
                "aCount",
                "bCount",
                "cCount",
                "unclassifiedCount");
            SetNumberFormat(schema, "aThresholdPercent", "decimal", false);
            SetNumberFormat(schema, "bThresholdPercent", "decimal", false);
        }
        else if (context.Type == typeof(SpaceWmsRuntimeWarehouseFloorKpiDto))
        {
            SetNullable(
                schema,
                true,
                "areaSquareMeters",
                "occupiedLocationCount",
                "occupiedLocationRatePercent",
                "aLocationCount",
                "bLocationCount",
                "cLocationCount",
                "unclassifiedLocationCount");
            SetNumberFormat(schema, "areaSquareMeters", "decimal", true);
            SetNumberFormat(schema, "occupiedLocationRatePercent", "decimal", true);
        }
        else if (context.Type == typeof(SpacePortalRackDto))
        {
            SetNumberFormat(schema, "rotationZ", "decimal", true);
        }
        else if (context.Type == typeof(SpacePortalRackLevelDto))
        {
            SetNumberFormat(schema, "maxLoad", "decimal", true);
        }
        else if (context.Type == typeof(SpacePortalLocationDto))
        {
            SetNumberFormat(schema, "maxLoad", "decimal", true);
        }
        else if (context.Type == typeof(SpacePortalStockItemDto))
        {
            SetNumberFormat(schema, "physicalQuantity", "decimal", true);
            SetNumberFormat(schema, "allocatedQuantity", "decimal", true);
        }
        else if (context.Type == typeof(SpacePortalTaskItemDto))
        {
            SetNullable(schema, true, "zoneLogicalId");
            SetNumberFormat(schema, "quantity", "decimal", true);
        }
        else if (context.Type == typeof(SpacePersonnelEventInput))
        {
            SetNullable(
                schema,
                true,
                "userId",
                "workState",
                "floorLogicalId",
                "locationLogicalId",
                "sourceSequence");
            SetNumberFormat(schema, "xMillimeters", "decimal", true);
            SetNumberFormat(schema, "yMillimeters", "decimal", true);
            SetNumberFormat(schema, "zMillimeters", "decimal", true);
            SetNumberFormat(schema, "accuracyMillimeters", "decimal", true);
        }
        else if (context.Type == typeof(SpacePersonnelCurrentPageDto))
        {
            SetNullable(schema, true, "nextCursor");
        }
        else if (context.Type == typeof(SpacePersonnelCurrentDto))
        {
            SetNullable(
                schema,
                true,
                "floorLogicalId",
                "locationLogicalId",
                "positionOccurredAtUtc",
                "positionReceivedAtUtc",
                "positionEventId",
                "positionSourceEventId",
                "workStateOccurredAtUtc",
                "workStateReceivedAtUtc",
                "workStateEventId",
                "workStateSourceEventId",
                "positionAgeMilliseconds",
                "workStateAgeMilliseconds");
            SetNumberFormat(schema, "xMillimeters", "decimal", true);
            SetNumberFormat(schema, "yMillimeters", "decimal", true);
            SetNumberFormat(schema, "zMillimeters", "decimal", true);
            SetNumberFormat(schema, "accuracyMillimeters", "decimal", true);
        }
        else if (context.Type == typeof(SpacePersonnelTrajectoryResponse))
        {
            SetNullable(schema, true, "nextCursor");
        }
        else if (context.Type == typeof(SpacePersonnelTrajectoryPointDto))
        {
            SetNullable(
                schema,
                true,
                "floorLogicalId",
                "locationLogicalId",
                "sourceSequence");
            SetNumberFormat(schema, "xMillimeters", "decimal", true);
            SetNumberFormat(schema, "yMillimeters", "decimal", true);
            SetNumberFormat(schema, "zMillimeters", "decimal", true);
            SetNumberFormat(schema, "accuracyMillimeters", "decimal", true);
        }
        else if (context.Type == typeof(SpaceDeviceCurrentPageDto))
        {
            SetNullable(schema, true, "nextCursor");
        }
        else if (context.Type == typeof(SpaceDeviceCurrentDto))
        {
            SetNullable(
                schema,
                true,
                "mappedFloorLogicalId",
                "mappedXMillimeters",
                "mappedYMillimeters",
                "mappedZMillimeters",
                "floorLogicalId",
                "locationLogicalId",
                "positionOccurredAtUtc",
                "positionReceivedAtUtc",
                "positionEventId",
                "positionSourceEventId",
                "operatingStateOccurredAtUtc",
                "operatingStateReceivedAtUtc",
                "operatingStateEventId",
                "operatingStateSourceEventId",
                "positionAgeMilliseconds",
                "operatingStateAgeMilliseconds",
                "maximumActiveAlarmSeverity");
            SetNumberFormat(schema, "mappedXMillimeters", "decimal", true);
            SetNumberFormat(schema, "mappedYMillimeters", "decimal", true);
            SetNumberFormat(schema, "mappedZMillimeters", "decimal", true);
            SetNumberFormat(schema, "xMillimeters", "decimal", true);
            SetNumberFormat(schema, "yMillimeters", "decimal", true);
            SetNumberFormat(schema, "zMillimeters", "decimal", true);
            SetNumberFormat(schema, "accuracyMillimeters", "decimal", true);
        }
        else if (context.Type == typeof(SpaceDeviceActiveAlarmDto))
        {
            SetNullable(schema, true, "alarmMessage", "ageMilliseconds");
        }
    }

    private static OpenApiSchema Property(
        OpenApiSchema schema,
        string propertyName) =>
        schema.Properties.TryGetValue(propertyName, out var property)
            ? property
            : throw new InvalidOperationException(
                $"Runtime schema property '{propertyName}' was not generated.");

    private static void SetNullable(
        OpenApiSchema schema,
        bool nullable,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            Property(schema, propertyName).Nullable = nullable;
    }

    private static void SetNumberFormat(
        OpenApiSchema schema,
        string propertyName,
        string format,
        bool nullable)
    {
        var property = Property(schema, propertyName);
        property.Type = "number";
        property.Format = format;
        property.Nullable = nullable;
    }
}

public sealed class SpaceDesignV1OperationFilter : IOperationFilter
{
    public void Apply(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        if (operation.OperationId == "GetTaskPath")
        {
            var taskId = operation.Parameters.Single(
                parameter =>
                    parameter.In == ParameterLocation.Query &&
                    string.Equals(
                        parameter.Name,
                        "taskId",
                        StringComparison.Ordinal));
            taskId.Required = true;
            taskId.Description =
                "WMS task identity; normalized by trimming and upper-casing. " +
                "1-100 characters.";
            return;
        }

        if (operation.OperationId is not (
                "CreateVersion" or
                "CreateSource" or
                "CreateAsset" or
                "AttachUnderlay" or
                "CalibrateUnderlay" or
                "UpdatePolicy" or
                "ApplyGenerationProposals" or
                "CancelGenerationRun" or
                "RetryGenerationRun" or
                "DiscardGenerationRun" or
                "ReconcileGenerationRun" or
                "CreateGenerationRun" or
                "CreateRackGenerationProfile"))
            return;

        var idempotencyKey = operation.Parameters.Single(
            parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(
                    parameter.Name,
                    "Idempotency-Key",
                    StringComparison.Ordinal));
        idempotencyKey.Required = true;
        idempotencyKey.Description =
            "Opaque caller key; 1-128 UTF-8 bytes. Reuse with a different " +
            "request returns SPACE_IDEMPOTENCY_KEY_REUSED.";

        if (operation.OperationId == "CreateGenerationRun")
        {
            var ifMatch = operation.Parameters.Single(
                parameter =>
                    parameter.In == ParameterLocation.Header &&
                    string.Equals(
                        parameter.Name,
                        "If-Match",
                        StringComparison.Ordinal));
            ifMatch.Required = true;
            ifMatch.Description =
                "Current Draft RowVersion, optionally quoted as an ETag.";
        }

        var successStatus = operation.OperationId switch
        {
            "CreateVersion" or
                "ApplyGenerationProposals" or
                "RetryGenerationRun" or
                "CreateGenerationRun" =>
                StatusCodes.Status202Accepted.ToString(),
            "AttachUnderlay" or
                "CalibrateUnderlay" or
                "UpdatePolicy" or
                "CancelGenerationRun" or
                "DiscardGenerationRun" or
                "ReconcileGenerationRun" =>
                StatusCodes.Status200OK.ToString(),
            _ => StatusCodes.Status201Created.ToString(),
        };
        operation.Responses[successStatus].Headers["Idempotent-Replay"] =
            new OpenApiHeader
            {
                Description =
                    "True when this response replays a stored result.",
                Schema = new OpenApiSchema { Type = "boolean" },
            };
    }
}
