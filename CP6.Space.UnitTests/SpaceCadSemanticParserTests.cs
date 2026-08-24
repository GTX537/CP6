using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadSemanticParserTests
{
    private static readonly Guid TenantId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Parse_emits_read_only_unified_proposals_for_standard_semantics()
    {
        var scenario = Scenario();

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        Assert.True(preview.IsReadOnlyPreview);
        Assert.True(preview.ReadyForConfirmation);
        Assert.Equal(10, preview.Items.Count);
        Assert.Equal(10, preview.Items.Select(item => item.PreviewObjectId).Distinct().Count());
        Assert.Equal(10, preview.Items.Select(item => item.Source.SourceRef).Distinct().Count());
        Assert.All(
            new[]
            {
                SpaceCadSemanticTarget.Wall,
                SpaceCadSemanticTarget.Column,
                SpaceCadSemanticTarget.Door,
                SpaceCadSemanticTarget.Dock,
                SpaceCadSemanticTarget.Zone,
                SpaceCadSemanticTarget.Aisle,
                SpaceCadSemanticTarget.Rack,
            },
            target => Assert.Contains(preview.Items, item => item.Target == target));

        Assert.Equal(
            SpaceCadSemanticDraftObjectKind.Zone,
            preview.Items.Single(item => item.Target == SpaceCadSemanticTarget.Zone)
                .DraftObjectKind);
        Assert.Equal(
            SpaceCadSemanticDraftObjectKind.Aisle,
            preview.Items.Single(item => item.Target == SpaceCadSemanticTarget.Aisle)
                .DraftObjectKind);
        Assert.All(
            preview.Items.Where(item => item.Target == SpaceCadSemanticTarget.Rack),
            item => Assert.Equal(SpaceCadSemanticDraftObjectKind.Rack, item.DraftObjectKind));
        Assert.Matches("^[0-9a-f]{64}$", preview.SemanticPreviewSha256);
    }

    [Fact]
    public void Parse_applies_confidence_bands_and_rejects_without_silent_geometry_loss()
    {
        var scenario = Scenario();

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        var wall = Item(preview, "H:100");
        Assert.Equal(SpaceCadSemanticDisposition.AutoAccepted, wall.Disposition);
        Assert.True(wall.IsConfirmable);
        Assert.True(wall.IsSelected);
        Assert.Equal(SpaceCadSemanticGeometryKind.Path, wall.Geometry!.Kind);
        Assert.Equal(3_000, wall.AppliedMapping.DefaultHeightMillimeters);
        Assert.Equal(200, wall.AppliedMapping.DefaultThicknessMillimeters);

        var door = Item(preview, "H:120");
        Assert.Equal(SpaceCadSemanticDisposition.Candidate, door.Disposition);
        Assert.True(door.IsConfirmable);
        Assert.False(door.IsSelected);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceRef == door.Source.SourceRef
                     && issue.Code == "SPACE_CAD_SEMANTIC_CONFIDENCE_REVIEW");

        var aisle = Item(preview, "H:150");
        Assert.Equal(SpaceCadSemanticDisposition.Candidate, aisle.Disposition);
        Assert.False(aisle.IsConfirmable);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceRef == aisle.Source.SourceRef
                     && issue.Code == "SPACE_CAD_SEMANTIC_CANDIDATE_ONLY");

        var rack = Item(preview, "H:160");
        Assert.Equal(SpaceCadSemanticGeometryKind.BlockInstance, rack.Geometry!.Kind);
        Assert.Equal(0.69m, rack.Confidence);
        Assert.False(rack.IsConfirmable);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceRef == rack.Source.SourceRef
                     && issue.Code == "SPACE_CAD_SEMANTIC_BLOCK_FOOTPRINT_UNAVAILABLE");

        var unsupported = Item(preview, "H:170");
        Assert.Equal(SpaceCadSemanticDisposition.Rejected, unsupported.Disposition);
        Assert.Null(unsupported.Geometry);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceRef == unsupported.Source.SourceRef
                     && issue.Code == "SPACE_CAD_SEMANTIC_ENTITY_UNSUPPORTED");

        var zeroLength = Item(preview, "H:171");
        Assert.Equal(SpaceCadSemanticDisposition.Rejected, zeroLength.Disposition);
        Assert.Null(zeroLength.Geometry);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceRef == zeroLength.Source.SourceRef
                     && issue.Code == "SPACE_CAD_SEMANTIC_ZERO_SIZE"
                     && issue.DetailToken == "path-requires-two-distinct-points");
    }

    [Fact]
    public void Parse_reports_unclosed_boundaries_and_each_overlapping_area_object()
    {
        var scenario = Scenario(
            addOverlappingZone: true,
            addUnclosedZone: true);

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        var unclosed = Assert.Single(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_SEMANTIC_BOUNDARY_UNCLOSED");
        Assert.Equal("H:143", unclosed.SourceRef);
        Assert.Equal(
            "closed-boundary-requires-closed-polyline-or-circle",
            unclosed.DetailToken);

        var overlaps = preview.Issues
            .Where(issue => issue.Code ==
                "SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP")
            .ToArray();
        Assert.Equal(2, overlaps.Length);
        Assert.Equal(
            ["H:140", "H:142"],
            overlaps.Select(issue => issue.SourceRef).Order(StringComparer.Ordinal));
        Assert.All(overlaps, issue => Assert.StartsWith("overlaps:cad-preview-", issue.DetailToken));
    }

    [Fact]
    public void Parse_does_not_report_overlap_for_boundary_contact_or_different_targets()
    {
        var scenario = Scenario(addTouchingZone: true);

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        Assert.DoesNotContain(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP");
    }

    [Fact]
    public void Parse_reports_overlap_for_coincident_polygon_with_reversed_winding()
    {
        var scenario = Scenario(addCoincidentReversedZone: true);

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        Assert.Equal(
            ["H:140", "H:145"],
            preview.Issues
                .Where(issue => issue.Code ==
                    "SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP")
                .Select(issue => issue.SourceRef)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Block_rule_is_evaluated_per_reference_and_overrides_layer_without_duplicates()
    {
        var scenario = Scenario(conditionalBlockRule: true);

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        var matched = Item(preview, "H:160");
        Assert.Equal(SpaceCadSemanticTarget.Rack, matched.Target);
        Assert.Equal(SpaceCadMappingSourceKind.Block, matched.AppliedMapping.SourceKind);
        Assert.Equal("B-RACK", matched.AppliedMapping.RuleId);

        var fallback = Item(preview, "H:161");
        Assert.Equal(SpaceCadSemanticTarget.Guide, fallback.Target);
        Assert.Equal(SpaceCadMappingSourceKind.Layer, fallback.AppliedMapping.SourceKind);
        Assert.Equal("L-RACK-FALLBACK", fallback.AppliedMapping.RuleId);
        Assert.Equal(2, preview.Items.Count(item => item.Source.BlockName == "RACK_UNIT"));
    }

    [Fact]
    public void Parse_is_deterministic_and_hash_validation_rejects_tampering()
    {
        var scenario = Scenario();

        var first = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);
        var second = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        Assert.Equal(first.SemanticPreviewSha256, second.SemanticPreviewSha256);
        Assert.Equal(
            SpaceCadSemanticParser.Serialize(first),
            SpaceCadSemanticParser.Serialize(second));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticParser.Validate(
            first with { ReadyForConfirmation = false }));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticParser.Validate(
            first with
            {
                Items = first.Items.Select((item, index) => index == 0
                        ? item with { Confidence = 0.91m }
                        : item)
                    .ToArray(),
            }));
    }

    [Fact]
    public void Parse_fails_closed_when_chain_artifacts_do_not_match()
    {
        var scenario = Scenario();
        var otherProfile = SpaceCadMapping.Seal(ProfileDraft(
            conditionalBlockRule: false,
            requiredUnsupported: false,
            name: "Different sealed definition"));

        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory with { FloorCode = "F02" },
            scenario.Profile,
            scenario.MappingPreview));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            otherProfile,
            scenario.MappingPreview));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation with
            {
                Metadata = scenario.Preparation.Metadata with { RotationZDegrees = 45 },
            },
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview));
        Assert.Throws<UnauthorizedAccessException>(() => SpaceCadSemanticParser.Parse(
            scenario.Request with
            {
                TenantId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            },
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview));
    }

    [Fact]
    public void Required_mapping_with_only_rejected_geometry_blocks_confirmation()
    {
        var scenario = Scenario(requiredUnsupported: true);

        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        Assert.False(preview.ReadyForConfirmation);
        Assert.Equal(1, preview.Summary.BlockingCount);
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_SEMANTIC_REQUIRED_SOURCE_REJECTED"
                     && issue.RuleId == "L-BAD");
    }

    private static SpaceCadSemanticPreviewItemV1 Item(
        SpaceCadSemanticPreviewV1 preview,
        string sourceRef) => preview.Items.Single(item => item.Source.SourceRef == sourceRef);

    internal static SemanticScenario Scenario(
        bool conditionalBlockRule = false,
        bool requiredUnsupported = false,
        bool addDistantZone = false,
        bool addOverlappingZone = false,
        bool useConcaveZone = false,
        bool addUnclosedZone = false,
        bool addTouchingZone = false,
        bool addCoincidentReversedZone = false)
    {
        var request = new SpaceCadConversionRequest(
            TenantId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new string('b', 64),
            SpaceCadSourceFormat.Dxf,
            "semantic-test-converter",
            "1.0.0");
        var primaryZonePoints = useConcaveZone
            ? new SpaceCadPointV1[]
            {
                new(0, 0),
                new(8_000, 0),
                new(8_000, 8_000),
                new(4_500, 8_000),
                new(4_500, 4_000),
                new(3_500, 4_000),
                new(3_500, 8_000),
                new(0, 8_000),
            }
            : Rectangle(0, 0, 8_000, 8_000);
        var entities = new SpaceCadIrEntityV1[]
        {
            Entity(
                "H:100", SpaceCadIrEntityType.Line, "LINE", "WALL",
                [new(0, 0), new(5_000, 0)],
                new(0, 0, 5_000, 0)),
            Entity(
                "H:110", SpaceCadIrEntityType.Circle, "CIRCLE", "COLUMN",
                [new(1_000, 1_000)],
                new(750, 750, 1_250, 1_250),
                radius: 250),
            Entity(
                "H:120", SpaceCadIrEntityType.Line, "LINE", "DOOR",
                [new(2_000, 0), new(3_000, 0)],
                new(2_000, 0, 3_000, 0)),
            Entity(
                "H:130", SpaceCadIrEntityType.ClosedPolyline, "LWPOLYLINE", "DOCK",
                Rectangle(0, 2_000, 2_000, 4_000),
                new(0, 2_000, 2_000, 4_000),
                isClosed: true),
            Entity(
                "H:140", SpaceCadIrEntityType.ClosedPolyline, "LWPOLYLINE", "ZONE",
                primaryZonePoints,
                new(0, 0, 8_000, 8_000),
                isClosed: true),
            Entity(
                "H:150", SpaceCadIrEntityType.Line, "LINE", "AISLE",
                [new(1_000, 5_000), new(7_000, 5_000)],
                new(1_000, 5_000, 7_000, 5_000)),
            Entity(
                "H:160", SpaceCadIrEntityType.BlockReference, "INSERT", "RACK",
                [new(3_000, 3_000)],
                new(3_000, 3_000, 3_000, 3_000),
                blockName: "RACK_UNIT",
                attributes: new Dictionary<string, string> { ["CODE"] = "R-001" }),
            Entity(
                "H:161", SpaceCadIrEntityType.BlockReference, "INSERT", "RACK",
                [new(4_000, 3_000)],
                new(4_000, 3_000, 4_000, 3_000),
                blockName: "RACK_UNIT",
                attributes: new Dictionary<string, string> { ["CODE"] = "R-999" }),
            Entity(
                "H:170", SpaceCadIrEntityType.Spline, "SPLINE", "BAD",
                [new(9_000, 9_000)],
                new(9_000, 9_000, 9_000, 9_000),
                supported: false),
            Entity(
                "H:171", SpaceCadIrEntityType.Line, "LINE", "BAD_GEOMETRY",
                [new(9_000, 8_000), new(9_000, 8_000)],
                new(9_000, 8_000, 9_000, 8_000)),
        };
        if (addDistantZone)
        {
            entities =
            [
                .. entities,
                Entity(
                    "H:142", SpaceCadIrEntityType.ClosedPolyline,
                    "LWPOLYLINE", "ZONE",
                    Rectangle(8_200, 0, 8_800, 1_000),
                    new(8_200, 0, 8_800, 1_000),
                    isClosed: true),
            ];
        }
        if (addOverlappingZone)
        {
            entities =
            [
                .. entities,
                Entity(
                    "H:142", SpaceCadIrEntityType.ClosedPolyline,
                    "LWPOLYLINE", "ZONE",
                    Rectangle(500, 500, 7_500, 7_500),
                    new(500, 500, 7_500, 7_500),
                    isClosed: true),
            ];
        }
        if (addUnclosedZone)
        {
            entities =
            [
                .. entities,
                Entity(
                    "H:143", SpaceCadIrEntityType.Polyline,
                    "LWPOLYLINE", "ZONE",
                    Rectangle(9_100, 0, 9_800, 1_000),
                    new(9_100, 0, 9_800, 1_000),
                    isClosed: false),
            ];
        }
        if (addTouchingZone)
        {
            entities =
            [
                .. entities,
                Entity(
                    "H:144", SpaceCadIrEntityType.ClosedPolyline,
                    "LWPOLYLINE", "ZONE",
                    Rectangle(8_000, 0, 8_800, 1_000),
                    new(8_000, 0, 8_800, 1_000),
                    isClosed: true),
            ];
        }
        if (addCoincidentReversedZone)
        {
            entities =
            [
                .. entities,
                Entity(
                    "H:145", SpaceCadIrEntityType.ClosedPolyline,
                    "LWPOLYLINE", "ZONE",
                    primaryZonePoints.Reverse().ToArray(),
                    new(0, 0, 8_000, 8_000),
                    isClosed: true),
            ];
        }
        var layerNames = new[]
        {
            "AISLE", "BAD", "BAD_GEOMETRY", "COLUMN", "DOCK", "DOOR", "RACK", "WALL", "ZONE",
        };
        var layers = layerNames.Select(name => new SpaceCadIrLayerV1(
            name,
            name,
            entities.LongCount(entity => entity.LayerId == name),
            "ACI:7",
            "CONTINUOUS")).ToArray();
        var bounds = new SpaceCadBoundsV1(0, 0, 9_000, 9_000);
        var package = new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                request.SourceSha256,
                request.SourceFormat,
                "AC1032",
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadIrVersions.CoordinateSystem,
                bounds,
                request.ConverterId,
                request.ConverterVersion),
            layers,
            [new SpaceCadIrBlockV1("H:B01", "RACK_UNIT", false, null, 4)],
            entities,
            [new SpaceCadConversionIssueV1(
                "SPACE_CAD_ENTITY_UNSUPPORTED",
                SpaceCadIssueSeverity.Warning,
                "H:170",
                "SPLINE")],
            new SpaceCadIrSummaryV1(
                layers.LongLength,
                1,
                entities.LongLength,
                entities.LongCount(entity => entity.IsSupported),
                entities.LongCount(entity => !entity.IsSupported),
                0,
                bounds));
        var preparation = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            new SpaceCadCoordinateConfirmationV1(
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
                    new SpaceCadBoundsV1(-10_000, -10_000, 20_000, 20_000))));
        var inventory = SpaceCadInventory.Build(request, preparation);
        var profile = SpaceCadMapping.Seal(ProfileDraft(
            conditionalBlockRule,
            requiredUnsupported));
        var mappingPreview = SpaceCadMapping.Preview(TenantId, inventory, profile);
        Assert.True(mappingPreview.ReadyForSemanticParsing);
        return new SemanticScenario(request, preparation, inventory, profile, mappingPreview);
    }

    private static SpaceCadMappingProfileDraftV1 ProfileDraft(
        bool conditionalBlockRule,
        bool requiredUnsupported,
        string name = "Semantic test mapping") =>
        new(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            Version: 1,
            name,
            SpaceCadMappingScope.System,
            TenantId: null,
            IsEnabled: true,
            BasedOnProfileId: null,
            BasedOnVersion: null,
            [
                Rule(
                    "B-RACK", SpaceCadMappingSourceKind.Block, "RACK_UNIT",
                    SpaceCadSemanticTarget.Rack, SpaceCadGeometryRule.BlockFootprint, 0.95m)
                with
                {
                    AttributeName = conditionalBlockRule ? "CODE" : null,
                    AttributeMatchKind = conditionalBlockRule
                        ? SpaceCadMappingMatchKind.Exact
                        : null,
                    AttributePattern = conditionalBlockRule ? "R-001" : null,
                },
                Rule(
                    "L-AISLE", SpaceCadMappingSourceKind.Layer, "AISLE",
                    SpaceCadSemanticTarget.Aisle, SpaceCadGeometryRule.DirectGeometry, 0.65m),
                Rule(
                    "L-BAD", SpaceCadMappingSourceKind.Layer, "BAD",
                    SpaceCadSemanticTarget.Guide, SpaceCadGeometryRule.DirectGeometry, 0.95m,
                    required: requiredUnsupported),
                Rule(
                    "L-BAD-GEOMETRY", SpaceCadMappingSourceKind.Layer, "BAD_GEOMETRY",
                    SpaceCadSemanticTarget.Wall, SpaceCadGeometryRule.Centerline, 0.95m),
                Rule(
                    "L-COLUMN", SpaceCadMappingSourceKind.Layer, "COLUMN",
                    SpaceCadSemanticTarget.Column, SpaceCadGeometryRule.DirectGeometry, 0.95m),
                Rule(
                    "L-DOCK", SpaceCadMappingSourceKind.Layer, "DOCK",
                    SpaceCadSemanticTarget.Dock, SpaceCadGeometryRule.ClosedBoundary, 0.95m),
                Rule(
                    "L-DOOR", SpaceCadMappingSourceKind.Layer, "DOOR",
                    SpaceCadSemanticTarget.Door, SpaceCadGeometryRule.DirectGeometry, 0.80m),
                Rule(
                    "L-RACK-FALLBACK", SpaceCadMappingSourceKind.Layer, "RACK",
                    SpaceCadSemanticTarget.Guide, SpaceCadGeometryRule.DirectGeometry, 0.95m),
                Rule(
                    "L-WALL", SpaceCadMappingSourceKind.Layer, "WALL",
                    SpaceCadSemanticTarget.Wall, SpaceCadGeometryRule.Centerline, 0.95m,
                    required: true, height: 3_000, thickness: 200),
                Rule(
                    "L-ZONE", SpaceCadMappingSourceKind.Layer, "ZONE",
                    SpaceCadSemanticTarget.Zone, SpaceCadGeometryRule.ClosedBoundary, 0.90m),
            ]);

    private static SpaceCadMappingRuleV1 Rule(
        string ruleId,
        SpaceCadMappingSourceKind sourceKind,
        string pattern,
        SpaceCadSemanticTarget target,
        SpaceCadGeometryRule geometryRule,
        decimal confidence,
        bool required = false,
        decimal? height = null,
        decimal? thickness = null) =>
        new(
            ruleId,
            100,
            sourceKind,
            SpaceCadMappingMatchKind.Exact,
            pattern,
            AttributeName: null,
            AttributeMatchKind: null,
            AttributePattern: null,
            target,
            TargetSubtype: null,
            geometryRule,
            height,
            thickness,
            confidence,
            required);

    private static SpaceCadIrEntityV1 Entity(
        string sourceRef,
        SpaceCadIrEntityType type,
        string rawType,
        string layerId,
        IReadOnlyList<SpaceCadPointV1> points,
        SpaceCadBoundsV1 bounds,
        decimal? radius = null,
        bool isClosed = false,
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
            radius,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            isClosed,
            supported,
            attributes ?? new Dictionary<string, string>());

    private static SpaceCadPointV1[] Rectangle(
        decimal minX,
        decimal minY,
        decimal maxX,
        decimal maxY) =>
    [
        new(minX, minY),
        new(maxX, minY),
        new(maxX, maxY),
        new(minX, maxY),
    ];

    internal sealed record SemanticScenario(
        SpaceCadConversionRequest Request,
        SpaceCadCoordinatePreparationV1 Preparation,
        SpaceCadInventoryV1 Inventory,
        SpaceCadMappingProfileV1 Profile,
        SpaceCadMappingPreviewV1 MappingPreview);
}
