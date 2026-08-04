using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadSemanticDiagnosticTests
{
    [Fact]
    public void Build_indexes_source_rule_confidence_and_canvas_location_for_every_proposal()
    {
        var (scenario, semantic, index) = Index();

        Assert.True(index.IsReadOnlyIndex);
        Assert.Equal(scenario.Request.TenantId, index.TenantId);
        Assert.Equal(10, index.Evidence.Count);
        Assert.Equal(4, index.Summary.HighConfidenceCount);
        Assert.Equal(1, index.Summary.ReviewConfidenceCount);
        Assert.Equal(3, index.Summary.LowConfidenceCount);
        Assert.Equal(2, index.Summary.RejectedCount);
        Assert.Equal(semantic.Issues.Count, index.Summary.SemanticDiagnosticCount);
        Assert.Equal(0, index.Summary.MappingDiagnosticCount);
        Assert.Equal(index.Diagnostics.Count, index.Summary.LocatableDiagnosticCount);
        Assert.Equal(0, index.Summary.UnlocatableDiagnosticCount);

        var wall = index.Evidence.Single(item => item.SourceRef == "H:100");
        Assert.Equal("L-WALL", wall.RuleId);
        Assert.Equal(0.95m, wall.Confidence);
        Assert.Equal(SpaceCadConfidenceBand.High, wall.ConfidenceBand);
        Assert.Equal(SpaceCadDiagnosticLocationKind.Entity, wall.Location.Kind);
        Assert.Equal("WALL", wall.Location.LayerId);
        Assert.True(wall.Location.CanFocusCanvas);
        Assert.Equal(new SpaceCadMillimeterPointV1(2_500, 0), wall.Location.Anchor);
        Assert.Matches("^[0-9a-f]{64}$", wall.EvidenceSha256);
        Assert.Matches("^[0-9a-f]{64}$", index.DiagnosticIndexSha256);
    }

    [Fact]
    public void Mapping_and_semantic_diagnostics_resolve_to_layer_and_entity_locations()
    {
        var scenario = SpaceCadSemanticParserTests.Scenario();
        var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
            scenario.Profile.SchemaVersion,
            scenario.Profile.ProfileId,
            scenario.Profile.Version,
            "Mapping with one intentionally unmapped layer",
            scenario.Profile.Scope,
            scenario.Profile.TenantId,
            scenario.Profile.IsEnabled,
            scenario.Profile.BasedOnProfileId,
            scenario.Profile.BasedOnVersion,
            scenario.Profile.Rules
                .Where(rule => rule.RuleId != "L-BAD-GEOMETRY")
                .ToArray()));
        var mapping = SpaceCadMapping.Preview(
            scenario.Request.TenantId,
            scenario.Inventory,
            profile);
        var semantic = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            profile,
            mapping);

        var index = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            profile,
            mapping,
            semantic);

        var unmapped = Assert.Single(
            index.Diagnostics,
            item => item.Origin == SpaceCadDiagnosticOrigin.Mapping
                    && item.Code == "SPACE_CAD_MAPPING_SOURCE_UNMAPPED"
                    && item.SourceKey == "BAD_GEOMETRY");
        Assert.Equal(SpaceCadDiagnosticRecovery.MapSource, unmapped.Recovery);
        Assert.Equal(SpaceCadDiagnosticLocationKind.Layer, unmapped.Location.Kind);
        Assert.Equal("BAD_GEOMETRY", unmapped.Location.LayerId);
        Assert.True(unmapped.Location.CanFocusCanvas);
        Assert.Equal(new SpaceCadMillimeterPointV1(9_000, 8_000), unmapped.Location.Anchor);

        var unsupported = Assert.Single(
            index.Diagnostics,
            item => item.Code == "SPACE_CAD_SEMANTIC_ENTITY_UNSUPPORTED");
        Assert.Equal(SpaceCadDiagnosticLocationKind.Entity, unsupported.Location.Kind);
        Assert.Equal("H:170", unsupported.Location.SourceRef);
        Assert.Equal(SpaceCadDiagnosticRecovery.InspectGeometry, unsupported.Recovery);
        Assert.True(unsupported.Location.CanFocusCanvas);
    }

    [Fact]
    public void Queries_filter_confidence_issue_origin_severity_location_and_page_bounds()
    {
        var (_, _, index) = Index();

        var low = SpaceCadSemanticDiagnostics.QueryEvidence(
            index,
            new SpaceCadSemanticEvidenceQueryV1(
                ConfidenceBand: SpaceCadConfidenceBand.Low,
                OnlyWithDiagnostics: true));
        var rackWarnings = SpaceCadSemanticDiagnostics.QueryDiagnostics(
            index,
            new SpaceCadSemanticDiagnosticQueryV1(
                Severity: SpaceCadIssueSeverity.Warning,
                Origin: SpaceCadDiagnosticOrigin.Semantic,
                LayerId: "rack",
                OnlyLocatable: true));
        var source = SpaceCadSemanticDiagnostics.QueryDiagnostics(
            index,
            new SpaceCadSemanticDiagnosticQueryV1(SourceRef: "H:171"));

        Assert.Equal(3, low.TotalCount);
        Assert.All(low.Items, item => Assert.Equal(SpaceCadConfidenceBand.Low, item.ConfidenceBand));
        Assert.Equal(2, rackWarnings.TotalCount);
        Assert.All(
            rackWarnings.Items,
            item => Assert.Equal(
                "SPACE_CAD_SEMANTIC_BLOCK_FOOTPRINT_UNAVAILABLE",
                item.Code));
        Assert.Equal(
            "SPACE_CAD_SEMANTIC_GEOMETRY_REJECTED",
            Assert.Single(source.Items).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpaceCadSemanticDiagnostics.QueryDiagnostics(
                index,
                new SpaceCadSemanticDiagnosticQueryV1(
                    Limit: SpaceCadSemanticDiagnosticVersions.MaximumPageSize + 1)));
    }

    [Fact]
    public void Build_and_serialization_are_deterministic()
    {
        var scenario = SpaceCadSemanticParserTests.Scenario();
        var semantic = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);

        var first = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview,
            semantic);
        var second = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview,
            semantic);

        Assert.Equal(first.DiagnosticIndexSha256, second.DiagnosticIndexSha256);
        Assert.Equal(
            SpaceCadSemanticDiagnostics.Serialize(first),
            SpaceCadSemanticDiagnostics.Serialize(second));
        Assert.Equal(
            first.Diagnostics.Select(item => item.DiagnosticId),
            second.Diagnostics.Select(item => item.DiagnosticId));
    }

    [Fact]
    public void Validation_and_chain_checks_reject_location_content_and_source_tampering()
    {
        var (scenario, semantic, index) = Index();
        var firstEvidence = index.Evidence[0];
        var tamperedLocation = firstEvidence.Location with
        {
            Anchor = new SpaceCadMillimeterPointV1(123, 456),
        };
        var tamperedEvidence = firstEvidence with { Location = tamperedLocation };

        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticDiagnostics.Validate(
            index with
            {
                Evidence = index.Evidence.Select((item, position) => position == 0
                        ? tamperedEvidence
                        : item)
                    .ToArray(),
            }));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticDiagnostics.Validate(
            index with { DiagnosticIndexSha256 = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() => SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview,
            semantic with { FloorCode = "F02" }));
    }

    [Fact]
    public void Empty_unmapped_layer_is_identified_without_inventing_canvas_bounds()
    {
        var scenario = SpaceCadSemanticParserTests.Scenario();
        var package = scenario.Preparation.Package;
        var preparation = scenario.Preparation with
        {
            Package = package with
            {
                Layers = package.Layers.Append(
                        new SpaceCadIrLayerV1(
                            "EMPTY",
                            "EMPTY",
                            EntityCount: 0,
                            "ACI:7",
                            "CONTINUOUS"))
                    .ToArray(),
                Summary = package.Summary with
                {
                    LayerCount = package.Summary.LayerCount + 1,
                },
            },
        };
        var inventory = SpaceCadInventory.Build(scenario.Request, preparation);
        var mapping = SpaceCadMapping.Preview(
            scenario.Request.TenantId,
            inventory,
            scenario.Profile);
        var semantic = SpaceCadSemanticParser.Parse(
            scenario.Request,
            preparation,
            inventory,
            scenario.Profile,
            mapping);

        var index = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            preparation,
            inventory,
            scenario.Profile,
            mapping,
            semantic);

        var diagnostic = Assert.Single(
            index.Diagnostics,
            item => item.Origin == SpaceCadDiagnosticOrigin.Mapping
                    && item.SourceKey == "EMPTY");
        Assert.Equal(SpaceCadDiagnosticLocationKind.Layer, diagnostic.Location.Kind);
        Assert.False(diagnostic.Location.CanFocusCanvas);
        Assert.Null(diagnostic.Location.Bounds);
        Assert.Null(diagnostic.Location.Anchor);
        Assert.Equal(0, diagnostic.Location.SuggestedPaddingMillimeters);
    }

    private static (
        SpaceCadSemanticParserTests.SemanticScenario Scenario,
        SpaceCadSemanticPreviewV1 Semantic,
        SpaceCadSemanticDiagnosticIndexV1 Index) Index()
    {
        var scenario = SpaceCadSemanticParserTests.Scenario();
        var semantic = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);
        var index = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview,
            semantic);
        return (scenario, semantic, index);
    }
}
