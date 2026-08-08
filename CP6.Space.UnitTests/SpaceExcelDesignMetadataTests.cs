using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceExcelDesignMetadataTests
{
    [Fact]
    public void Location_type_is_canonical_and_updates_with_imported_specification()
    {
        var tenantId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var location = SpaceLocationRevision.Create(
            tenantId,
            versionId,
            Guid.NewGuid(),
            floorId,
            rackId,
            "L-01",
            1,
            1,
            1,
            100,
            200,
            300,
            locationType: "storage");

        Assert.Equal(SpaceLocationTypes.Storage, location.LocationType);

        location.UpdateImportedSpecification(
            floorId,
            rackId,
            "L-01",
            1,
            1,
            1,
            100,
            200,
            300,
            locationType: "PICKING");

        Assert.Equal(SpaceLocationTypes.Picking, location.LocationType);
        Assert.Throws<ArgumentException>(() =>
            SpaceLocationTypes.NormalizeOptional("Unknown"));
    }

    [Fact]
    public void External_binding_pins_one_snapshot_and_normalizes_identity()
    {
        var tenantId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var location = Location(tenantId, versionId);
        var source = Source(tenantId, versionId);

        var binding = SpaceLocationExternalBinding.Create(
            tenantId,
            Guid.NewGuid(),
            location,
            " adapter-v1 ",
            " WH-01 ",
            " EXT-01 ",
            SpaceLocationBindingMode.WmsPrimary,
            source,
            " Bindings!2 ");

        Assert.Equal("adapter-v1", binding.AdapterId);
        Assert.Equal("WH-01", binding.WarehouseCode);
        Assert.Equal("EXT-01", binding.ExternalLocationId);
        Assert.Equal(location.LogicalId, binding.LocationLogicalId);
        Assert.Equal(source.Id, binding.SourceId);

        binding.ChangeBindingMode(SpaceLocationBindingMode.WmsAlias);
        Assert.Equal(SpaceLocationBindingMode.WmsAlias, binding.BindingMode);

        var otherVersionLocation = Location(tenantId, Guid.NewGuid());
        Assert.Throws<SpaceTenantScopeException>(() => binding.UpdateTarget(
            tenantId,
            otherVersionLocation,
            SpaceLocationBindingMode.WmsAlias,
            source,
            "Bindings!3"));
    }

    [Fact]
    public void Design_attribute_has_typed_target_and_soft_delete_lifecycle()
    {
        var tenantId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var source = Source(tenantId, versionId);
        var attribute = SpaceDesignAttribute.Create(
            tenantId,
            Guid.NewGuid(),
            versionId,
            "racklevel",
            Guid.NewGuid(),
            "manufacturing",
            " BeamProfile ",
            " B-100 ",
            " mm ",
            source,
            "Attributes!2");

        Assert.Equal(SpaceDesignAttributeObjectTypes.RackLevel,
            attribute.ObjectType);
        Assert.Equal(SpaceDesignAttributeNamespaces.Manufacturing,
            attribute.Namespace);
        Assert.Equal("BeamProfile", attribute.Key);
        Assert.Equal("B-100", attribute.Value);
        Assert.Equal("mm", attribute.Unit);

        attribute.UpdateValue("B-200", null, source, "Attributes!3");
        Assert.Equal("B-200", attribute.Value);
        Assert.Null(attribute.Unit);
        attribute.Remove();
        Assert.True(attribute.IsDeleted);
        Assert.Throws<ArgumentException>(() =>
            SpaceDesignAttributeNamespaces.Normalize("Runtime"));
    }

    private static SpaceLocationRevision Location(
        Guid tenantId,
        Guid versionId) => SpaceLocationRevision.Create(
        tenantId,
        versionId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "L-01",
        1,
        1,
        1,
        100,
        200,
        300);

    private static SpaceModelSource Source(Guid tenantId, Guid versionId) =>
        SpaceModelSource.CreateInlineSource(
            tenantId,
            versionId,
            SpaceSourceType.Template,
            "Excel metadata test",
            new string('a', 64));
}
