using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceRackLevelRevisionTests
{
    [Fact]
    public void Different_levels_preserve_independent_dimensions_and_loads()
    {
        var tenantId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var rackLogicalId = Guid.NewGuid();

        var lower = SpaceRackLevelRevision.Create(
            tenantId,
            versionId,
            Guid.NewGuid(),
            rackLogicalId,
            levelNo: 1,
            bottomZ: 0,
            clearHeight: 1200,
            binCount: 4,
            depthCount: 1,
            cellWidth: 1000,
            cellDepth: 900,
            maxLoad: 1500.5m,
            beamHeight: 100);
        var upper = SpaceRackLevelRevision.Create(
            tenantId,
            versionId,
            Guid.NewGuid(),
            rackLogicalId,
            levelNo: 2,
            bottomZ: 1300,
            clearHeight: 800,
            binCount: 3,
            depthCount: 2,
            cellWidth: 1200,
            cellDepth: 1100,
            maxLoad: 750m,
            beamHeight: 80);

        Assert.Equal(1200, lower.ClearHeight);
        Assert.Equal(4, lower.BinCount);
        Assert.Equal(1, lower.DepthCount);
        Assert.Equal(1000, lower.CellWidth);
        Assert.Equal(900, lower.CellDepth);
        Assert.Equal(100, lower.BeamHeight);
        Assert.Equal(1500.5m, lower.MaxLoad);

        Assert.Equal(1300, upper.BottomZ);
        Assert.Equal(800, upper.ClearHeight);
        Assert.Equal(3, upper.BinCount);
        Assert.Equal(2, upper.DepthCount);
        Assert.Equal(1200, upper.CellWidth);
        Assert.Equal(1100, upper.CellDepth);
        Assert.Equal(80, upper.BeamHeight);
        Assert.Equal(750m, upper.MaxLoad);
    }

    [Fact]
    public void Update_specification_changes_all_per_level_values()
    {
        var level = NewLevel();

        level.UpdateSpecification(
            levelNo: 2,
            bottomZ: 1450,
            clearHeight: 900,
            binCount: 5,
            depthCount: 2,
            cellWidth: 800,
            cellDepth: 1000,
            maxLoad: 975.25m,
            beamHeight: 90);

        Assert.Equal(2, level.LevelNo);
        Assert.Equal(1450, level.BottomZ);
        Assert.Equal(900, level.ClearHeight);
        Assert.Equal(5, level.BinCount);
        Assert.Equal(2, level.DepthCount);
        Assert.Equal(800, level.CellWidth);
        Assert.Equal(1000, level.CellDepth);
        Assert.Equal(90, level.BeamHeight);
        Assert.Equal(975.25m, level.MaxLoad);
    }

    [Theory]
    [InlineData(0, 1000, 1, 1, 1000, 800)]
    [InlineData(1, 0, 1, 1, 1000, 800)]
    [InlineData(1, 1000, 0, 1, 1000, 800)]
    [InlineData(1, 1000, 1, 0, 1000, 800)]
    [InlineData(1, 1000, 1, 1, 0, 800)]
    [InlineData(1, 1000, 1, 1, 1000, 0)]
    public void Positive_fields_reject_zero(
        int levelNo,
        int clearHeight,
        int binCount,
        int depthCount,
        int cellWidth,
        int cellDepth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpaceRackLevelRevision.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                levelNo,
                bottomZ: 0,
                clearHeight,
                binCount,
                depthCount,
                cellWidth,
                cellDepth));
    }

    [Fact]
    public void Negative_offsets_beam_height_and_load_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewLevel(bottomZ: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewLevel(beamHeight: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewLevel(maxLoad: -0.0001m));
    }

    [Fact]
    public void Invalid_update_does_not_partially_mutate_the_level()
    {
        var level = NewLevel();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => level.UpdateSpecification(
                levelNo: 2,
                bottomZ: 1200,
                clearHeight: 900,
                binCount: 3,
                depthCount: 2,
                cellWidth: 900,
                cellDepth: 1000,
                maxLoad: -1,
                beamHeight: 80));

        Assert.Equal(1, level.LevelNo);
        Assert.Equal(0, level.BottomZ);
        Assert.Equal(1000, level.ClearHeight);
        Assert.Equal(0, level.BeamHeight);
        Assert.Null(level.MaxLoad);
    }

    [Fact]
    public void Rack_logical_identity_is_required()
    {
        Assert.Throws<ArgumentException>(
            () => SpaceRackLevelRevision.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                1,
                0,
                1000,
                1,
                1,
                1000,
                800));
    }

    private static SpaceRackLevelRevision NewLevel(
        int bottomZ = 0,
        int beamHeight = 0,
        decimal? maxLoad = null) =>
        SpaceRackLevelRevision.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            bottomZ,
            1000,
            1,
            1,
            1000,
            800,
            maxLoad,
            beamHeight);
}
