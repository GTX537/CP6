using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceWarehouseTemplatePlanCodecTests
{
    [Fact]
    public void Tenant_plan_is_canonical_sealed_and_buildable()
    {
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var request = Request();

        var preview = SpaceWarehouseTemplatePlanCodec.Seal(
            templateId,
            versionId,
            request.SchemaVersion,
            request.Floors,
            request.Zones,
            request.Aisles,
            request.Racks);
        var reordered = SpaceWarehouseTemplatePlanCodec.Seal(
            templateId,
            versionId,
            request.SchemaVersion,
            request.Floors.Reverse().ToArray(),
            request.Zones.Reverse().ToArray(),
            request.Aisles.Reverse().ToArray(),
            request.Racks.Reverse().ToArray());

        Assert.Equal(preview.TemplateContentHash, reordered.TemplateContentHash);
        Assert.Equal(preview.ProposalHash, reordered.ProposalHash);
        Assert.Equal(1, preview.Counts.Floors);
        Assert.Equal(4, preview.Counts.Locations);
        Assert.Equal("Storage", preview.Zones[0].ZoneType);
        Assert.False(preview.WritesDraft);

        var contentJson = SpaceWarehouseTemplatePlanCodec.SerializeContent(preview);
        var restored = SpaceWarehouseTemplatePlanCodec.ReadAndSeal(
            templateId,
            versionId,
            contentJson,
            preview.TemplateContentHash);
        Assert.Equal(preview.TemplateContentHash, restored.TemplateContentHash);
        Assert.Equal(preview.ProposalHash, restored.ProposalHash);
        Assert.Equal(preview.Counts, restored.Counts);
        Assert.Equal(preview.Floors, restored.Floors);
        Assert.Equal(preview.Zones, restored.Zones);
        Assert.Equal(preview.Aisles, restored.Aisles);
        Assert.Equal(preview.Racks, restored.Racks);

        Assert.True(SpaceBuiltInWarehouseTemplates.TryBuildFloorCommandBatch(
            preview,
            "floor:f1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            expectedFloorRevision: 2,
            expectedContentRevision: 3,
            out var floor,
            out var counts,
            out var commandBatch));
        Assert.Equal("F1", floor!.FloorCode);
        Assert.Equal(4, counts!.Locations);
        Assert.Equal(3, commandBatch!.Commands.Count);
    }

    [Fact]
    public void Tenant_plan_rejects_invalid_parent_chains_and_duplicate_layout_keys()
    {
        var request = Request();
        var invalidParent = request.Racks
            .Select(rack => rack with { ZoneKey = "zone:unknown" })
            .ToArray();
        Assert.Throws<ArgumentException>(() =>
            SpaceWarehouseTemplatePlanCodec.Seal(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request.SchemaVersion,
                request.Floors,
                request.Zones,
                request.Aisles,
                invalidParent));

        var duplicateKey = request.Racks
            .Select(rack => rack with { Key = "aisle:a1" })
            .ToArray();
        Assert.Throws<ArgumentException>(() =>
            SpaceWarehouseTemplatePlanCodec.Seal(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request.SchemaVersion,
                request.Floors,
                request.Zones,
                request.Aisles,
                duplicateKey));
    }

    [Fact]
    public void Tenant_template_domain_rejects_invalid_metadata_and_hashes()
    {
        Assert.Throws<ArgumentException>(() =>
            SpaceWarehouseTemplate.CreateTenant(
                Guid.NewGuid(),
                " ",
                "Name",
                null));
        var template = SpaceWarehouseTemplate.CreateTenant(
            Guid.NewGuid(),
            " private-01 ",
            " Private warehouse ",
            " Tenant only ");
        Assert.Equal("private-01", template.TemplateCode);
        Assert.Equal("PRIVATE-01", template.NormalizedTemplateCode);
        Assert.Throws<ArgumentException>(() =>
            SpaceWarehouseTemplateVersion.CreateReady(
                template.TenantId,
                Guid.NewGuid(),
                template.Id,
                1,
                1,
                "{}",
                "not-a-hash",
                1,
                0,
                0,
                0,
                0));
    }

    private static CreateTenantSpaceWarehouseTemplateRequest Request() =>
        new(
            "PRIVATE-01",
            "Private warehouse",
            null,
            SpaceWarehouseTemplateContract.SchemaVersion,
            [
                new SpaceWarehouseTemplateFloorPlanDto(
                    " floor:f1 ", "F1", "Floor 1", 1, 0,
                    10_000, 8_000, 6_000),
            ],
            [
                new SpaceWarehouseTemplateZonePlanDto(
                    "zone:z1", "floor:f1", "Z1", " storage ",
                    0, 0, 10_000, 8_000),
            ],
            [
                new SpaceWarehouseTemplateAislePlanDto(
                    "aisle:a1", "floor:f1", "zone:z1", "A1",
                    5_000, 0, 5_000, 8_000),
            ],
            [
                new SpaceWarehouseTemplateRackPlanDto(
                    "rack:r1", "floor:f1", "zone:z1", "aisle:a1", "R1",
                    1_000, 1_000, 0, 0,
                    2_000, 1_000, 3_000,
                    2, 2, 1),
            ]);
}
