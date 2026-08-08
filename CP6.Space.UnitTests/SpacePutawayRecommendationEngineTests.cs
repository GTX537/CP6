using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpacePutawayRecommendationEngineTests
{
    private static readonly Guid FloorA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FloorB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneA =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ZoneB =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Exact_identity_then_near_empty_then_other_empty_are_stable()
    {
        var locations = new[]
        {
            Location("F1-L01", FloorA, ZoneA, 0, 0),
            Location("F1-L02", FloorA, ZoneA, 2_000, 0),
            Location("F2-L01", FloorB, ZoneB, 0, 0),
            Location("F1-L03", FloorA, ZoneA, 3_000, 0),
        };
        var inventory = new[]
        {
            Inventory(locations[0], "SKU-1", "OWNER-1", "LOT-1", 10, 2),
            Inventory(locations[3], "SKU-OTHER", "OWNER-1", "LOT-1", 1, 0),
        };

        var result = new SpacePutawayRecommendationEngine().Generate(
            Request(),
            locations,
            inventory,
            new HashSet<Guid>());

        Assert.Equal(4, result.ExaminedLocationCount);
        Assert.Equal(3, result.EligibleCandidateCount);
        Assert.Equal(
            ["F1-L01", "F1-L02", "F2-L01"],
            result.Candidates.Select(value => value.SpaceLocationCode));
        Assert.Equal(
            "ConsolidateExactStockIdentity",
            result.Candidates[0].Category);
        Assert.Equal("EmptyNearExistingStock", result.Candidates[1].Category);
        Assert.Equal(2m, result.Candidates[1].DistanceToMatchingStockMeters);
        Assert.Equal("EmptyLocation", result.Candidates[2].Category);
        Assert.Equal(1, result.Exclusions.OccupiedIncompatible);
        var sample = Assert.Single(result.ExclusionSamples);
        Assert.Equal("OCCUPIED_WITH_INCOMPATIBLE_STOCK", sample.Reason);
        Assert.False(result.ExclusionSamplesTruncated);
    }

    [Fact]
    public void Every_ineligible_location_records_exactly_one_first_reason()
    {
        var values = Enumerable.Range(1, 9)
            .Select(index => Location($"F1-L{index:00}", FloorA, ZoneA, index * 1_000, 0))
            .ToArray();
        values[0] = values[0] with { SpaceLocationCode = null };
        values[1] = values[1] with { FloorLogicalId = FloorB, ZoneLogicalId = ZoneB };
        values[6] = values[6] with { WidthMillimeters = 100 };
        values[7] = values[7] with { MaxLoad = null };
        values[8] = values[8] with { MaxLoad = 50 };
        var inventory = new[]
        {
            Inventory(values[3], "SKU-1", "OWNER-1", "LOT-1", 1, 2),
            Inventory(values[4], "SKU-1", "OWNER-1", "LOT-1", 1, 0, false),
            Inventory(values[5], "SKU-X", "OWNER-1", "LOT-1", 1, 0),
        };

        var result = new SpacePutawayRecommendationEngine().Generate(
            Request() with
            {
                FloorLogicalId = FloorA,
                RequiredWidthMillimeters = 500,
                RequiredMaxLoad = 100,
            },
            values,
            inventory,
            new HashSet<Guid> { values[2].LocationLogicalId });

        Assert.Equal(9, result.ExaminedLocationCount);
        Assert.Equal(0, result.EligibleCandidateCount);
        Assert.Empty(result.Candidates);
        Assert.Equal(1, result.Exclusions.MissingSpatialMetadata);
        Assert.Equal(1, result.Exclusions.OutsideRequestedScope);
        Assert.Equal(1, result.Exclusions.ActiveTask);
        Assert.Equal(1, result.Exclusions.InvalidInventory);
        Assert.Equal(1, result.Exclusions.LocationCodeMismatch);
        Assert.Equal(1, result.Exclusions.OccupiedIncompatible);
        Assert.Equal(1, result.Exclusions.DimensionTooSmall);
        Assert.Equal(1, result.Exclusions.LoadUnverifiable);
        Assert.Equal(1, result.Exclusions.LoadInsufficient);
        Assert.Equal(9, result.ExclusionSamples.Count);
        Assert.Equal(9, result.ExclusionSamples.Select(x => x.Reason).Distinct().Count());
    }

    [Fact]
    public void Exclusion_samples_are_stably_bounded_at_one_hundred()
    {
        var locations = Enumerable.Range(1, 101)
            .Select(index => Location($"F2-L{index:000}", FloorB, ZoneB, index, 0))
            .Reverse()
            .ToArray();

        var result = new SpacePutawayRecommendationEngine().Generate(
            Request() with { FloorLogicalId = FloorA },
            locations,
            [],
            new HashSet<Guid>());

        Assert.Equal(101, result.Exclusions.OutsideRequestedScope);
        Assert.Equal(100, result.ExclusionSamples.Count);
        Assert.True(result.ExclusionSamplesTruncated);
        Assert.Equal(
            result.ExclusionSamples.OrderBy(value => value.SpaceLocationCode),
            result.ExclusionSamples);
    }

    [Fact]
    public void Missing_owner_or_lot_never_consolidates_occupied_stock()
    {
        var location = Location("F1-L01", FloorA, ZoneA, 0, 0);
        var result = new SpacePutawayRecommendationEngine().Generate(
            Request() with { OwnerId = null },
            [location],
            [Inventory(location, "SKU-1", "OWNER-1", "LOT-1", 10, 0)],
            new HashSet<Guid>());

        Assert.Empty(result.Candidates);
        Assert.Equal(1, result.Exclusions.OccupiedIncompatible);
    }

    [Fact]
    public void Candidate_count_is_truncated_after_stable_tie_breaking()
    {
        var first = Location("F1-L02", FloorA, ZoneA, 0, 0);
        var second = Location("F1-L01", FloorA, ZoneA, 0, 0);
        var third = Location("F1-L03", FloorA, ZoneA, 0, 0);

        var result = new SpacePutawayRecommendationEngine().Generate(
            Request() with { MaximumCandidates = 2 },
            [third, first, second],
            [],
            new HashSet<Guid>());

        Assert.Equal(3, result.EligibleCandidateCount);
        Assert.True(result.IsTruncated);
        Assert.Equal(
            ["F1-L01", "F1-L02"],
            result.Candidates.Select(value => value.SpaceLocationCode));
        Assert.Equal([1, 2], result.Candidates.Select(value => value.Rank));
    }

    private static GenerateSpacePutawayRecommendationRequest Request() =>
        new("SKU-1", "OWNER-1", "LOT-1", 5, MaximumCandidates: 10);

    private static SpacePutawayLocationInput Location(
        string code,
        Guid floorId,
        Guid zoneId,
        decimal x,
        decimal y) =>
        new(
            Guid.NewGuid(),
            code,
            floorId,
            floorId == FloorA ? "F1" : "F2",
            floorId == FloorA ? "Floor 1" : "Floor 2",
            floorId == FloorA ? 1 : 2,
            zoneId,
            zoneId == ZoneA ? "Z1" : "Z2",
            Guid.NewGuid(),
            "R1",
            1,
            1,
            1,
            1_000,
            1_000,
            1_000,
            200,
            x,
            y);

    private static SpacePutawayInventoryInput Inventory(
        SpacePutawayLocationInput location,
        string material,
        string owner,
        string lot,
        decimal physical,
        decimal allocated,
        bool codeMatches = true) =>
        new(
            location.LocationLogicalId,
            physical,
            allocated,
            material,
            owner,
            lot,
            codeMatches);
}
