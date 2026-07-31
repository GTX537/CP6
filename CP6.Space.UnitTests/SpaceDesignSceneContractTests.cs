using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceDesignSceneContractTests
{
    [Fact]
    public void Scene_contract_is_versioned_and_design_revision_authoritative()
    {
        Assert.Equal(1, SpaceDesignSceneContract.SchemaVersion);
        Assert.Equal(
            "DesignRevision",
            SpaceDesignSceneContract.Authority);
        Assert.Equal(
            "SPACE_LOGICAL_ID_NOT_FOUND",
            SpaceErrorCodes.LogicalIdNotFound);

        var properties = typeof(SpaceDesignSceneDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SpaceDesignSceneDto.RackLevels), properties);
        Assert.Contains(nameof(SpaceDesignSceneDto.Elements), properties);
        Assert.Contains(
            nameof(SpaceDesignSceneDto.ElementAttributes),
            properties);
        Assert.Contains(nameof(SpaceDesignSceneDto.Locations), properties);
        Assert.Contains(
            nameof(SpaceDesignSceneDto.RuntimeOverlayIncluded),
            properties);
    }

    [Fact]
    public void Semantic_scene_has_no_runtime_inventory_or_task_payload()
    {
        var propertyNames = typeof(SpaceDesignSceneDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(
                "Inventory",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(
                "Stock",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(
                "Task",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(
                "Personnel",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(
                "DeviceState",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Element_attributes_do_not_embed_location_facts()
    {
        var propertyNames = typeof(SpaceSceneElementAttributeDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(
            [
                nameof(SpaceSceneElementAttributeDto.Id),
                nameof(SpaceSceneElementAttributeDto.ElementRevisionId),
                nameof(SpaceSceneElementAttributeDto.Namespace),
                nameof(SpaceSceneElementAttributeDto.Key),
                nameof(SpaceSceneElementAttributeDto.ValueType),
                nameof(SpaceSceneElementAttributeDto.Value),
                nameof(SpaceSceneElementAttributeDto.Unit),
            ],
            StringComparer.Ordinal);
        Assert.True(expected.SetEquals(propertyNames));
    }

    [Fact]
    public void Scene_element_exposes_existing_asset_reference_only()
    {
        var propertyNames = typeof(SpaceSceneElementDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SpaceSceneElementDto.ModelAssetId), propertyNames);
        Assert.DoesNotContain("ModelAssetScope", propertyNames);
        Assert.DoesNotContain("ModelAssetOwnerTenantId", propertyNames);
    }
}
