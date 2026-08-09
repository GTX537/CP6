using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpacePutawayRecommendationService
{
    Task<GenerateSpacePutawayRecommendationResponse> GenerateAsync(
        Guid siteId,
        Guid recommendationId,
        GenerateSpacePutawayRecommendationRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePutawayRecommendationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        CancellationToken cancellationToken = default);
}

public sealed record SpacePutawayLocationInput(
    Guid LocationLogicalId,
    string? SpaceLocationCode,
    Guid FloorLogicalId,
    string? FloorCode,
    string? FloorName,
    int FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    int ColumnNo,
    int LevelNo,
    int DepthNo,
    int WidthMillimeters,
    int HeightMillimeters,
    int DepthMillimeters,
    decimal? MaxLoad,
    decimal? AnchorXMillimeters,
    decimal? AnchorYMillimeters);

public sealed record SpacePutawayInventoryInput(
    Guid LocationLogicalId,
    decimal PhysicalQuantity,
    decimal AllocatedQuantity,
    string? MaterialNumber,
    string? OwnerId,
    string? LotNumber,
    bool CodeMatches);

public sealed record SpacePutawayCandidateSet(
    int ExaminedLocationCount,
    int EligibleCandidateCount,
    bool IsTruncated,
    SpacePutawayRecommendationExclusionsDto Exclusions,
    bool ExclusionSamplesTruncated,
    IReadOnlyList<SpacePutawayRecommendationExclusionSampleDto>
        ExclusionSamples,
    IReadOnlyList<SpacePutawayRecommendationCandidateDto> Candidates);

public sealed class SpacePutawayRecommendationEngine
{
    public const int MaximumExclusionSampleCount = 100;

    public SpacePutawayCandidateSet Generate(
        GenerateSpacePutawayRecommendationRequest request,
        IReadOnlyCollection<SpacePutawayLocationInput> locations,
        IReadOnlyCollection<SpacePutawayInventoryInput> inventory,
        IReadOnlySet<Guid> activeTaskLocations)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(activeTaskLocations);
        if (request.MaximumCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Maximum candidates must be positive.");
        }

        var inventoryByLocation = inventory
            .GroupBy(value => value.LocationLogicalId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var locationsById = locations.ToDictionary(
            value => value.LocationLogicalId);
        var matchingStockLocations = inventory
            .Where(value =>
                value.PhysicalQuantity > 0 &&
                value.AllocatedQuantity >= 0 &&
                value.AllocatedQuantity <= value.PhysicalQuantity &&
                value.CodeMatches &&
                Same(value.MaterialNumber, request.MaterialNumber) &&
                (request.OwnerId is null ||
                 Same(value.OwnerId, request.OwnerId)) &&
                (request.LotNumber is null ||
                 Same(value.LotNumber, request.LotNumber)))
            .Select(value => value.LocationLogicalId)
            .Distinct()
            .Where(locationsById.ContainsKey)
            .Select(value => locationsById[value])
            .ToArray();

        var counts = new ExclusionCounts();
        var excludedCount = 0;
        var samples = new List<SpacePutawayRecommendationExclusionSampleDto>();
        var candidates = new List<Candidate>();
        foreach (var location in locations
                     .OrderBy(value => value.FloorLevel)
                     .ThenBy(value => value.FloorCode, StringComparer.Ordinal)
                     .ThenBy(
                         value => value.SpaceLocationCode,
                         StringComparer.Ordinal)
                     .ThenBy(value => value.LocationLogicalId))
        {
            string? reason = null;
            if (string.IsNullOrWhiteSpace(location.SpaceLocationCode) ||
                string.IsNullOrWhiteSpace(location.FloorCode) ||
                string.IsNullOrWhiteSpace(location.FloorName))
            {
                counts.MissingSpatialMetadata++;
                reason = "MISSING_SPATIAL_METADATA";
            }
            else if ((request.FloorLogicalId.HasValue &&
                      request.FloorLogicalId != location.FloorLogicalId) ||
                     (request.ZoneLogicalId.HasValue &&
                      request.ZoneLogicalId != location.ZoneLogicalId))
            {
                counts.OutsideRequestedScope++;
                reason = "OUTSIDE_REQUESTED_SCOPE";
            }
            else if (activeTaskLocations.Contains(location.LocationLogicalId))
            {
                counts.ActiveTask++;
                reason = "ACTIVE_TASK_AT_OBSERVATION";
            }

            var rows = inventoryByLocation.GetValueOrDefault(
                location.LocationLogicalId) ?? [];
            if (reason is null && rows.Any(value =>
                    value.PhysicalQuantity < 0 ||
                    value.AllocatedQuantity < 0 ||
                    value.AllocatedQuantity > value.PhysicalQuantity))
            {
                counts.InvalidInventory++;
                reason = "INVALID_INVENTORY_QUANTITY";
            }
            if (reason is null && rows.Any(value => !value.CodeMatches))
            {
                counts.LocationCodeMismatch++;
                reason = "WMS_SPACE_LOCATION_CODE_MISMATCH";
            }

            var positive = rows
                .Where(value => value.PhysicalQuantity > 0)
                .ToArray();
            var canConsolidate =
                positive.Length > 0 &&
                request.AllowExactStockConsolidation &&
                request.OwnerId is not null &&
                request.LotNumber is not null &&
                positive.All(value =>
                    Same(value.MaterialNumber, request.MaterialNumber) &&
                    Same(value.OwnerId, request.OwnerId) &&
                    Same(value.LotNumber, request.LotNumber));
            if (reason is null && positive.Length > 0 && !canConsolidate)
            {
                counts.OccupiedIncompatible++;
                reason = "OCCUPIED_WITH_INCOMPATIBLE_STOCK";
            }
            if (reason is null &&
                ((request.RequiredWidthMillimeters.HasValue &&
                  location.WidthMillimeters <
                  request.RequiredWidthMillimeters.Value) ||
                 (request.RequiredHeightMillimeters.HasValue &&
                  location.HeightMillimeters <
                  request.RequiredHeightMillimeters.Value) ||
                 (request.RequiredDepthMillimeters.HasValue &&
                  location.DepthMillimeters <
                  request.RequiredDepthMillimeters.Value)))
            {
                counts.DimensionTooSmall++;
                reason = "PUBLISHED_DIMENSION_TOO_SMALL";
            }
            if (reason is null &&
                request.RequiredMaxLoad.HasValue &&
                !location.MaxLoad.HasValue)
            {
                counts.LoadUnverifiable++;
                reason = "PUBLISHED_MAX_LOAD_UNAVAILABLE";
            }
            if (reason is null &&
                request.RequiredMaxLoad.HasValue &&
                location.MaxLoad < request.RequiredMaxLoad)
            {
                counts.LoadInsufficient++;
                reason = "PUBLISHED_MAX_LOAD_INSUFFICIENT";
            }
            if (reason is not null)
            {
                excludedCount++;
                if (samples.Count < MaximumExclusionSampleCount)
                {
                    samples.Add(new SpacePutawayRecommendationExclusionSampleDto(
                        location.LocationLogicalId,
                        location.SpaceLocationCode,
                        location.FloorLogicalId,
                        location.FloorCode,
                        location.ZoneLogicalId,
                        location.ZoneCode,
                        reason));
                }
                continue;
            }

            var sameFloor = matchingStockLocations.Any(value =>
                value.FloorLogicalId == location.FloorLogicalId);
            var sameZone = location.ZoneLogicalId.HasValue &&
                matchingStockLocations.Any(value =>
                    value.ZoneLogicalId == location.ZoneLogicalId);
            var distance = NearestDistanceMeters(
                location,
                matchingStockLocations);
            var category = canConsolidate
                ? "ConsolidateExactStockIdentity"
                : sameZone || sameFloor
                    ? "EmptyNearExistingStock"
                    : "EmptyLocation";
            var rules = new List<string>
            {
                canConsolidate
                    ? "EXACT_SKU_OWNER_LOT_CONSOLIDATION"
                    : "EMPTY_AT_INVENTORY_OBSERVATION",
                "NO_ACTIVE_TASK_AT_TASK_OBSERVATION",
                "PUBLISHED_LOCATION_DIMENSIONS_ACCEPTED",
            };
            if (request.RequiredMaxLoad.HasValue)
                rules.Add("PUBLISHED_MAX_LOAD_VERIFIED");
            if (sameZone)
                rules.Add("SAME_ZONE_AS_MATCHING_STOCK");
            else if (sameFloor)
                rules.Add("SAME_FLOOR_AS_MATCHING_STOCK");
            if (distance.HasValue)
                rules.Add("DISTANCE_FROM_PUBLISHED_RACK_GEOMETRY");

            candidates.Add(new Candidate(
                location,
                category,
                positive.Sum(value => value.PhysicalQuantity),
                positive.Sum(value => value.AllocatedQuantity),
                sameFloor,
                sameZone,
                distance,
                rules));
        }

        var ordered = candidates
            .OrderBy(value => CategoryOrder(value.Category))
            .ThenByDescending(value => value.SameZone)
            .ThenByDescending(value => value.SameFloor)
            .ThenBy(value => value.DistanceMeters ?? decimal.MaxValue)
            .ThenBy(value => value.Location.LevelNo)
            .ThenBy(
                value => value.Location.SpaceLocationCode,
                StringComparer.Ordinal)
            .ThenBy(value => value.Location.LocationLogicalId)
            .ToArray();
        var returned = ordered
            .Take(request.MaximumCandidates)
            .Select((value, index) => Map(value, index + 1))
            .ToArray();

        return new SpacePutawayCandidateSet(
            locations.Count,
            ordered.Length,
            returned.Length < ordered.Length,
            counts.ToDto(),
            excludedCount > samples.Count,
            samples,
            returned);
    }

    private static SpacePutawayRecommendationCandidateDto Map(
        Candidate value,
        int rank) =>
        new(
            rank,
            value.Category,
            value.Location.LocationLogicalId,
            value.Location.SpaceLocationCode!,
            value.Location.FloorLogicalId,
            value.Location.FloorCode!,
            value.Location.FloorName!,
            value.Location.FloorLevel,
            value.Location.ZoneLogicalId,
            value.Location.ZoneCode,
            value.Location.RackLogicalId,
            value.Location.RackCode,
            value.Location.ColumnNo,
            value.Location.LevelNo,
            value.Location.DepthNo,
            value.Location.WidthMillimeters,
            value.Location.HeightMillimeters,
            value.Location.DepthMillimeters,
            value.Location.MaxLoad,
            value.CurrentPhysicalQuantity,
            value.CurrentAllocatedQuantity,
            value.SameFloor,
            value.SameZone,
            value.DistanceMeters,
            value.Rules);

    private static decimal? NearestDistanceMeters(
        SpacePutawayLocationInput candidate,
        IReadOnlyCollection<SpacePutawayLocationInput> matching)
    {
        if (!candidate.AnchorXMillimeters.HasValue ||
            !candidate.AnchorYMillimeters.HasValue)
        {
            return null;
        }
        var distances = matching
            .Where(value =>
                value.FloorLogicalId == candidate.FloorLogicalId &&
                value.AnchorXMillimeters.HasValue &&
                value.AnchorYMillimeters.HasValue)
            .Select(value =>
            {
                var dx = candidate.AnchorXMillimeters.Value -
                         value.AnchorXMillimeters!.Value;
                var dy = candidate.AnchorYMillimeters.Value -
                         value.AnchorYMillimeters!.Value;
                return (decimal)Math.Sqrt((double)(dx * dx + dy * dy)) /
                       1_000m;
            })
            .ToArray();
        return distances.Length == 0
            ? null
            : Math.Round(
                distances.Min(),
                3,
                MidpointRounding.AwayFromZero);
    }

    private static int CategoryOrder(string category) =>
        category switch
        {
            "ConsolidateExactStockIdentity" => 0,
            "EmptyNearExistingStock" => 1,
            _ => 2,
        };

    private static bool Same(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private sealed record Candidate(
        SpacePutawayLocationInput Location,
        string Category,
        decimal CurrentPhysicalQuantity,
        decimal CurrentAllocatedQuantity,
        bool SameFloor,
        bool SameZone,
        decimal? DistanceMeters,
        IReadOnlyList<string> Rules);

    private sealed class ExclusionCounts
    {
        public int MissingSpatialMetadata { get; set; }
        public int OutsideRequestedScope { get; set; }
        public int ActiveTask { get; set; }
        public int InvalidInventory { get; set; }
        public int LocationCodeMismatch { get; set; }
        public int OccupiedIncompatible { get; set; }
        public int DimensionTooSmall { get; set; }
        public int LoadUnverifiable { get; set; }
        public int LoadInsufficient { get; set; }

        public SpacePutawayRecommendationExclusionsDto ToDto() =>
            new(
                MissingSpatialMetadata,
                OutsideRequestedScope,
                ActiveTask,
                InvalidInventory,
                LocationCodeMismatch,
                OccupiedIncompatible,
                DimensionTooSmall,
                LoadUnverifiable,
                LoadInsufficient);
    }
}
