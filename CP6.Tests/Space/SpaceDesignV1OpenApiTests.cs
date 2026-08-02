using System.Text.Json;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Space;

public sealed class SpaceDesignV1OpenApiTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string OpenApiPath = Path.Combine(
        RepositoryRoot,
        "docs",
        "space",
        "contracts",
        "design-v1.openapi.json");

    [Fact]
    public void Contract_contains_the_frozen_paths_and_scene_operation()
    {
        using var document = ReadContract();
        var paths = document.RootElement.GetProperty("paths");
        var expectedPaths = new[]
        {
            "/api/space/design/v1/sites/{siteId}/model",
            "/api/space/design/v1/sites/{siteId}/device-events",
            "/api/space/design/v1/sites/{siteId}/devices",
            "/api/space/design/v1/sites/{siteId}/device-mappings",
            "/api/space/design/v1/sites/{siteId}/device-mappings/{mappingId}",
            "/api/space/design/v1/sites/{siteId}/personnel-events",
            "/api/space/design/v1/sites/{siteId}/personnel",
            "/api/space/design/v1/sites/{siteId}/personnel/trajectory",
            "/api/space/design/v1/sites/{siteId}/versions",
            "/api/space/design/v1/sites/{siteId}/runtime/inventory",
            "/api/space/design/v1/sites/{siteId}/runtime/inventory/locate",
            "/api/space/design/v1/sites/{siteId}/runtime/overview",
            "/api/space/design/v1/sites/{siteId}/runtime/tasks",
            "/api/space/design/v1/sites/{siteId}/runtime/tasks/path",
            "/api/space/design/v1/assets",
            "/api/space/design/v1/ai-policy",
            "/api/space/design/v1/ai-usage",
            "/api/space/design/v1/mapping-profiles/excel",
            "/api/space/design/v1/mapping-profiles/excel/{profileId}",
            "/api/space/design/v1/mapping-profiles/excel/preview",
            "/api/space/design/v1/modeling-templates/excel/standard",
            "/api/space/design/v1/versions/{versionId}",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/scene",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/underlay",
            "/api/space/design/v1/versions/{versionId}/files/{fileId}",
            "/api/space/design/v1/versions/{versionId}/excel-sources",
            "/api/space/design/v1/versions/{versionId}/sources",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/content",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights/{jobId}",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights/{jobId}/report",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration",
            "/api/space/design/v1/versions/{versionId}/underlay-sources",
            "/api/space/design/v1/jobs/{jobId}",
            "/api/space/design/v1/versions/{versionId}/issues",
            "/api/space/design/v1/versions/{versionId}/wms-adoption/refresh",
            "/api/space/design/v1/versions/{versionId}/wms-adoption/locations",
            "/api/space/design/v1/versions/{versionId}/wms-adoption/locations/{adoptionId}/bind",
            "/api/space/design/v1/versions/{versionId}/wms-adoption/bindings:batch",
            "/api/space/design/v1/versions/{versionId}/wms-adoption/locations/{adoptionId}/place",
            "/api/space/external-organization",
            "/api/space/external-organization/{organizationId}",
            "/api/space/external-organization/{organizationId}/membership",
            "/api/space/external-organization/{organizationId}/membership/{membershipId}",
            "/api/space/external-organization/{organizationId}/grant",
            "/api/space/external-organization/{organizationId}/grant/{grantId}",
            "/api/space/field-policy",
            "/api/space/field-policy/{policyId}",
            "/api/space/portal/v1/organizations",
            "/api/space/portal/v1/sites",
            "/api/space/portal/v1/sites/{siteId}/published-scene",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/exports/gltf",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/historical-datasets",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/historical-datasets/{datasetId}",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/simulation-runs",
            "/api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/simulation-runs/{runId}",
            "/api/space/planning/v1/sites/{siteId}/comparisons",
            "/api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}",
            "/api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}/decisions",
            "/api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}/decisions/{decisionId}",
            "/api/space/portal/v1/sites/{siteId}/stock",
            "/api/space/portal/v1/sites/{siteId}/tasks",
        };

        Assert.Equal(
            expectedPaths.OrderBy(value => value),
            paths.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(value => value));

        var operationIds = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(IsOperation)
            .Select(operation =>
                operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        Assert.Equal(84, operationIds.Length);
        Assert.Equal(84, operationIds.Distinct().Count());
        Assert.Contains("GetPolicy", operationIds);
        Assert.Contains("UpdatePolicy", operationIds);
        Assert.Contains("GetUsage", operationIds);
        Assert.Contains("IngestPersonnelEvents", operationIds);
        Assert.Contains("GetCurrentPersonnel", operationIds);
        Assert.Contains("GetPersonnelTrajectory", operationIds);
        Assert.Contains("GetDeviceMappings", operationIds);
        Assert.Contains("CreateDeviceMapping", operationIds);
        Assert.Contains("UpdateDeviceMapping", operationIds);
        Assert.Contains("IngestDeviceEvents", operationIds);
        Assert.Contains("GetAssets", operationIds);
        Assert.Contains("DownloadStandardExcelTemplate", operationIds);
        Assert.Contains("GetProfiles", operationIds);
        Assert.Contains("GetProfile", operationIds);
        Assert.Contains("Preview", operationIds);
        Assert.Contains("SaveProfile", operationIds);
        Assert.Contains("UploadExcelSource", operationIds);
        Assert.Contains("StartPreflight", operationIds);
        Assert.Contains("GetPreflight", operationIds);
        Assert.Contains("DownloadErrorReport", operationIds);
        Assert.Contains("CreateAsset", operationIds);
        Assert.Contains("CreateVersion", operationIds);
        Assert.Contains("CreateSource", operationIds);
        Assert.Contains("GetScene", operationIds);
        Assert.Contains("ApplyElementCommands", operationIds);
        Assert.Contains("UploadUnderlay", operationIds);
        Assert.Contains("GetFile", operationIds);
        Assert.Contains("GetUnderlayContent", operationIds);
        Assert.Contains("AttachUnderlay", operationIds);
        Assert.Contains("GetUnderlayCalibration", operationIds);
        Assert.Contains("CalibrateUnderlay", operationIds);
        Assert.Contains("RefreshWmsAdoption", operationIds);
        Assert.Contains("GetWmsAdoptionLocations", operationIds);
        Assert.Contains("BindWmsAdoption", operationIds);
        Assert.Contains("BindWmsAdoptionBatch", operationIds);
        Assert.Contains("PlaceWmsAdoption", operationIds);
        Assert.Contains("GetInventory", operationIds);
        Assert.Contains("LocateInventory", operationIds);
        Assert.Contains("GetWarehouseOverview", operationIds);
        Assert.Contains("GetTasks", operationIds);
        Assert.Contains("GetTaskPath", operationIds);
        Assert.Contains("GetOrganizations", operationIds);
        Assert.Contains("GetOrganization", operationIds);
        Assert.Contains("CreateOrganization", operationIds);
        Assert.Contains("UpdateOrganization", operationIds);
        Assert.Contains("GetMemberships", operationIds);
        Assert.Contains("CreateMembership", operationIds);
        Assert.Contains("UpdateMembership", operationIds);
        Assert.Contains("GetGrants", operationIds);
        Assert.Contains("GetGrant", operationIds);
        Assert.Contains("CreateGrant", operationIds);
        Assert.Contains("UpdateGrant", operationIds);
        Assert.Contains("GetFieldPolicies", operationIds);
        Assert.Contains("GetFieldPolicy", operationIds);
        Assert.Contains("CreateFieldPolicy", operationIds);
        Assert.Contains("UpdateFieldPolicy", operationIds);
        Assert.Contains("GetPortalOrganizations", operationIds);
        Assert.Contains("GetPortalSites", operationIds);
        Assert.Contains("GetPortalPublishedScene", operationIds);
        Assert.Contains("CreateBranch", operationIds);
        Assert.Contains("GetBranch", operationIds);
        Assert.Contains("GetBranches", operationIds);
        Assert.Contains("DownloadGlb", operationIds);
        Assert.Contains("CreateHistoricalDataset", operationIds);
        Assert.Contains("GetHistoricalDataset", operationIds);
        Assert.Contains("GetHistoricalDatasets", operationIds);
        Assert.Contains("CreateSimulationRun", operationIds);
        Assert.Contains("GetSimulationRun", operationIds);
        Assert.Contains("GetSimulationRuns", operationIds);
        Assert.Contains("CreateComparison", operationIds);
        Assert.Contains("GetComparison", operationIds);
        Assert.Contains("GetComparisons", operationIds);
        Assert.Contains("CreateDecision", operationIds);
        Assert.Contains("GetDecision", operationIds);
        Assert.Contains("GetDecisions", operationIds);
        Assert.Contains("GetPortalStock", operationIds);
        Assert.Contains("GetPortalTasks", operationIds);

        var taskIdParameter = paths
            .GetProperty("/api/space/design/v1/sites/{siteId}/runtime/tasks/path")
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() == "taskId");
        Assert.True(taskIdParameter.GetProperty("required").GetBoolean());
        Assert.Equal("query", taskIdParameter.GetProperty("in").GetString());

        var exchange = paths.GetProperty(
                "/api/space/planning/v1/sites/{siteId}/scenario-branches/" +
                "{branchId}/exports/gltf")
            .GetProperty("get");
        var exchangeResponses = exchange.GetProperty("responses");
        var successContent = exchangeResponses.GetProperty("200")
            .GetProperty("content");
        Assert.Equal(
            new[] { "model/gltf-binary" },
            successContent.EnumerateObject().Select(value => value.Name));
        Assert.Equal(
            "binary",
            successContent.GetProperty("model/gltf-binary")
                .GetProperty("schema")
                .GetProperty("format")
                .GetString());
        Assert.True(exchangeResponses.GetProperty("409")
            .GetProperty("content")
            .TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public void Ai_admin_contract_is_idempotent_and_never_accepts_provider_secrets()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var update = root.GetProperty("paths")
            .GetProperty("/api/space/design/v1/ai-policy")
            .GetProperty("put");
        var idempotency = update.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(update.GetProperty("responses")
            .GetProperty("200")
            .GetProperty("headers")
            .TryGetProperty("Idempotent-Replay", out _));

        var requestProperties = root.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(
                "CP6.Space.Contracts.UpdateSpaceAiPolicyRequest")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "allowedProviderAliases",
                "allowedSiteIds",
                "currency",
                "dailyBudgetMinor",
                "dataPolicy",
                "expectedVersion",
                "externalProviderEnabled",
                "maxConcurrentRuns",
                "monthlyBudgetMinor",
            },
            requestProperties.Order());
        Assert.DoesNotContain(requestProperties, property =>
            property.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            property.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            property.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            property.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Personnel_ingest_contract_is_explicit_idempotent_and_never_infers_location()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/sites/{siteId}/personnel-events")
            .GetProperty("post");
        Assert.Equal(
            "IngestPersonnelEvents",
            operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("requestBody")
            .GetProperty("required")
            .GetBoolean());
        Assert.True(operation.GetProperty("responses").TryGetProperty("202", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var requestRequired = schemas
            .GetProperty(
                "CP6.Space.Contracts.IngestSpacePersonnelEventsRequest")
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.Equal(
            new[] { "contractVersion", "events", "sourceId", "sourceKind" },
            requestRequired.Order());

        var eventSchema = schemas.GetProperty(
            "CP6.Space.Contracts.SpacePersonnelEventInput");
        Assert.Equal(
            new[]
            {
                "eventKind", "occurredAtUtc", "personExternalId", "sourceEventId",
            },
            eventSchema.GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Order());
        var properties = eventSchema.GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        Assert.DoesNotContain(properties, value =>
            value.Contains("inferred", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("displayName", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("estimated", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            operation.GetProperty("parameters").EnumerateArray(),
            value => value.GetProperty("name").GetString() == "Idempotency-Key");
    }

    [Fact]
    public void Personnel_runtime_contract_is_bounded_traceable_and_privacy_minimal()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var current = paths
            .GetProperty("/api/space/design/v1/sites/{siteId}/personnel")
            .GetProperty("get");
        var trajectory = paths
            .GetProperty(
                "/api/space/design/v1/sites/{siteId}/personnel/trajectory")
            .GetProperty("get");

        Assert.Equal(
            "GetCurrentPersonnel",
            current.GetProperty("operationId").GetString());
        Assert.Equal(
            "GetPersonnelTrajectory",
            trajectory.GetProperty("operationId").GetString());
        foreach (var requiredQuery in new[]
                 {
                     "personExternalId", "sourceId", "fromUtc", "toUtc",
                 })
        {
            var parameter = trajectory.GetProperty("parameters")
                .EnumerateArray()
                .Single(value =>
                    value.GetProperty("name").GetString() == requiredQuery);
            Assert.True(parameter.GetProperty("required").GetBoolean());
        }

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var currentProperties = schemas
            .GetProperty("CP6.Space.Contracts.SpacePersonnelCurrentDto")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        Assert.Contains("positionSourceEventId", currentProperties);
        Assert.Contains("positionIsStale", currentProperties);
        Assert.Contains("isSimulated", currentProperties);
        Assert.DoesNotContain(currentProperties, value =>
            value.Contains("name", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("userId", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("email", StringComparison.OrdinalIgnoreCase));

        var trajectoryProperties = schemas
            .GetProperty("CP6.Space.Contracts.SpacePersonnelTrajectoryPointDto")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        Assert.Contains("eventId", trajectoryProperties);
        Assert.Contains("sourceEventId", trajectoryProperties);
        Assert.DoesNotContain(trajectoryProperties, value =>
            value.Contains("userId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Device_contract_freezes_mapping_and_append_only_event_shapes()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var mappings = paths.GetProperty(
            "/api/space/design/v1/sites/{siteId}/device-mappings");
        var update = paths.GetProperty(
                "/api/space/design/v1/sites/{siteId}/device-mappings/{mappingId}")
            .GetProperty("put");
        var ingest = paths.GetProperty(
                "/api/space/design/v1/sites/{siteId}/device-events")
            .GetProperty("post");
        var current = paths.GetProperty(
                "/api/space/design/v1/sites/{siteId}/devices")
            .GetProperty("get");

        Assert.Equal(
            "GetDeviceMappings",
            mappings.GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal(
            "CreateDeviceMapping",
            mappings.GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal(
            "UpdateDeviceMapping",
            update.GetProperty("operationId").GetString());
        Assert.Equal(
            "IngestDeviceEvents",
            ingest.GetProperty("operationId").GetString());
        Assert.Equal(
            "GetCurrentDevices",
            current.GetProperty("operationId").GetString());
        Assert.True(ingest.GetProperty("responses").TryGetProperty("202", out _));
        Assert.DoesNotContain(
            ingest.GetProperty("parameters").EnumerateArray(),
            value => value.GetProperty("name").GetString() == "Idempotency-Key");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.CreateSpaceDeviceMappingRequest"),
            "sourceId",
            "sourceKind",
            "deviceExternalId",
            "deviceKind",
            "elementLogicalId");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.UpdateSpaceDeviceMappingRequest"),
            "deviceKind",
            "elementLogicalId",
            "expectedRowVersion");
        var eventSchema = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceDeviceEventInput");
        AssertExactRequired(
            eventSchema,
            "sourceEventId",
            "deviceExternalId",
            "eventKind",
            "occurredAtUtc");
        var properties = eventSchema.GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        Assert.Contains("operatingState", properties);
        Assert.Contains("alarmExternalId", properties);
        Assert.Contains("sourceSequence", properties);
        Assert.DoesNotContain(properties, value =>
            value.Contains("inferred", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("estimated", StringComparison.OrdinalIgnoreCase));

        var currentProperties = Schema(
                schemas,
                "CP6.Space.Contracts.SpaceDeviceCurrentDto")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        Assert.Contains("mappingIsCurrent", currentProperties);
        Assert.Contains("mappedXMillimeters", currentProperties);
        Assert.Contains("positionSourceEventId", currentProperties);
        Assert.Contains("operatingStateSourceEventId", currentProperties);
        Assert.Contains("positionIsStale", currentProperties);
        Assert.Contains("isSimulated", currentProperties);
        Assert.Contains("activeAlarms", currentProperties);
        Assert.DoesNotContain(currentProperties, value =>
            value.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("command", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("acknowledge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Standard_modeling_template_is_a_bounded_excel_download()
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/space/design/v1/modeling-templates/excel/standard")
            .GetProperty("get");
        var response = operation.GetProperty("responses").GetProperty("200");
        var schema = response.GetProperty("content")
            .GetProperty(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("binary", schema.GetProperty("format").GetString());
        Assert.False(operation.TryGetProperty("parameters", out _));
    }

    [Fact]
    public void Excel_mapping_contract_freezes_versioning_preview_and_idempotency()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var collection = paths.GetProperty(
            "/api/space/design/v1/mapping-profiles/excel");
        var preview = paths.GetProperty(
            "/api/space/design/v1/mapping-profiles/excel/preview")
            .GetProperty("post");
        var item = paths.GetProperty(
            "/api/space/design/v1/mapping-profiles/excel/{profileId}")
            .GetProperty("get");

        var idempotency = collection.GetProperty("post")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.Equal("header", idempotency.GetProperty("in").GetString());
        var version = item.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() == "version");
        Assert.False(
            version.TryGetProperty("required", out var required) &&
            required.GetBoolean());

        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.PreviewSpaceExcelMappingRequest",
            preview.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.SaveSpaceExcelMappingProfileRequest"),
            "name",
            "definition");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.PreviewSpaceExcelMappingRequest"),
            "definition",
            "workbook");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.SpaceExcelMappingDefinitionDto"),
            "schemaVersion",
            "unknownColumnPolicy",
            "emptyValuePolicy",
            "duplicateRowPolicy",
            "sheets");
        var headerSample = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceExcelHeaderSampleDto");
        AssertExactRequired(headerSample, "sheetName", "headers");
        Assert.DoesNotContain(
            "cell",
            headerSample.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "file",
            headerSample.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Excel_preflight_contract_freezes_upload_job_result_and_report()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var upload = paths.GetProperty(
                "/api/space/design/v1/versions/{versionId}/excel-sources")
            .GetProperty("post");
        var start = paths.GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights")
            .GetProperty("post");
        var report = paths.GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights/{jobId}/report")
            .GetProperty("get");

        var multipart = upload.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");
        AssertExactRequired(multipart, "File");
        var idempotency = start.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.Equal("header", idempotency.GetProperty("in").GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.StartSpaceExcelPreflightRequest",
            start.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        var reportSchema = report.GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("text/csv")
            .GetProperty("schema");
        Assert.Equal("string", reportSchema.GetProperty("type").GetString());
        Assert.Equal("binary", reportSchema.GetProperty("format").GetString());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.StartSpaceExcelPreflightRequest"),
            "mappingProfileId",
            "mappingProfileVersion");
        AssertExactRequired(
            Schema(schemas,
                "CP6.Space.Contracts.StartSpaceExcelPreflightResponse"),
            "jobId",
            "jobStatus",
            "jobStatusUrl",
            "previewUrl",
            "errorReportUrl",
            "mappingProfileId",
            "mappingProfileVersion",
            "mappingDefinitionHash",
            "source",
            "idempotentReplay");
        var issue = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceExcelPreflightIssueDto");
        AssertExactRequired(
            issue,
            "id",
            "severity",
            "code",
            "messageArgsJson",
            "createdAtUtc");
        Assert.DoesNotContain(
            "cellValue",
            issue.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_organization_contract_exposes_typed_management_surface()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var organizations = paths.GetProperty(
            "/api/space/external-organization");
        var membership = paths.GetProperty(
            "/api/space/external-organization/{organizationId}/membership");
        var grant = paths.GetProperty(
            "/api/space/external-organization/{organizationId}/grant");

        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.CreateSpaceExternalOrganizationRequest",
            organizations.GetProperty("post")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.CreateSpaceExternalMembershipRequest",
            membership.GetProperty("post")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.CreateSpaceExternalGrantRequest",
            grant.GetProperty("post")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(
            "CP6.Space.Contracts.SpaceExternalOrganizationDto",
            out _));
        Assert.True(schemas.TryGetProperty(
            "CP6.Space.Contracts.SpaceExternalMembershipDto",
            out _));
        Assert.True(schemas.TryGetProperty(
            "CP6.Space.Contracts.SpaceExternalGrantDto",
            out _));
        Assert.True(schemas.TryGetProperty(
            "CP6.Space.Contracts.SpaceExternalGrantObjectDto",
            out _));
    }

    [Fact]
    public void Field_policy_and_portal_contracts_freeze_allowlist_boundaries()
    {
        using var document = ReadContract();
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var create = Schema(
            schemas,
            "CP6.Space.Contracts.CreateSpaceFieldPolicyRequest");
        var update = Schema(
            schemas,
            "CP6.Space.Contracts.UpdateSpaceFieldPolicyRequest");
        var field = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceFieldPolicyFieldRequest");
        AssertExactRequired(create, "name", "audienceType", "fields");
        AssertExactRequired(
            update,
            "name",
            "fields",
            "canExport",
            "status");
        AssertExactRequired(field, "resourceType", "fieldName");

        var scene = Schema(
            schemas,
            "CP6.Space.Contracts.SpacePortalPublishedSceneDto");
        var floor = Schema(
            schemas,
            "CP6.Space.Contracts.SpacePortalFloorDto");
        var stock = Schema(
            schemas,
            "CP6.Space.Contracts.SpacePortalStockItemDto");
        var task = Schema(
            schemas,
            "CP6.Space.Contracts.SpacePortalTaskItemDto");
        AssertExactRequired(
            scene,
            "siteId",
            "publishedVersionId",
            "authorizationVersion",
            "floors");
        AssertExactRequired(
            floor,
            "logicalId",
            "zones",
            "aisles",
            "racks",
            "rackLevels",
            "locations",
            "elements");
        AssertNullable(floor, "level", "code", "name", "boundaryJson");
        AssertExactRequired(stock, "locationLogicalId", "floorLogicalId");
        AssertNullable(stock, "materialNumber", "lotNumber", "ownerId");
        AssertNumberFormat(stock, "physicalQuantity", "decimal", true);
        AssertExactRequiredNames(
            task,
            "locationLogicalId",
            "floorLogicalId",
            "zoneLogicalId");
        AssertNonNullable(task, "locationLogicalId", "floorLogicalId");
        AssertNullable(task, "zoneLogicalId", "taskId", "materialNumber");
        AssertNumberFormat(task, "quantity", "decimal", true);

        foreach (var forbidden in new[]
                 {
                     "revisionId",
                     "sourceId",
                     "sourceRef",
                     "rowVersion",
                     "contentHash",
                     "elementAttributes",
                     "underlaySourceId",
                 })
        {
            Assert.DoesNotContain(
                forbidden,
                scene.GetRawText() + floor.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Runtime_inventory_and_task_contracts_expose_source_and_dual_identity()
    {
        using var document = ReadContract();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        Assert.True(paths.TryGetProperty(
            "/api/space/design/v1/sites/{siteId}/runtime/inventory",
            out _));
        Assert.True(paths.TryGetProperty(
            "/api/space/design/v1/sites/{siteId}/runtime/inventory/locate",
            out _));
        Assert.True(paths.TryGetProperty(
            "/api/space/design/v1/sites/{siteId}/runtime/overview",
            out var overviewPath));
        Assert.True(paths.TryGetProperty(
            "/api/space/design/v1/sites/{siteId}/runtime/tasks",
            out _));
        Assert.True(paths.TryGetProperty(
            "/api/space/design/v1/sites/{siteId}/runtime/tasks/path",
            out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var sourceProperties = schemas
            .GetProperty("CP6.Space.Contracts.SpaceWmsRuntimeSourceDto")
            .GetProperty("properties");
        Assert.True(sourceProperties.TryGetProperty("kind", out _));
        Assert.True(sourceProperties.TryGetProperty("adapterId", out _));
        Assert.True(sourceProperties.TryGetProperty("observedAtUtc", out _));
        Assert.True(sourceProperties.TryGetProperty("receivedAtUtc", out _));
        Assert.True(sourceProperties.TryGetProperty("delayMilliseconds", out _));
        Assert.True(sourceProperties.TryGetProperty("clockSkewMilliseconds", out _));
        Assert.True(sourceProperties.TryGetProperty("isAvailable", out _));

        var inventoryProperties = schemas
            .GetProperty("CP6.Space.Contracts.SpaceWmsRuntimeInventoryItemDto")
            .GetProperty("properties");
        Assert.True(inventoryProperties.TryGetProperty(
            "locationLogicalId",
            out _));
        Assert.True(inventoryProperties.TryGetProperty(
            "wmsLogicalId",
            out _));
        Assert.True(inventoryProperties.TryGetProperty("codeMatches", out _));

        var overview = overviewPath.GetProperty("get");
        Assert.Equal(
            "GetWarehouseOverview",
            overview.GetProperty("operationId").GetString());
        var windowParameter = overview.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() == "abcWindowDays");
        Assert.False(
            windowParameter.TryGetProperty("required", out var required) &&
            required.GetBoolean());
        Assert.Equal(
            90,
            windowParameter.GetProperty("schema")
                .GetProperty("default")
                .GetInt32());

        var overviewSchema = schemas.GetProperty(
            "CP6.Space.Contracts.SpaceWmsRuntimeWarehouseOverviewResponse");
        AssertExactRequired(
            overviewSchema,
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
            "floors");
        var inventoryKpi = schemas.GetProperty(
            "CP6.Space.Contracts.SpaceWmsRuntimeWarehouseInventoryKpiDto");
        AssertNullable(
            inventoryKpi,
            "occupiedLocationRatePercent",
            "capacityUtilizationPercent");
    }

    [Fact]
    public void Runtime_contracts_freeze_required_nullability_and_decimals()
    {
        using var document = ReadContract();
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var inventoryResponse = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeInventoryResponse");
        var taskResponse = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskResponse");
        var locateResponse = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeInventoryLocateResponse");
        var locateCriteria = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeInventoryLocateCriteriaDto");
        var locateHit = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeInventoryLocateHitDto");
        var source = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeSourceDto");
        var inventoryItem = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeInventoryItemDto");
        var taskItem = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskItemDto");
        var taskPath = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskPathResponse");
        var taskFloor = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskFloorDto");
        var taskWorkload = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskWorkloadDto");
        var taskAisle = Schema(
            schemas,
            "CP6.Space.Contracts.SpaceWmsRuntimeTaskAisleDto");

        AssertExactRequired(
            inventoryResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "items");
        AssertExactRequired(
            taskResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "items");
        AssertExactRequired(
            locateResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "criteria",
            "locationCount",
            "floorCount",
            "items");
        AssertExactRequiredNames(
            locateCriteria,
            "materialNumber",
            "lotNumber",
            "containerNumber",
            "ownerId");
        AssertExactRequired(
            locateHit,
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
            "ownerIds");
        AssertExactRequired(
            source,
            "kind",
            "adapterId",
            "dataSourceId",
            "observedAtUtc",
            "receivedAtUtc",
            "delayMilliseconds",
            "clockSkewMilliseconds",
            "isSimulated",
            "isAvailable");
        AssertExactRequired(
            inventoryItem,
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
            "allocatedQuantity");
        AssertExactRequired(
            taskItem,
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
            "floorLevel");
        AssertExactRequired(
            taskPath,
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
            "aisles");
        AssertExactRequired(
            taskFloor,
            "floorLogicalId",
            "floorCode",
            "floorName",
            "floorLevel",
            "elevationMillimeters",
            "heightMillimeters",
            "stopCount",
            "totalQuantity");
        AssertExactRequired(
            taskWorkload,
            "floorLogicalId",
            "floorCode",
            "stopCount",
            "totalQuantity");
        AssertExactRequired(
            taskAisle,
            "floorLogicalId",
            "zoneLogicalId",
            "aisleLogicalId",
            "aisleCode",
            "centerlineJson");

        AssertNonNullable(
            inventoryResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "items");
        AssertNonNullable(
            locateResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "criteria",
            "locationCount",
            "floorCount",
            "items");
        AssertNonNullable(
            source,
            "kind",
            "dataSourceId",
            "observedAtUtc",
            "isSimulated",
            "isAvailable");
        AssertNonNullable(
            inventoryItem,
            "spaceLocationCode",
            "wmsLocationCode",
            "floorCode",
            "floorName",
            "physicalQuantity",
            "allocatedQuantity");
        AssertNonNullable(
            locateHit,
            "spaceLocationCode",
            "wmsLocationCode",
            "floorCode",
            "floorName",
            "physicalQuantity",
            "allocatedQuantity",
            "materialNumbers",
            "lotNumbers",
            "containerNumbers",
            "ownerIds");
        AssertNonNullable(
            taskItem,
            "taskId",
            "taskType",
            "status",
            "spaceLocationCode",
            "wmsLocationCode",
            "floorCode",
            "floorName");
        AssertNullable(inventoryItem, "materialNumber", "ownerId");
        AssertNullable(
            locateCriteria,
            "materialNumber",
            "lotNumber",
            "containerNumber",
            "ownerId");
        AssertNullable(taskItem, "zoneLogicalId", "quantity", "materialNumber");
        AssertNullable(taskWorkload, "zoneLogicalId", "zoneCode");

        AssertNumberFormat(inventoryItem, "physicalQuantity", "decimal", false);
        AssertNumberFormat(inventoryItem, "allocatedQuantity", "decimal", false);
        AssertNumberFormat(locateHit, "physicalQuantity", "decimal", false);
        AssertNumberFormat(locateHit, "allocatedQuantity", "decimal", false);
        AssertNumberFormat(taskItem, "quantity", "decimal", true);
        AssertNumberFormat(taskPath, "totalQuantity", "decimal", false);
        AssertNumberFormat(taskFloor, "totalQuantity", "decimal", false);
        AssertNumberFormat(taskWorkload, "totalQuantity", "decimal", false);
    }

    [Fact]
    public void Runtime_controller_preserves_its_mvc_identity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers()
            .AddApplicationPart(typeof(SpaceWmsRuntimeController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(action =>
                action.ControllerTypeInfo.AsType() ==
                typeof(SpaceWmsRuntimeController))
            .OrderBy(action => action.ActionName)
            .ToArray();

        Assert.Equal(
            [
                "GetInventory",
                "GetTaskPath",
                "GetTasks",
                "GetWarehouseOverview",
                "LocateInventory",
            ],
            actions.Select(action => action.ActionName));
        Assert.All(
            actions,
            action => Assert.Equal(
                "SpaceWmsRuntime",
                action.ControllerName));
    }

    [Theory]
    [InlineData(
        "/api/space/design/v1/versions/{versionId}/wms-adoption/locations/{adoptionId}/bind",
        "CP6.Space.Contracts.BindSpaceWmsAdoptionRequest")]
    [InlineData(
        "/api/space/design/v1/versions/{versionId}/wms-adoption/bindings:batch",
        "CP6.Space.Contracts.BatchBindSpaceWmsAdoptionRequest")]
    [InlineData(
        "/api/space/design/v1/versions/{versionId}/wms-adoption/locations/{adoptionId}/place",
        "CP6.Space.Contracts.PlaceSpaceWmsAdoptionRequest")]
    public void Wms_adoption_writes_use_stable_body_and_response_contracts(
        string path,
        string requestSchema)
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post");

        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        Assert.Equal(
            $"#/components/schemas/{requestSchema}",
            operation.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.SpaceWmsAdoptionCommandResponse",
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
    }

    [Fact]
    public void Wms_refresh_exposes_gateway_and_unavailable_problem_details()
    {
        using var document = ReadContract();
        var responses = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/wms-adoption/refresh")
            .GetProperty("post")
            .GetProperty("responses");

        foreach (var status in new[] { "502", "503" })
        {
            Assert.Equal(
                "#/components/schemas/CP6.WebApi.OpenApi.SpaceDesignProblemDetails",
                responses.GetProperty(status)
                    .GetProperty("content")
                    .GetProperty("application/problem+json")
                    .GetProperty("schema")
                    .GetProperty("$ref")
                    .GetString());
        }
    }

    [Fact]
    public void Element_commands_use_a_required_batch_body_and_stable_response()
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands")
            .GetProperty("post");

        Assert.Equal(
            "ApplyElementCommands",
            operation.GetProperty("operationId").GetString());
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.ApplySpaceElementCommandBatchRequest",
            operation.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.ApplySpaceElementCommandBatchResponse",
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
    }

    [Fact]
    public void Underlay_upload_and_content_use_bounded_binary_contracts()
    {
        using var document = ReadContract();
        var paths = document.RootElement.GetProperty("paths");
        var upload = paths
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/underlay-sources")
            .GetProperty("post");
        var multipart = upload.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");
        var required = multipart.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet();
        Assert.True(required.SetEquals(["File", "SourceType"]));
        var file = multipart.GetProperty("properties").GetProperty("File");
        Assert.Equal("string", file.GetProperty("type").GetString());
        Assert.Equal("binary", file.GetProperty("format").GetString());

        var content = paths
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/content")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");
        Assert.True(content.TryGetProperty("application/pdf", out _));
        Assert.True(content.TryGetProperty("image/png", out _));
        Assert.True(content.TryGetProperty("image/jpeg", out _));
        foreach (var mediaType in content.EnumerateObject())
        {
            var schema = mediaType.Value.GetProperty("schema");
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.Equal("binary", schema.GetProperty("format").GetString());
        }
    }

    [Fact]
    public void Underlay_attach_requires_body_idempotency_and_replay_header()
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/underlay")
            .GetProperty("put");

        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Fact]
    public void Underlay_calibration_requires_floor_body_idempotency_and_replay_header()
    {
        using var document = ReadContract();
        var path = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration");

        var floorLogicalId = path.GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "floorLogicalId");
        Assert.Equal("query", floorLogicalId.GetProperty("in").GetString());
        Assert.True(floorLogicalId.GetProperty("required").GetBoolean());

        var operation = path.GetProperty("post");
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Theory]
    [InlineData(
        "/api/space/design/v1/sites/{siteId}/versions",
        "202")]
    [InlineData(
        "/api/space/design/v1/versions/{versionId}/sources",
        "201")]
    [InlineData(
        "/api/space/design/v1/assets",
        "201")]
    public void Write_operations_require_body_idempotency_and_replay_header(
        string path,
        string successStatus)
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post");

        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty(successStatus)
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Fact]
    public void Error_contract_requires_stable_problem_details_extensions()
    {
        using var document = ReadContract();
        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("CP6.WebApi.OpenApi.SpaceDesignProblemDetails");
        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet();

        Assert.Contains("code", required);
        Assert.Contains("traceId", required);
        Assert.Contains("correlationId", required);
        Assert.Contains("recovery", required);

        foreach (var path in document.RootElement
                     .GetProperty("paths")
                     .EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject()
                         .Where(IsOperation))
            {
                foreach (var status in new[]
                         {
                             "400", "401", "403", "404", "409", "422", "500",
                         })
                {
                    Assert.True(
                        operation.Value.GetProperty("responses")
                            .GetProperty(status)
                            .GetProperty("content")
                            .TryGetProperty("application/problem+json", out _),
                        $"{operation.Name.ToUpperInvariant()} {path.Name} " +
                        $"does not expose Problem Details for {status}.");
                }
            }
        }
    }

    [Fact]
    public void Generated_clients_use_stable_operation_names()
    {
        var csharp = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "CP6.Space.Client",
                "SpaceDesignV1Client.g.cs"));
        var typescript = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "sdk",
                "typescript",
                "space-design-v1",
                "spaceDesignV1Client.ts"));

        foreach (var operation in new[]
                 {
                     "GetModel",
                     "GetVersions",
                     "CreateVersion",
                     "GetVersion",
                     "GetScene",
                     "ApplyElementCommands",
                     "GetAssets",
                     "DownloadStandardExcelTemplate",
                     "CreateAsset",
                     "GetSources",
                     "CreateSource",
                     "UploadExcelSource",
                     "StartPreflight",
                     "GetPreflight",
                     "DownloadErrorReport",
                     "UploadUnderlay",
                     "GetFile",
                     "GetUnderlayContent",
                     "AttachUnderlay",
                     "GetUnderlayCalibration",
                     "CalibrateUnderlay",
                     "GetJob",
                     "GetIssues",
                     "RefreshWmsAdoption",
                     "GetWmsAdoptionLocations",
                     "BindWmsAdoption",
                     "BindWmsAdoptionBatch",
                     "PlaceWmsAdoption",
                     "GetInventory",
                     "LocateInventory",
                     "GetWarehouseOverview",
                     "GetTasks",
                     "IngestPersonnelEvents",
                     "GetCurrentPersonnel",
                     "GetPersonnelTrajectory",
                     "GetOrganizations",
                     "GetOrganization",
                     "CreateOrganization",
                     "UpdateOrganization",
                     "GetMemberships",
                     "CreateMembership",
                     "UpdateMembership",
                     "GetGrants",
                     "GetGrant",
                     "CreateGrant",
                     "UpdateGrant",
                     "GetFieldPolicies",
                     "GetFieldPolicy",
                     "CreateFieldPolicy",
                     "UpdateFieldPolicy",
                     "GetPortalOrganizations",
                     "GetPortalSites",
                     "GetPortalPublishedScene",
                     "GetPortalStock",
                     "GetPortalTasks",
                     "CreateHistoricalDataset",
                     "GetHistoricalDataset",
                     "GetHistoricalDatasets",
                     "CreateSimulationRun",
                     "GetSimulationRun",
                     "GetSimulationRuns",
                     "CreateComparison",
                     "GetComparison",
                     "GetComparisons",
                     "CreateDecision",
                     "GetDecision",
                     "GetDecisions",
                  })
        {
            Assert.Contains(operation, csharp, StringComparison.Ordinal);
            Assert.Contains(operation, typescript, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("GET2", csharp, StringComparison.Ordinal);
        Assert.DoesNotContain("Dto2", csharp, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_runtime_clients_preserve_guarantees_and_decimals()
    {
        var csharp = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "CP6.Space.Client",
                "SpaceDesignV1Client.g.cs"));
        var typescript = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "sdk",
                "typescript",
                "space-design-v1",
                "spaceDesignV1Client.ts"));

        var csharpInventory = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeInventoryItemDto");
        var csharpTask = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeTaskItemDto");
        var csharpLocateHit = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeInventoryLocateHitDto");
        var csharpLocateCriteria = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeInventoryLocateCriteriaDto");
        var csharpSource = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeSourceDto");
        var csharpWarehouseInventory = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeWarehouseInventoryKpiDto");
        var csharpWarehouseModel = ExtractTypeBlock(
            csharp,
            "public partial class SpaceWmsRuntimeWarehouseModelKpiDto");
        Assert.Contains(
            "public string AdapterId { get; set; }",
            csharpSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public System.DateTimeOffset ReceivedAtUtc { get; set; }",
            csharpSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public long DelayMilliseconds { get; set; }",
            csharpSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public long ClockSkewMilliseconds { get; set; }",
            csharpSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal PhysicalQuantity { get; set; }",
            csharpInventory,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal AllocatedQuantity { get; set; }",
            csharpInventory,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal PhysicalQuantity { get; set; }",
            csharpLocateHit,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal? Quantity { get; set; }",
            csharpTask,
            StringComparison.Ordinal);
        Assert.Contains(
            "public string? MaterialNumber { get; set; }",
            csharpInventory,
            StringComparison.Ordinal);
        Assert.Contains(
            "public string? OwnerId { get; set; }",
            csharpLocateCriteria,
            StringComparison.Ordinal);
        Assert.Contains(
            "public string? ZoneCode { get; set; }",
            csharpTask,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal? CapacityUtilizationPercent { get; set; }",
            csharpWarehouseInventory,
            StringComparison.Ordinal);
        Assert.Contains(
            "public decimal RackFootprintSquareMeters { get; set; }",
            csharpWarehouseModel,
            StringComparison.Ordinal);

        var inventoryResponse = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeInventoryResponse");
        AssertRequiredTypeScriptProperties(
            inventoryResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "items");

        var source = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeSourceDto");
        AssertRequiredTypeScriptProperties(
            source,
            "kind",
            "adapterId",
            "dataSourceId",
            "observedAtUtc",
            "receivedAtUtc",
            "delayMilliseconds",
            "clockSkewMilliseconds",
            "isSimulated",
            "isAvailable");

        var locateResponse = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeInventoryLocateResponse");
        AssertRequiredTypeScriptProperties(
            locateResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "criteria",
            "locationCount",
            "floorCount",
            "items");

        var locateHit = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeInventoryLocateHitDto");
        AssertRequiredTypeScriptProperties(
            locateHit,
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
            "ownerIds");

        var locateCriteriaTypeScript = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeInventoryLocateCriteriaDto");
        Assert.Contains(
            "ownerId: string | null | undefined;",
            locateCriteriaTypeScript,
            StringComparison.Ordinal);

        var taskResponse = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeTaskResponse");
        AssertRequiredTypeScriptProperties(
            taskResponse,
            "siteId",
            "publishedVersionId",
            "warehouseCode",
            "source",
            "items");

        var inventoryItem = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeInventoryItemDto");
        AssertRequiredTypeScriptProperties(
            inventoryItem,
            "locationLogicalId",
            "wmsLogicalId",
            "spaceLocationCode",
            "wmsLocationCode",
            "codeMatches");

        var taskItem = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeTaskItemDto");
        AssertRequiredTypeScriptProperties(
            taskItem,
            "taskId",
            "taskType",
            "status",
            "sequenceNo",
            "locationLogicalId",
            "wmsLogicalId",
            "spaceLocationCode",
            "wmsLocationCode",
            "codeMatches");
        Assert.Contains(
            "materialNumber?: string | null | undefined;",
            inventoryItem,
            StringComparison.Ordinal);
        Assert.Contains(
            "zoneCode?: string | null | undefined;",
            taskItem,
            StringComparison.Ordinal);

        var warehouseOverview = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeWarehouseOverviewResponse");
        AssertRequiredTypeScriptProperties(
            warehouseOverview,
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
            "floors");
        var warehouseInventory = ExtractTypeBlock(
            typescript,
            "export interface ISpaceWmsRuntimeWarehouseInventoryKpiDto");
        Assert.Contains(
            "capacityUtilizationPercent: number | null | undefined;",
            warehouseInventory,
            StringComparison.Ordinal);
        Assert.Contains(
            "occupiedLocationRatePercent: number | null | undefined;",
            warehouseInventory,
            StringComparison.Ordinal);
    }

    private static JsonElement Schema(JsonElement schemas, string name) =>
        schemas.GetProperty(name);

    private static void AssertExactRequired(
        JsonElement schema,
        params string[] expected)
    {
        AssertExactRequiredNames(schema, expected);
        AssertNonNullable(schema, expected);
    }

    private static void AssertExactRequiredNames(
        JsonElement schema,
        params string[] expected)
    {
        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.True(required
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet()
            .SetEquals(expected));
    }

    private static void AssertNonNullable(
        JsonElement schema,
        params string[] propertyNames)
    {
        var properties = schema.GetProperty("properties");
        foreach (var propertyName in propertyNames)
        {
            var property = properties.GetProperty(propertyName);
            Assert.False(
                property.TryGetProperty("nullable", out var nullable) &&
                nullable.GetBoolean(),
                $"{propertyName} must be non-nullable.");
        }
    }

    private static void AssertNullable(
        JsonElement schema,
        params string[] propertyNames)
    {
        var properties = schema.GetProperty("properties");
        foreach (var propertyName in propertyNames)
        {
            Assert.True(
                properties.GetProperty(propertyName)
                    .GetProperty("nullable")
                    .GetBoolean(),
                $"{propertyName} must remain nullable.");
        }
    }

    private static void AssertNumberFormat(
        JsonElement schema,
        string propertyName,
        string format,
        bool nullable)
    {
        var property = schema.GetProperty("properties")
            .GetProperty(propertyName);
        Assert.Equal("number", property.GetProperty("type").GetString());
        Assert.Equal(format, property.GetProperty("format").GetString());
        Assert.Equal(
            nullable,
            property.TryGetProperty("nullable", out var nullableProperty) &&
            nullableProperty.GetBoolean());
    }

    private static string ExtractTypeBlock(string text, string declaration)
    {
        var start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing generated type: {declaration}");
        var openingBrace = text.IndexOf('{', start);
        Assert.True(openingBrace >= 0, $"Missing type body: {declaration}");
        var depth = 0;
        for (var index = openingBrace; index < text.Length; index++)
        {
            if (text[index] == '{')
                depth++;
            else if (text[index] == '}' && --depth == 0)
                return text[start..(index + 1)];
        }

        throw new Xunit.Sdk.XunitException(
            $"Unterminated generated type: {declaration}");
    }

    private static void AssertRequiredTypeScriptProperties(
        string typeBlock,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            Assert.Contains(
                $"{propertyName}:",
                typeBlock,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"{propertyName}?:",
                typeBlock,
                StringComparison.Ordinal);
        }
    }

    private static JsonDocument ReadContract()
    {
        Assert.True(File.Exists(OpenApiPath), OpenApiPath);
        return JsonDocument.Parse(File.ReadAllText(OpenApiPath));
    }

    private static bool IsOperation(JsonProperty property) =>
        property.Name is "get" or "post" or "put" or "patch" or "delete";
}
