using CP6.Space.Application;
using CP6.Space.Contracts;

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

    [Fact]
    public void Standard_template_builds_a_deterministic_floor_command_batch()
    {
        var template = Assert.Single(SpaceBuiltInWarehouseTemplates.List());
        Assert.True(SpaceBuiltInWarehouseTemplates.TryPreview(
            template.Id,
            template.LatestVersion.Id,
            out var preview));
        Assert.NotNull(preview);
        var floor = Assert.Single(
            preview.Floors,
            candidate => candidate.FloorCode == "F1");
        var modelVersionId = Guid.NewGuid();
        var floorLogicalId = Guid.NewGuid();
        var commandBatchId = Guid.NewGuid();
        var clientInstanceId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();

        Assert.True(SpaceBuiltInWarehouseTemplates.TryBuildFloorCommandBatch(
            template.Id,
            template.LatestVersion.Id,
            floor.Key,
            modelVersionId,
            floorLogicalId,
            commandBatchId,
            clientInstanceId,
            leaseId,
            expectedFloorRevision: 4,
            expectedContentRevision: 9,
            out var selectedFloor,
            out var counts,
            out var batch));
        Assert.NotNull(selectedFloor);
        Assert.NotNull(counts);
        Assert.NotNull(batch);
        Assert.Equal(floor, selectedFloor);
        Assert.Equal(1, counts.Floors);
        Assert.Equal(3, counts.Zones);
        Assert.Equal(10, counts.Aisles);
        Assert.Equal(250, counts.Racks);
        Assert.Equal(5_000, counts.Locations);
        Assert.Equal(263, batch.Commands.Count);
        Assert.Equal(3, batch.Commands.Count(candidate =>
            candidate.Type == SpaceLayoutCommandContract.CreateZone));
        Assert.Equal(10, batch.Commands.Count(candidate =>
            candidate.Type == SpaceLayoutCommandContract.CreateAisle));
        Assert.Equal(250, batch.Commands.Count(candidate =>
            candidate.Type == SpaceLayoutCommandContract.CreateRack));
        Assert.Equal(commandBatchId, batch.CommandBatchId);
        Assert.Equal(clientInstanceId, batch.ClientInstanceId);
        Assert.Equal(leaseId, batch.LeaseId);
        Assert.Equal(4, batch.ExpectedFloorRevision);
        Assert.Equal(9, batch.ExpectedContentRevision);
        Assert.Equal(
            5_000,
            batch.Commands
                .Where(candidate => candidate.CreateRack is not null)
                .SelectMany(candidate => candidate.CreateRack!.Levels)
                .Sum(level => level.BinCount * level.DepthCount));

        Assert.True(SpaceBuiltInWarehouseTemplates.TryBuildFloorCommandBatch(
            template.Id,
            template.LatestVersion.Id,
            floor.Key,
            modelVersionId,
            floorLogicalId,
            commandBatchId,
            clientInstanceId,
            leaseId,
            4,
            9,
            out _,
            out _,
            out var replayBatch));
        Assert.NotNull(replayBatch);
        Assert.Equal(
            batch.Commands.Select(candidate => candidate.CommandId),
            replayBatch.Commands.Select(candidate => candidate.CommandId));
        Assert.Equal(
            batch.Commands.Select(candidate => candidate.TargetLogicalId),
            replayBatch.Commands.Select(candidate => candidate.TargetLogicalId));

        Assert.False(SpaceBuiltInWarehouseTemplates.TryBuildFloorCommandBatch(
            template.Id,
            template.LatestVersion.Id,
            "floor:unknown",
            modelVersionId,
            floorLogicalId,
            commandBatchId,
            clientInstanceId,
            leaseId,
            4,
            9,
            out _,
            out _,
            out _));
    }
}
