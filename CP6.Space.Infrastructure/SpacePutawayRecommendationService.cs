using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePutawayRecommendationService(
    SpaceContext context,
    ISpaceWmsRuntimeService runtime,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access,
    SpacePutawayRecommendationEngine engine)
    : ISpacePutawayRecommendationService
{
    public const string DefinitionVersion = "space-putaway-v1";
    public const int MaximumCandidateCount = 50;
    public const int MaximumEvidenceLocationCount = 10_000;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task<GenerateSpacePutawayRecommendationResponse>
        GenerateAsync(
            Guid siteId,
            Guid recommendationId,
            GenerateSpacePutawayRecommendationRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        ArgumentNullException.ThrowIfNull(request);
        var normalized = Normalize(request);
        var requestHash = RequestHash(siteId, normalized);
        access.EnsureSiteAccess(siteId, write: false);

        var existing = await context.PutawayRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == recommendationId,
                cancellationToken);
        if (existing is not null)
            return Duplicate(existing, siteId, requestHash);

        var inventory = await runtime.QueryInventoryAsync(
            siteId,
            locationLogicalIds: null,
            cancellationToken);
        var tasks = await runtime.QueryTasksAsync(
            siteId,
            locationLogicalIds: null,
            cancellationToken);
        EnsureAvailable(inventory.Source);
        EnsureAvailable(tasks.Source);
        EnsureSameRuntimeContext(inventory, tasks, siteId);

        var floorRows = await context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == inventory.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new FloorRow(
                value.LogicalId,
                value.FloorCode,
                value.Name,
                value.Level))
            .ToArrayAsync(cancellationToken);
        var zoneRows = await context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == inventory.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new ZoneRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneCode))
            .ToArrayAsync(cancellationToken);
        var rackRows = await context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == inventory.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new RackRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneLogicalId,
                value.RackCode,
                value.X,
                value.Y,
                value.RotationZ))
            .ToArrayAsync(cancellationToken);
        var locationRows = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == inventory.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => new LocationRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.RackLogicalId,
                value.LocationCode,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo,
                value.Width,
                value.Height,
                value.Depth,
                value.MaxLoad))
            .Take(MaximumEvidenceLocationCount + 1)
            .ToArrayAsync(cancellationToken);
        if (locationRows.Length > MaximumEvidenceLocationCount)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PutawayRecommendationEvidenceLimit,
                422,
                "The putaway recommendation evidence limit was exceeded.",
                $"The current Published model has more than {MaximumEvidenceLocationCount} active locations.",
                "narrow-putaway-scope-or-reduce-model");
        }

        ValidateRequestedScope(normalized, floorRows, zoneRows);
        var locations = BuildLocations(
            floorRows,
            zoneRows,
            rackRows,
            locationRows);
        var knownLocations = locations
            .Select(value => value.LocationLogicalId)
            .ToHashSet();
        if (inventory.Items.Any(value =>
                !knownLocations.Contains(value.LocationLogicalId)) ||
            tasks.Items.Any(value =>
                !knownLocations.Contains(value.LocationLogicalId)))
        {
            throw ContractViolation(
                "The WMS runtime referenced a location outside the active Published model.");
        }

        var inventoryInputs = inventory.Items
            .Select(value => new SpacePutawayInventoryInput(
                value.LocationLogicalId,
                value.PhysicalQuantity,
                value.AllocatedQuantity,
                NormalizeSourceIdentifier(
                    value.MaterialNumber,
                    200,
                    "material"),
                NormalizeSourceIdentifier(value.OwnerId, 100, "owner"),
                NormalizeSourceIdentifier(value.LotNumber, 200, "lot"),
                value.CodeMatches))
            .ToArray();
        var candidateSet = engine.Generate(
            normalized,
            locations,
            inventoryInputs,
            tasks.Items
                .Select(value => value.LocationLogicalId)
                .ToHashSet());
        ValidateCandidateSet(candidateSet);

        var generatedAt = clock.UtcNow;
        if (generatedAt.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        var sources = new SpacePutawayRecommendationSourcesDto(
            inventory.Source,
            tasks.Source);
        var limitations = Limitations(
            normalized,
            sources,
            locations.Any(value =>
                !value.AnchorXMillimeters.HasValue ||
                !value.AnchorYMillimeters.HasValue));
        var outcome = candidateSet.Candidates.Count == 0
            ? "NoCandidate"
            : "CandidatesGenerated";
        var entity = SpacePutawayRecommendation.Create(
            execution.TenantId,
            recommendationId,
            new SpacePutawayRecommendationData(
                siteId,
                inventory.PublishedVersionId,
                inventory.WarehouseCode,
                generatedAt,
                execution.ActorId,
                DefinitionVersion,
                outcome,
                candidateSet.ExaminedLocationCount,
                candidateSet.EligibleCandidateCount,
                candidateSet.Candidates.Count,
                candidateSet.IsTruncated,
                candidateSet.ExclusionSamplesTruncated,
                JsonSerializer.Serialize(normalized, Json),
                JsonSerializer.Serialize(sources, Json),
                JsonSerializer.Serialize(candidateSet.Exclusions, Json),
                JsonSerializer.Serialize(candidateSet.ExclusionSamples, Json),
                JsonSerializer.Serialize(candidateSet.Candidates, Json),
                JsonSerializer.Serialize(limitations, Json),
                requestHash));
        context.PutawayRecommendations.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.Entry(entity).State = EntityState.Detached;
            var concurrent = await context.PutawayRecommendations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == recommendationId,
                    cancellationToken);
            if (concurrent is null)
                throw;
            return Duplicate(concurrent, siteId, requestHash);
        }

        return new GenerateSpacePutawayRecommendationResponse(
            "Generated",
            Map(entity));
    }

    public async Task<SpacePutawayRecommendationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        access.EnsureSiteAccess(siteId, write: false);
        var value = await context.PutawayRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == recommendationId &&
                    item.SiteId == siteId,
                cancellationToken)
            ?? throw NotFound();
        return Map(value);
    }

    private GenerateSpacePutawayRecommendationResponse Duplicate(
        SpacePutawayRecommendation existing,
        Guid siteId,
        string requestHash)
    {
        if (existing.SiteId != siteId ||
            !string.Equals(
                existing.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PutawayRecommendationConflict,
                409,
                "The putaway recommendation identity is already in use.",
                recoveryAction: "use-new-recommendation-id");
        }
        return new GenerateSpacePutawayRecommendationResponse(
            "Duplicate",
            Map(existing));
    }

    private static GenerateSpacePutawayRecommendationRequest Normalize(
        GenerateSpacePutawayRecommendationRequest request)
    {
        var material = RequiredIdentifier(
            request.MaterialNumber,
            200,
            "materialNumber");
        var owner = OptionalIdentifier(request.OwnerId, 100, "ownerId");
        var lot = OptionalIdentifier(request.LotNumber, 200, "lotNumber");
        if (request.InboundQuantity <= 0)
        {
            throw Invalid(
                "inboundQuantity",
                "Inbound quantity must be positive.");
        }
        if (request.MaximumCandidates is < 1 or > MaximumCandidateCount)
        {
            throw Invalid(
                "maximumCandidates",
                $"Maximum candidates must be between 1 and {MaximumCandidateCount}.");
        }
        if (request.FloorLogicalId == Guid.Empty)
            throw Invalid("floorLogicalId", "Identity cannot be empty.");
        if (request.ZoneLogicalId == Guid.Empty)
            throw Invalid("zoneLogicalId", "Identity cannot be empty.");
        ValidatePositive(
            request.RequiredWidthMillimeters,
            "requiredWidthMillimeters");
        ValidatePositive(
            request.RequiredHeightMillimeters,
            "requiredHeightMillimeters");
        ValidatePositive(
            request.RequiredDepthMillimeters,
            "requiredDepthMillimeters");
        if (request.RequiredMaxLoad <= 0)
        {
            throw Invalid(
                "requiredMaxLoad",
                "Required maximum load must be positive.");
        }
        return request with
        {
            MaterialNumber = material,
            OwnerId = owner,
            LotNumber = lot,
        };
    }

    private static void ValidatePositive(int? value, string field)
    {
        if (value <= 0)
            throw Invalid(field, "The value must be positive.");
    }

    private static void ValidateRequestedScope(
        GenerateSpacePutawayRecommendationRequest request,
        IReadOnlyCollection<FloorRow> floors,
        IReadOnlyCollection<ZoneRow> zones)
    {
        if (request.FloorLogicalId.HasValue &&
            floors.All(value =>
                value.LogicalId != request.FloorLogicalId))
        {
            throw Invalid(
                "floorLogicalId",
                "The requested floor is not active in the Published version.");
        }
        if (!request.ZoneLogicalId.HasValue)
            return;
        var zone = zones.SingleOrDefault(value =>
            value.LogicalId == request.ZoneLogicalId)
            ?? throw Invalid(
                "zoneLogicalId",
                "The requested zone is not active in the Published version.");
        if (request.FloorLogicalId.HasValue &&
            zone.FloorLogicalId != request.FloorLogicalId)
        {
            throw Invalid(
                "zoneLogicalId",
                "The requested zone is outside the requested floor.");
        }
    }

    private static SpacePutawayLocationInput[] BuildLocations(
        IReadOnlyCollection<FloorRow> floors,
        IReadOnlyCollection<ZoneRow> zones,
        IReadOnlyCollection<RackRow> racks,
        IReadOnlyCollection<LocationRow> locations)
    {
        var floorById = floors.ToDictionary(value => value.LogicalId);
        var zoneById = zones.ToDictionary(value => value.LogicalId);
        var rackById = racks.ToDictionary(value => value.LogicalId);
        var result = new List<SpacePutawayLocationInput>(locations.Count);
        foreach (var value in locations)
        {
            if (!floorById.TryGetValue(value.FloorLogicalId, out var floor))
            {
                throw ContractViolation(
                    "An active location references a missing active floor.");
            }
            RackRow? rack = null;
            ZoneRow? zone = null;
            if (value.RackLogicalId.HasValue)
            {
                if (!rackById.TryGetValue(value.RackLogicalId.Value, out rack))
                {
                    throw ContractViolation(
                        "An active location references a missing active rack.");
                }
                if (rack.FloorLogicalId != value.FloorLogicalId ||
                    !zoneById.TryGetValue(rack.ZoneLogicalId, out zone) ||
                    zone.FloorLogicalId != value.FloorLogicalId)
                {
                    throw ContractViolation(
                        "An active location has inconsistent rack, zone, or floor metadata.");
                }
            }
            var anchor = Anchor(value, rack);
            result.Add(new SpacePutawayLocationInput(
                value.LogicalId,
                value.LocationCode,
                floor.LogicalId,
                floor.FloorCode,
                floor.FloorName,
                floor.FloorLevel,
                zone?.LogicalId,
                zone?.ZoneCode,
                rack?.LogicalId,
                rack?.RackCode,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo,
                value.Width,
                value.Height,
                value.Depth,
                value.MaxLoad,
                anchor?.X,
                anchor?.Y));
        }
        return result.ToArray();
    }

    private static Point? Anchor(LocationRow location, RackRow? rack)
    {
        if (rack is null)
            return null;
        var angle = (double)rack.RotationZ * Math.PI / 180d;
        var localX = (location.ColumnNo - 0.5m) * location.Width;
        var localY = (location.DepthNo - 0.5m) * location.Depth;
        var x = rack.X +
                localX * (decimal)Math.Cos(angle) -
                localY * (decimal)Math.Sin(angle);
        var y = rack.Y +
                localX * (decimal)Math.Sin(angle) +
                localY * (decimal)Math.Cos(angle);
        return new Point(x, y);
    }

    private static void EnsureSameRuntimeContext(
        SpaceWmsRuntimeInventoryResponse inventory,
        SpaceWmsRuntimeTaskResponse tasks,
        Guid siteId)
    {
        if (inventory.SiteId != siteId ||
            tasks.SiteId != siteId ||
            inventory.PublishedVersionId != tasks.PublishedVersionId ||
            !string.Equals(
                inventory.WarehouseCode,
                tasks.WarehouseCode,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionConflict,
                409,
                "The Published warehouse changed while putaway candidates were generated.",
                recoveryAction: "retry-putaway-generation");
        }
        var first = inventory.Source;
        var second = tasks.Source;
        if (!string.Equals(first.Kind, second.Kind, StringComparison.Ordinal) ||
            !string.Equals(
                first.AdapterId,
                second.AdapterId,
                StringComparison.Ordinal) ||
            !string.Equals(
                first.DataSourceId,
                second.DataSourceId,
                StringComparison.Ordinal) ||
            first.IsSimulated != second.IsSimulated ||
            first.IsAvailable != second.IsAvailable)
        {
            throw ContractViolation(
                "The WMS source identity changed while putaway candidates were generated.");
        }
    }

    private static IReadOnlyList<string> Limitations(
        GenerateSpacePutawayRecommendationRequest request,
        SpacePutawayRecommendationSourcesDto sources,
        bool hasLocationWithoutGeometry)
    {
        var values = new List<string>
        {
            "INBOUND_QUANTITY_NOT_USED_AS_VOLUME_OR_WEIGHT_CAPACITY",
            "ONLY_EXPLICIT_DIMENSION_AND_MAX_LOAD_CONSTRAINTS_ARE_ENFORCED",
            "CONTAINER_STORAGE_CLASS_TEMPERATURE_AND_HAZARD_RULES_ARE_NOT_AVAILABLE",
            "ACTIVE_TASK_EXCLUSION_IS_POINT_IN_TIME",
            "INVENTORY_AND_TASK_OBSERVATIONS_ARE_NOT_ATOMIC",
            "DISTANCE_IS_PUBLISHED_GEOMETRIC_APPROXIMATION_NOT_ROUTE_DISTANCE",
            "RECOMMENDATION_REQUIRES_REVALIDATION_BEFORE_APPROVAL_OR_EXECUTION",
            "RECOMMENDATION_DOES_NOT_RESERVE_MOVE_OR_WRITE_INVENTORY",
        };
        if (request.OwnerId is null || request.LotNumber is null)
            values.Add("CONSOLIDATION_REQUIRES_EXPLICIT_OWNER_AND_LOT");
        if (sources.Inventory.IsSimulated || sources.ActiveTasks.IsSimulated)
            values.Add("SIMULATED_RUNTIME_SOURCE");
        if (hasLocationWithoutGeometry)
        {
            values.Add(
                "GEOMETRIC_PROXIMITY_UNAVAILABLE_FOR_LOCATIONS_WITHOUT_RACK_ANCHORS");
        }
        return values;
    }

    private static string RequestHash(
        Guid siteId,
        GenerateSpacePutawayRecommendationRequest request)
    {
        var values = new[]
        {
            DefinitionVersion,
            siteId.ToString("D"),
            request.MaterialNumber,
            request.OwnerId ?? string.Empty,
            request.LotNumber ?? string.Empty,
            Decimal(request.InboundQuantity),
            request.FloorLogicalId?.ToString("D") ?? string.Empty,
            request.ZoneLogicalId?.ToString("D") ?? string.Empty,
            request.RequiredWidthMillimeters?.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty,
            request.RequiredHeightMillimeters?.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty,
            request.RequiredDepthMillimeters?.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty,
            request.RequiredMaxLoad.HasValue
                ? Decimal(request.RequiredMaxLoad.Value)
                : string.Empty,
            request.AllowExactStockConsolidation ? "1" : "0",
            request.MaximumCandidates.ToString(CultureInfo.InvariantCulture),
        };
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join("\n", values))))
            .ToLowerInvariant();
    }

    private static string Decimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static SpacePutawayRecommendationDto Map(
        SpacePutawayRecommendation value)
    {
        var request = Deserialize<GenerateSpacePutawayRecommendationRequest>(
            value.RequestJson,
            "request");
        var sources = Deserialize<SpacePutawayRecommendationSourcesDto>(
            value.SourcesJson,
            "sources");
        var exclusions =
            Deserialize<SpacePutawayRecommendationExclusionsDto>(
                value.ExclusionsJson,
                "exclusions");
        var exclusionSamples = Deserialize<
            SpacePutawayRecommendationExclusionSampleDto[]>(
            value.ExclusionSamplesJson,
            "exclusion samples");
        var candidates = Deserialize<SpacePutawayRecommendationCandidateDto[]>(
            value.CandidatesJson,
            "candidates");
        var limitations = Deserialize<string[]>(
            value.LimitationsJson,
            "limitations");
        ValidatePersistedEvidence(
            value,
            exclusions,
            exclusionSamples,
            candidates);
        return new SpacePutawayRecommendationDto(
            value.Id,
            value.SiteId,
            value.PublishedVersionId,
            value.WarehouseCode,
            Utc(value.GeneratedAtUtc),
            value.GeneratedBy,
            value.DefinitionVersion,
            value.Outcome,
            request,
            sources,
            value.ExaminedLocationCount,
            value.EligibleCandidateCount,
            value.ReturnedCandidateCount,
            value.IsTruncated,
            exclusions,
            value.ExclusionSamplesTruncated,
            exclusionSamples,
            candidates,
            limitations);
    }

    private static void ValidateCandidateSet(SpacePutawayCandidateSet value)
    {
        var excluded = ExclusionCount(value.Exclusions);
        if (excluded + value.EligibleCandidateCount !=
            value.ExaminedLocationCount ||
            value.ExclusionSamples.Count > excluded ||
            value.ExclusionSamples.Count >
            SpacePutawayRecommendationEngine.MaximumExclusionSampleCount ||
            value.ExclusionSamplesTruncated !=
            (value.ExclusionSamples.Count < excluded) ||
            value.Candidates.Select(item => item.Rank)
                .SequenceEqual(
                    Enumerable.Range(1, value.Candidates.Count)) is false)
        {
            throw ContractViolation(
                "The generated putaway recommendation evidence does not reconcile.");
        }
    }

    private static void ValidatePersistedEvidence(
        SpacePutawayRecommendation value,
        SpacePutawayRecommendationExclusionsDto exclusions,
        IReadOnlyCollection<SpacePutawayRecommendationExclusionSampleDto>
            exclusionSamples,
        IReadOnlyCollection<SpacePutawayRecommendationCandidateDto> candidates)
    {
        var excluded = ExclusionCount(exclusions);
        if (candidates.Count != value.ReturnedCandidateCount ||
            excluded + value.EligibleCandidateCount !=
            value.ExaminedLocationCount ||
            exclusionSamples.Count > excluded ||
            exclusionSamples.Count >
            SpacePutawayRecommendationEngine.MaximumExclusionSampleCount ||
            value.ExclusionSamplesTruncated !=
            (exclusionSamples.Count < excluded) ||
            candidates.Select(item => item.Rank)
                .SequenceEqual(
                    Enumerable.Range(1, candidates.Count)) is false)
        {
            throw ContractViolation(
                "The persisted putaway recommendation evidence does not reconcile.");
        }
    }

    private static int ExclusionCount(
        SpacePutawayRecommendationExclusionsDto value) =>
        value.MissingSpatialMetadata +
        value.OutsideRequestedScope +
        value.ActiveTask +
        value.InvalidInventory +
        value.LocationCodeMismatch +
        value.OccupiedIncompatible +
        value.DimensionTooSmall +
        value.LoadUnverifiable +
        value.LoadInsufficient;

    private static T Deserialize<T>(string value, string field)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value, Json)
                   ?? throw new JsonException($"{field} is missing.");
        }
        catch (JsonException)
        {
            throw ContractViolation(
                $"The persisted putaway recommendation {field} is invalid.");
        }
    }

    private void EnsureInternalExecution()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PutawayRecommendationsInternalOnly,
                403,
                "Putaway recommendations are available to internal principals only.",
                recoveryAction: "use-internal-operations-principal");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private static void EnsureAvailable(SpaceWmsRuntimeSourceDto source)
    {
        if (!source.IsAvailable)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                503,
                "The WMS runtime source is unavailable.",
                recoveryAction: "retry-putaway-generation",
                retryable: true);
        }
    }

    private static void EnsureIdentity(Guid value, string field)
    {
        if (value == Guid.Empty)
            throw Invalid(field, "Identity is required.");
    }

    private static string RequiredIdentifier(
        string? value,
        int maximumLength,
        string field) =>
        OptionalIdentifier(value, maximumLength, field)
        ?? throw Invalid(field, "A value is required.");

    private static string? OptionalIdentifier(
        string? value,
        int maximumLength,
        string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                field,
                $"The identifier must be at most {maximumLength} characters and contain no control characters.");
        }
        return normalized.ToUpperInvariant();
    }

    private static string? NormalizeSourceIdentifier(
        string? value,
        int maximumLength,
        string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw ContractViolation(
                $"The inventory source returned an invalid {field} identifier.");
        }
        return normalized.ToUpperInvariant();
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException Invalid(
        string field,
        string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The putaway recommendation request is invalid.",
            $"{field}: {detail}",
            "correct-request");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.PutawayRecommendationNotFound,
            404,
            "The putaway recommendation was not found.",
            recoveryAction: "generate-putaway-recommendation");

    private static SpaceProblemException ContractViolation(string detail) =>
        new(
            SpaceErrorCodes.WmsRuntimeContractViolation,
            502,
            "The putaway recommendation source violated its contract.",
            detail,
            "check-putaway-source");

    private sealed record FloorRow(
        Guid LogicalId,
        string FloorCode,
        string FloorName,
        int FloorLevel);

    private sealed record ZoneRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        string ZoneCode);

    private sealed record RackRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid ZoneLogicalId,
        string RackCode,
        int X,
        int Y,
        decimal RotationZ);

    private sealed record LocationRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        string? LocationCode,
        int ColumnNo,
        int LevelNo,
        int DepthNo,
        int Width,
        int Height,
        int Depth,
        decimal? MaxLoad);

    private sealed record Point(decimal X, decimal Y);
}
