using CP6.Space.Application;

namespace CP6.Space.UnitTests;

public sealed class SpaceBuiltInWarehouseTemplateTests
{
    [Fact]
    public void Standard_template_is_stable_complete_and_preview_only()
    {
        var first = Assert.Single(SpaceBuiltInWarehouseTemplates.List());
        var second = Assert.Single(SpaceBuiltInWarehouseTemplates.List());

        Assert.Equal("System", first.Scope);
        Assert.Equal("SPACE-STANDARD-01", first.TemplateCode);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.LatestVersion.Id, second.LatestVersion.Id);
        Assert.Equal(first.LatestVersion.ContentHash, second.LatestVersion.ContentHash);
        Assert.Equal(64, first.LatestVersion.ContentHash.Length);
        Assert.Equal(2, first.LatestVersion.Counts.Floors);
        Assert.Equal(7, first.LatestVersion.Counts.Zones);
        Assert.Equal(20, first.LatestVersion.Counts.Aisles);
        Assert.Equal(500, first.LatestVersion.Counts.Racks);
        Assert.Equal(10_000, first.LatestVersion.Counts.Locations);

        Assert.True(SpaceBuiltInWarehouseTemplates.TryPreview(
            first.Id,
            first.LatestVersion.Id,
            out var preview));
        Assert.NotNull(preview);
        Assert.False(preview.WritesDraft);
        Assert.Equal(first.LatestVersion.ContentHash, preview.TemplateContentHash);
        Assert.Equal(64, preview.ProposalHash.Length);
        Assert.Equal(first.LatestVersion.Counts, preview.Counts);
        Assert.Equal(2, preview.Floors.Count);
        Assert.Equal(7, preview.Zones.Count);
        Assert.Equal(20, preview.Aisles.Count);
        Assert.Equal(500, preview.Racks.Count);
    }

    [Fact]
    public void Standard_template_plan_references_only_declared_parents()
    {
        var template = Assert.Single(SpaceBuiltInWarehouseTemplates.List());
        Assert.True(SpaceBuiltInWarehouseTemplates.TryPreview(
            template.Id,
            template.LatestVersion.Id,
            out var preview));
        Assert.NotNull(preview);

        var floorKeys = preview.Floors.Select(value => value.Key).ToHashSet();
        var zoneKeys = preview.Zones.Select(value => value.Key).ToHashSet();
        var aisleKeys = preview.Aisles.Select(value => value.Key).ToHashSet();
        Assert.Equal(preview.Floors.Count, floorKeys.Count);
        Assert.Equal(preview.Zones.Count, zoneKeys.Count);
        Assert.Equal(preview.Aisles.Count, aisleKeys.Count);
        Assert.Equal(preview.Racks.Count,
            preview.Racks.Select(value => value.Key).Distinct().Count());
        Assert.All(preview.Zones,
            value => Assert.Contains(value.FloorKey, floorKeys));
        Assert.All(preview.Aisles, value =>
        {
            Assert.Contains(value.FloorKey, floorKeys);
            Assert.Contains(value.ZoneKey, zoneKeys);
        });
        Assert.All(preview.Racks, value =>
        {
            Assert.Contains(value.FloorKey, floorKeys);
            Assert.Contains(value.ZoneKey, zoneKeys);
            Assert.Contains(value.AisleKey, aisleKeys);
            Assert.True(value.Columns > 0);
            Assert.True(value.Levels > 0);
            Assert.True(value.Depths > 0);
        });

        Assert.False(SpaceBuiltInWarehouseTemplates.TryPreview(
            template.Id,
            Guid.NewGuid(),
            out _));
    }
}
