using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadInventoryTests
{
    [Fact]
    public void Build_preserves_complete_layer_block_attribute_and_range_inventory()
    {
        var (request, preparation) = PreparedPackage();

        var result = SpaceCadInventory.Build(request, preparation);

        Assert.Equal(request.SourceSha256, result.SourceSha256);
        Assert.Equal(preparation.Metadata.TransformSha256, result.CoordinateTransformSha256);
        Assert.Equal("F01", result.FloorCode);
        Assert.Equal(4, result.Summary.LayerCount);
        Assert.Equal(1, result.Summary.EmptyLayerCount);
        Assert.Equal(4, result.Summary.EntityCount);
        Assert.Equal(3, result.Summary.SupportedEntityCount);
        Assert.Equal(1, result.Summary.UnsupportedEntityCount);
        Assert.Equal(new SpaceCadBoundsV1(0, 0, 3_000, 3_000), result.Summary.Bounds);
        Assert.Matches("^[0-9a-f]{64}$", result.InventorySha256);

        var empty = Assert.Single(result.Layers, layer => layer.LayerId == "0");
        Assert.Equal(0, empty.EntityCount);
        Assert.Equal("ACI:7", empty.Color);
        Assert.Equal("CONTINUOUS", empty.LineType);
        Assert.True(empty.IsVisible);

        var hidden = Assert.Single(result.Layers, layer => layer.LayerId == "HIDDEN");
        Assert.False(hidden.IsVisible);
        Assert.Equal(1, hidden.UnsupportedEntityCount);
        Assert.Equal(1, hidden.EntityTypeCounts["Spline"]);

        var rackLayer = Assert.Single(result.Layers, layer => layer.LayerId == "RACK");
        Assert.Equal(2, rackLayer.BlockReferenceCount);
        Assert.Equal(2, rackLayer.AttributedEntityCount);
        Assert.Equal(new SpaceCadBoundsV1(1_000, 1_000, 2_000, 2_000), rackLayer.Bounds);

        var block = Assert.Single(result.Blocks);
        Assert.True(block.IsDefined);
        Assert.Equal(2, block.ReferenceCount);
        Assert.Equal(2, block.AttributedReferenceCount);
        Assert.Equal(2, block.DefinitionEntityCount);
        var code = Assert.Single(block.Attributes, attribute => attribute.Name == "CODE");
        Assert.Equal(2, code.ReferenceCount);
        Assert.Equal(2, code.DistinctValueCount);
        Assert.Equal(2, result.BlockReferences.Count);
    }

    [Fact]
    public void Build_is_deterministic_for_the_same_prepared_package()
    {
        var (request, preparation) = PreparedPackage();

        var first = SpaceCadInventory.Build(request, preparation);
        var second = SpaceCadInventory.Build(request, preparation);

        Assert.Equal(first.InventorySha256, second.InventorySha256);
        Assert.Equal(SpaceCadInventory.Serialize(first), SpaceCadInventory.Serialize(second));
    }

    [Fact]
    public void Build_rejects_a_preparation_that_is_not_ready()
    {
        var (request, preparation) = PreparedPackage();

        Assert.Throws<InvalidDataException>(
            () => SpaceCadInventory.Build(
                request,
                preparation with { ReadyForParsing = false }));
    }

    [Fact]
    public void Build_rejects_tampered_coordinate_metadata()
    {
        var (request, preparation) = PreparedPackage();
        preparation = preparation with
        {
            Metadata = preparation.Metadata with { RotationZDegrees = 45 },
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadInventory.Build(request, preparation));
    }

    [Fact]
    public void Serialize_rejects_inventory_content_tampering()
    {
        var (request, preparation) = PreparedPackage();
        var inventory = SpaceCadInventory.Build(request, preparation);

        Assert.Throws<InvalidDataException>(
            () => SpaceCadInventory.Serialize(inventory with { FloorCode = "F02" }));
    }

    [Fact]
    public void Layer_query_filters_search_visibility_type_empty_layers_and_pages()
    {
        var inventory = Inventory();

        var visible = SpaceCadInventory.QueryLayers(
            inventory,
            new SpaceCadLayerInventoryQueryV1(IsVisible: true, IncludeEmpty: false));
        var block = SpaceCadInventory.QueryLayers(
            inventory,
            new SpaceCadLayerInventoryQueryV1(EntityType: SpaceCadIrEntityType.BlockReference));
        var page = SpaceCadInventory.QueryLayers(
            inventory,
            new SpaceCadLayerInventoryQueryV1(IncludeEmpty: false, Offset: 1, Limit: 1));

        Assert.Equal(2, visible.TotalCount);
        Assert.DoesNotContain(visible.Items, layer => layer.LayerId == "0");
        Assert.Equal("RACK", Assert.Single(block.Items).LayerId);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal("RACK", Assert.Single(page.Items).LayerId);
    }

    [Fact]
    public void Block_query_filters_by_name_external_state_and_attribute()
    {
        var inventory = Inventory();

        var result = SpaceCadInventory.QueryBlocks(
            inventory,
            new SpaceCadBlockInventoryQueryV1(
                Search: "rack",
                IsExternalReference: false,
                AttributeName: "code"));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("RACK", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void Block_reference_query_filters_by_layer_block_and_attribute_value()
    {
        var inventory = Inventory();

        var result = SpaceCadInventory.QueryBlockReferences(
            inventory,
            new SpaceCadBlockReferenceInventoryQueryV1(
                LayerId: "rack",
                BlockName: "rack",
                AttributeName: "code",
                AttributeValue: "R-002"));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("H:102", Assert.Single(result.Items).SourceRef);
    }

    [Fact]
    public void Queries_reject_unbounded_pages_and_value_only_attribute_filters()
    {
        var inventory = Inventory();

        Assert.Throws<ArgumentOutOfRangeException>(() => SpaceCadInventory.QueryLayers(
            inventory,
            new SpaceCadLayerInventoryQueryV1(
                Limit: SpaceCadInventoryVersions.MaximumPageSize + 1)));
        Assert.Throws<ArgumentException>(() => SpaceCadInventory.QueryBlockReferences(
            inventory,
            new SpaceCadBlockReferenceInventoryQueryV1(AttributeValue: "R-001")));
    }

    private static SpaceCadInventoryV1 Inventory()
    {
        var (request, preparation) = PreparedPackage();
        return SpaceCadInventory.Build(request, preparation);
    }

    private static (SpaceCadConversionRequest Request, SpaceCadCoordinatePreparationV1 Preparation)
        PreparedPackage()
    {
        var request = new SpaceCadConversionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new string('a', 64),
            SpaceCadSourceFormat.Dxf,
            "test-converter",
            "1.0.0");
        var entities = new SpaceCadIrEntityV1[]
        {
            Entity(
                "H:100",
                SpaceCadIrEntityType.Line,
                "LINE",
                "WALL",
                [new SpaceCadPointV1(0, 0), new SpaceCadPointV1(3_000, 0)],
                new SpaceCadBoundsV1(0, 0, 3_000, 0)),
            Entity(
                "H:101",
                SpaceCadIrEntityType.BlockReference,
                "INSERT",
                "RACK",
                [new SpaceCadPointV1(1_000, 1_000)],
                new SpaceCadBoundsV1(1_000, 1_000, 1_000, 1_000),
                blockName: "RACK",
                attributes: new Dictionary<string, string> { ["CODE"] = "R-001" }),
            Entity(
                "H:102",
                SpaceCadIrEntityType.BlockReference,
                "INSERT",
                "RACK",
                [new SpaceCadPointV1(2_000, 2_000)],
                new SpaceCadBoundsV1(2_000, 2_000, 2_000, 2_000),
                blockName: "RACK",
                attributes: new Dictionary<string, string> { ["CODE"] = "R-002" }),
            Entity(
                "H:103",
                SpaceCadIrEntityType.Spline,
                "SPLINE",
                "HIDDEN",
                [new SpaceCadPointV1(3_000, 3_000)],
                new SpaceCadBoundsV1(3_000, 3_000, 3_000, 3_000),
                supported: false),
        };
        var bounds = new SpaceCadBoundsV1(0, 0, 3_000, 3_000);
        var package = new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                request.SourceSha256,
                SpaceCadSourceFormat.Dxf,
                "AC1032",
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadIrVersions.CoordinateSystem,
                bounds,
                request.ConverterId,
                request.ConverterVersion),
            [
                new SpaceCadIrLayerV1("0", "0", 0, "ACI:7", "CONTINUOUS"),
                new SpaceCadIrLayerV1("HIDDEN", "HIDDEN", 1, "ACI:1", "DASHED", false),
                new SpaceCadIrLayerV1("RACK", "RACK", 2, "ACI:3", "CONTINUOUS"),
                new SpaceCadIrLayerV1("WALL", "WALL", 1, "ACI:7", "CONTINUOUS"),
            ],
            [new SpaceCadIrBlockV1("H:B01", "RACK", false, null, 2)],
            entities,
            [new SpaceCadConversionIssueV1(
                "SPACE_CAD_ENTITY_UNSUPPORTED",
                SpaceCadIssueSeverity.Warning,
                "H:103",
                "SPLINE")],
            new SpaceCadIrSummaryV1(4, 1, 4, 3, 1, 0, bounds));
        var confirmation = new SpaceCadCoordinateConfirmationV1(
            request.SourceSha256,
            UnitConfirmed: true,
            SpaceCadUnit.Millimeter,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            RotationZDegrees: 0,
            new SpaceCadFloorAssignmentV1(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "F01",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(-10_000, -10_000, 10_000, 10_000)));
        return (request, SpaceCadCoordinatePreparation.Prepare(request, package, confirmation));
    }

    private static SpaceCadIrEntityV1 Entity(
        string sourceRef,
        SpaceCadIrEntityType type,
        string rawType,
        string layerId,
        IReadOnlyList<SpaceCadPointV1> points,
        SpaceCadBoundsV1 bounds,
        string? blockName = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        bool supported = true) =>
        new(
            sourceRef,
            type,
            rawType,
            layerId,
            blockName,
            points,
            Radius: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            IsClosed: false,
            supported,
            attributes ?? new Dictionary<string, string>());
}
