using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceRackGenerationProfileTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ready_version_canonicalizes_levels_and_computes_locations()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var firstProfile = SpaceRackGenerationProfile.CreateTenant(
            tenantId,
            "RACK-A",
            "Rack A",
            null,
            actorId,
            Now);
        var secondProfile = SpaceRackGenerationProfile.CreateTenant(
            tenantId,
            "RACK-B",
            "Rack B",
            null,
            actorId,
            Now);
        SpaceRackGenerationProfileLevel[] levels =
        [
            new(2, 2500, 2000, 4, 2, 600, 500, 100, 800),
            new(1, 0, 2200, 4, 2, 600, 500, 100, 1000),
        ];

        var first = SpaceRackGenerationProfileVersion.CreateReady(
            firstProfile,
            1,
            2400,
            1000,
            5000,
            levels,
            actorId,
            Now);
        var second = SpaceRackGenerationProfileVersion.CreateReady(
            secondProfile,
            1,
            2400,
            1000,
            5000,
            levels.Reverse().ToArray(),
            actorId,
            Now);

        Assert.Equal([1, 2], first.ReadLevels().Select(level => level.LevelNo));
        Assert.Equal(16, first.LocationCount);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
    }

    [Fact]
    public void Invalid_level_dimensions_are_rejected()
    {
        var profile = SpaceRackGenerationProfile.CreateTenant(
            Guid.NewGuid(),
            "RACK-A",
            "Rack A",
            null,
            Guid.NewGuid(),
            Now);

        Assert.Throws<ArgumentException>(() =>
            SpaceRackGenerationProfileVersion.CreateReady(
                profile,
                1,
                2400,
                1000,
                5000,
                [new(1, 0, 2200, 5, 2, 600, 500, 100)],
                Guid.NewGuid(),
                Now));
    }

    [Fact]
    public void Excessive_derived_location_count_is_rejected_as_input()
    {
        var profile = SpaceRackGenerationProfile.CreateTenant(
            Guid.NewGuid(),
            "LARGE-RACK",
            "Large rack",
            null,
            Guid.NewGuid(),
            Now);

        var error = Assert.Throws<ArgumentException>(() =>
            SpaceRackGenerationProfileVersion.CreateReady(
                profile,
                1,
                int.MaxValue,
                int.MaxValue,
                5000,
                [new(1, 0, 2200, int.MaxValue, int.MaxValue, 1, 1)],
                Guid.NewGuid(),
                Now));

        Assert.Contains("location limit", error.Message);
    }
}
