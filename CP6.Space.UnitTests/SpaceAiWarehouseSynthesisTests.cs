using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiWarehouseSynthesisTests
{
    private static readonly Guid SiteId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ModelVersionId =
        Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid RunId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly byte[] HmacKey = Enumerable.Range(1, 32)
        .Select(value => (byte)value)
        .ToArray();

    [Fact]
    public async Task Human_locked_rule_ai_default_priority_keeps_all_evidence()
    {
        var fixture = Fixture(
        [
            new("H:120", "type", "Door"),
        ]);
        var request = Request(
            fixture,
            [
                Suggestion(fixture, "H:120", WarehouseSpaceType.Wall, 0.98m),
            ],
            defaults:
            [
                new("H:120", "type", "Rack"),
            ]);
        var synthesizer = new WarehouseDraftSynthesizer();

        var first = await synthesizer.SynthesizeAsync(request);
        var repeat = await synthesizer.SynthesizeAsync(request);

        var door = Proposal(first, "H:120");
        var type = Assert.Single(door.Fields, field => field.FieldPath == "type");
        Assert.Equal("Door", type.ValueToken);
        Assert.Equal(WarehouseFusionSource.HumanLocked, type.WinningSource);
        Assert.Equal(
            [
                WarehouseFusionSource.HumanLocked,
                WarehouseFusionSource.DeterministicRule,
                WarehouseFusionSource.Ai,
                WarehouseFusionSource.TemplateDefault,
            ],
            type.Evidence.Select(item => item.Source).ToArray());
        Assert.Contains(first.Issues, issue =>
            issue.Code == "AI_LOCKED_VALUE_CONFLICT"
            && issue.SourceRef == "H:120"
            && issue.Severity == WarehouseProposalIssueSeverity.Info);
        Assert.DoesNotContain(first.Issues, issue =>
            issue.Code == "LOCKED_RULE_VALUE_CONFLICT"
            && issue.SourceRef == "H:120");
        Assert.Equal(0.80m, door.Confidence);
        Assert.Equal(WarehouseFusionConfidenceBand.Medium, door.ConfidenceBand);
        Assert.Equal(first.ProposalSetSha256, repeat.ProposalSetSha256);
        Assert.Equal(
            WarehouseDraftSynthesizer.Serialize(first),
            WarehouseDraftSynthesizer.Serialize(repeat));
        Assert.True(first.IsReadOnlyPreview);
        Assert.False(first.DraftWritten);
        Assert.False(first.Summary.ReadyForApply);
    }

    [Fact]
    public async Task Rule_only_snapshot_preserves_human_name_and_parent_relation_locks()
    {
        var baseFixture = Fixture();
        var empty = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            ModelVersionId,
            RunId,
            baseFixture.Preview);
        var zoneKey = empty.LocalSourceMap.Entries.Single(item =>
            item.SourceRefs.Contains("H:140", StringComparer.Ordinal)).SourceKey;
        SpaceAiCadLockedFactV1[] facts =
        [
            new("H:160", "attributes.name", "Locked Rack"),
            new("H:160", "relations.zoneSourceKey", zoneKey),
        ];
        var snapshot = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            ModelVersionId,
            RunId,
            baseFixture.Preview,
            facts);
        var output = new WarehouseGenerationResult(
            WarehouseGenerationInput.CurrentSchemaVersion,
            "rule-only-test-request-000000000001",
            "cp6-deterministic-rules",
            new WarehouseGenerationUsage(0, 0),
            [],
            []);
        var validated = new WarehouseGenerationOutputValidator().Validate(
            snapshot.ProviderInput,
            output);

        var result = await new WarehouseDraftSynthesizer().SynthesizeAsync(
            new WarehouseDraftSynthesisRequestV1(
                ModelVersionId,
                "rules-e02-s06-v1",
                snapshot,
                baseFixture.Preview,
                validated,
                facts,
                [],
                []));

        var rack = Proposal(result, "H:160");
        Assert.Contains(rack.Fields, field =>
            field.FieldPath == "attributes.name" &&
            field.ValueToken == "Locked Rack" &&
            field.WinningSource == WarehouseFusionSource.HumanLocked);
        Assert.Contains(rack.Fields, field =>
            field.FieldPath == "relations.zoneSourceKey" &&
            field.ValueToken == zoneKey &&
            field.WinningSource == WarehouseFusionSource.HumanLocked);
    }

    [Fact]
    public async Task Unique_zone_geometry_infers_parent_for_aisle_and_racks()
    {
        var fixture = Fixture();

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(Request(fixture, []));

        var zoneKey = fixture.SourceKey("H:140");
        foreach (var sourceRef in new[] { "H:150", "H:160", "H:161" })
        {
            var field = Assert.Single(
                Proposal(result, sourceRef).Fields,
                item => item.FieldPath == "relations.zoneSourceKey");
            Assert.Equal(zoneKey, field.ValueToken);
            Assert.Equal(
                WarehouseFusionSource.DeterministicRule,
                field.WinningSource);
            Assert.Contains(field.Evidence, evidence =>
                evidence.EvidenceCodes.Contains(
                    "RULE:ZONE_GEOMETRY_CONTAINMENT_V1",
                    StringComparer.Ordinal));
            Assert.DoesNotContain(result.Issues, issue =>
                issue.Code == SpaceErrorCodes.RuleOnlyParentRequired &&
                issue.SourceRef == sourceRef);
        }
    }

    [Fact]
    public async Task Ambiguous_zone_geometry_keeps_parent_blocking()
    {
        var fixture = Fixture(addOverlappingZone: true);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(Request(fixture, []));

        foreach (var sourceRef in new[] { "H:150", "H:160", "H:161" })
        {
            Assert.DoesNotContain(
                Proposal(result, sourceRef).Fields,
                item => item.FieldPath == "relations.zoneSourceKey");
            Assert.Contains(result.Issues, issue =>
                issue.Code == SpaceErrorCodes.RuleOnlyParentRequired &&
                issue.SourceRef == sourceRef &&
                issue.Severity == WarehouseProposalIssueSeverity.Blocking &&
                issue.DetailToken == "ambiguous-containing-zones");
        }
    }

    [Fact]
    public async Task Concave_zone_does_not_infer_for_path_that_leaves_boundary()
    {
        var fixture = Fixture(useConcaveZone: true);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(Request(fixture, []));

        Assert.DoesNotContain(
            Proposal(result, "H:150").Fields,
            item => item.FieldPath == "relations.zoneSourceKey");
        Assert.Contains(result.Issues, issue =>
            issue.Code == SpaceErrorCodes.RuleOnlyParentRequired &&
            issue.SourceRef == "H:150" &&
            issue.DetailToken == "no-containing-zone");
        Assert.Contains(
            Proposal(result, "H:160").Fields,
            item => item.FieldPath == "relations.zoneSourceKey" &&
                    item.ValueToken == fixture.SourceKey("H:140"));
    }

    [Fact]
    public async Task Deterministic_parent_beats_conflicting_ai_relation()
    {
        var fixture = Fixture(addDistantZone: true);
        var request = Request(
            fixture,
            [
                Suggestion(
                    fixture,
                    "H:160",
                    WarehouseSpaceType.Rack,
                    0.96m,
                    relations:
                    [
                        Relation(
                            fixture,
                            WarehouseRelationType.ContainedBy,
                            "H:142"),
                    ]),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        var rack = Proposal(result, "H:160");
        var field = Assert.Single(
            rack.Fields,
            item => item.FieldPath == "relations.zoneSourceKey");
        Assert.Equal(fixture.SourceKey("H:140"), field.ValueToken);
        Assert.Equal(WarehouseFusionSource.DeterministicRule, field.WinningSource);
        Assert.Empty(rack.Relations);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_RULE_VALUE_CONFLICT" &&
            issue.SourceRef == "H:160" &&
            issue.FieldPath == "relations.zoneSourceKey" &&
            issue.Severity == WarehouseProposalIssueSeverity.Warning &&
            issue.DetailToken == "confidence-downgraded");
    }

    [Fact]
    public async Task Legacy_rule_version_does_not_rewrite_frozen_parent_behavior()
    {
        var fixture = Fixture();
        var request = Request(fixture, []) with
        {
            RuleVersion = SpaceAiGenerationRunContract.LegacyRuleVersion,
        };

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        Assert.DoesNotContain(
            Proposal(result, "H:150").Fields,
            item => item.FieldPath == "relations.zoneSourceKey");
        Assert.DoesNotContain(
            Proposal(result, "H:160").Fields,
            item => item.FieldPath == "relations.zoneSourceKey");
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code == SpaceErrorCodes.RuleOnlyParentRequired);
    }

    [Fact]
    public async Task Soft_rule_type_conflict_retains_rule_and_downgrades_band()
    {
        var fixture = Fixture();
        var request = Request(
            fixture,
            [
                Suggestion(fixture, "H:100", WarehouseSpaceType.Column, 0.98m),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        var wall = Proposal(result, "H:100");
        var type = Assert.Single(wall.Fields, field => field.FieldPath == "type");
        Assert.Equal("Wall", type.ValueToken);
        Assert.Equal(WarehouseFusionSource.DeterministicRule, type.WinningSource);
        Assert.Equal(WarehouseFusionConfidenceBand.Medium, wall.ConfidenceBand);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_RULE_VALUE_CONFLICT"
            && issue.SourceRef == "H:100"
            && issue.Severity == WarehouseProposalIssueSeverity.Warning
            && issue.DetailToken == "confidence-downgraded");
        Assert.Equal(
            SpaceCadSemanticGeometryKind.Path,
            wall.Geometry.Kind);
        Assert.Equal(
            WarehouseProposalGeometrySource.CadIrDeterministicRule,
            wall.GeometrySource);
    }

    [Fact]
    public async Task Strong_rule_conflict_retains_high_confidence_geometry_and_type()
    {
        var fixture = Fixture(strongWallRule: true);
        var request = Request(
            fixture,
            [
                Suggestion(fixture, "H:100", WarehouseSpaceType.Column, 0.99m),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        var wall = Proposal(result, "H:100");
        Assert.Equal(WarehouseSpaceType.Wall, wall.ObjectType);
        Assert.Equal(1m, wall.Confidence);
        Assert.Equal(WarehouseFusionConfidenceBand.High, wall.ConfidenceBand);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_RULE_VALUE_CONFLICT"
            && issue.SourceRef == "H:100"
            && issue.Severity == WarehouseProposalIssueSeverity.Info
            && issue.DetailToken == "strong-rule-retained");
    }

    [Fact]
    public async Task Explicit_rack_profile_derives_stable_summary_and_missing_profile_blocks()
    {
        var fixture = Fixture();
        var request = Request(
            fixture,
            [],
            profiles:
            [
                new("H:160", WarehouseRackProfileSource.ExplicitSelected,
                    Profile("10000000-0000-0000-0000-000000000001")),
                new("H:160", WarehouseRackProfileSource.ExcelMapping,
                    Profile("20000000-0000-0000-0000-000000000002")),
                new("H:160", WarehouseRackProfileSource.HumanLocked,
                    Profile("30000000-0000-0000-0000-000000000003")),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        var rack = Proposal(result, "H:160");
        var derivation = Assert.IsType<WarehouseRackDerivationV1>(
            rack.RackDerivation);
        Assert.Equal(WarehouseRackProfileSource.HumanLocked, derivation.WinningSource);
        Assert.Equal(
            [
                WarehouseRackProfileSource.HumanLocked,
                WarehouseRackProfileSource.ExcelMapping,
                WarehouseRackProfileSource.ExplicitSelected,
            ],
            derivation.EvidenceSources);
        Assert.Equal(2, derivation.Levels.Count);
        Assert.Equal(8, derivation.LocationCount);
        Assert.Equal(8, result.Summary.DerivedLocationCount);
        Assert.Equal(2, result.Summary.DerivedRackLevelCount);
        Assert.True(derivation.RequiresExistingCodeServicePrecheck);
        Assert.Equal(
            WarehouseProposalCodeState.ExistingServicePrecheckRequired,
            rack.CodeState);
        Assert.NotEqual(
            derivation.Levels[0].FirstLocationLogicalId,
            derivation.Levels[0].LastLocationLogicalId);
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code == SpaceErrorCodes.RackProfileRequired
            && issue.SourceRef == "H:160");
        Assert.Contains(result.Issues, issue =>
            issue.Code == SpaceErrorCodes.RackProfileRequired
            && issue.SourceRef == "H:161"
            && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
    }

    [Fact]
    public async Task Parent_cycles_and_targets_without_rule_geometry_are_blocking()
    {
        var fixture = Fixture();
        var request = Request(
            fixture,
            [
                Suggestion(
                    fixture,
                    "H:140",
                    WarehouseSpaceType.Zone,
                    0.96m,
                    relations:
                    [
                        Relation(fixture, WarehouseRelationType.ParentCandidate, "H:150"),
                    ]),
                Suggestion(
                    fixture,
                    "H:150",
                    WarehouseSpaceType.Aisle,
                    0.96m,
                    relations:
                    [
                        Relation(fixture, WarehouseRelationType.ParentCandidate, "H:140"),
                    ]),
                Suggestion(
                    fixture,
                    "H:100",
                    WarehouseSpaceType.Wall,
                    0.96m,
                    relations:
                    [
                        Relation(fixture, WarehouseRelationType.ContainedBy, "H:170"),
                    ]),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        Assert.Empty(Proposal(result, "H:140").Relations);
        Assert.Empty(Proposal(result, "H:150").Relations);
        Assert.Empty(Proposal(result, "H:100").Relations);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_PARENT_RELATION_CYCLE"
            && issue.SourceRef == "H:140"
            && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_PARENT_RELATION_CYCLE"
            && issue.SourceRef == "H:150");
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_RELATION_TARGET_UNRESOLVED"
            && issue.SourceRef == "H:100");
    }

    [Fact]
    public async Task Ai_suggestion_without_deterministic_geometry_is_never_a_proposal()
    {
        var fixture = Fixture();
        var request = Request(
            fixture,
            [
                Suggestion(fixture, "H:170", WarehouseSpaceType.Zone, 0.99m),
            ]);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        Assert.DoesNotContain(result.Proposals, item => item.SourceRef == "H:170");
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AI_GEOMETRY_RULE_REQUIRED"
            && issue.SourceRef == "H:170"
            && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
    }

    [Fact]
    public async Task Locked_attribute_incompatible_with_final_type_blocks_instead_of_leaking()
    {
        var fixture = Fixture(
        [
            new("H:100", "attributes.rackType", "Selective"),
        ]);
        var request = Request(fixture, []);

        var result = await new WarehouseDraftSynthesizer()
            .SynthesizeAsync(request);

        var wall = Proposal(result, "H:100");
        Assert.DoesNotContain(
            wall.Fields,
            field => field.FieldPath == "attributes.rackType");
        Assert.Contains(result.Issues, issue =>
            issue.Code == "LOCKED_ATTRIBUTE_TYPE_CONFLICT"
            && issue.SourceRef == "H:100"
            && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
    }

    [Fact]
    public async Task Hash_and_locked_fact_snapshot_tampering_fail_closed()
    {
        var fixture = Fixture(
        [
            new("H:120", "type", "Door"),
        ]);
        var request = Request(fixture, []);
        var synthesizer = new WarehouseDraftSynthesizer();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            synthesizer.SynthesizeAsync(request with
            {
                Ai = request.Ai with { CanonicalSha256 = new string('0', 64) },
            }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            synthesizer.SynthesizeAsync(request with { LockedFacts = [] }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            synthesizer.SynthesizeAsync(request with
            {
                RulePreview = request.RulePreview with
                {
                    CoordinateTransformSha256 = new string('0', 64),
                },
            }));
    }

    [Fact]
    public void Deterministic_ids_are_uuid_v5_stable_and_coordinate_specific()
    {
        var sourceHash = new string('a', 64);

        var rack = WarehouseDeterministicIdentity.CreateObjectLogicalId(
            ModelVersionId,
            sourceHash,
            "source-rack-1");
        var repeat = WarehouseDeterministicIdentity.CreateObjectLogicalId(
            ModelVersionId,
            sourceHash,
            "source-rack-1");
        var changed = WarehouseDeterministicIdentity.CreateObjectLogicalId(
            ModelVersionId,
            sourceHash,
            "source-rack-2");
        var level = WarehouseDeterministicIdentity.CreateRackLevelLogicalId(rack, 1);
        var location = WarehouseDeterministicIdentity.CreateLocationLogicalId(
            rack,
            1,
            1,
            1);
        var nextLocation = WarehouseDeterministicIdentity.CreateLocationLogicalId(
            rack,
            1,
            1,
            2);

        Assert.Equal(rack, repeat);
        Assert.NotEqual(rack, changed);
        Assert.NotEqual(level, location);
        Assert.NotEqual(location, nextLocation);
        Assert.Equal('5', rack.ToString("D")[14]);
        Assert.Contains(rack.ToString("D")[19], "89ab");
    }

    [Fact]
    public async Task Cancellation_is_observed_before_synthesis()
    {
        var fixture = Fixture();
        var request = Request(fixture, []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WarehouseDraftSynthesizer().SynthesizeAsync(
                request,
                cancellation.Token));
    }

    internal static FixtureData Fixture(
        IReadOnlyList<SpaceAiCadLockedFactV1>? lockedFacts = null,
        bool strongWallRule = false,
        bool addDistantZone = false,
        bool addOverlappingZone = false,
        bool useConcaveZone = false)
    {
        var scenario = SpaceCadSemanticParserTests.Scenario(
            addDistantZone: addDistantZone,
            addOverlappingZone: addOverlappingZone,
            useConcaveZone: useConcaveZone);
        var profile = scenario.Profile;
        var mappingPreview = scenario.MappingPreview;
        if (strongWallRule)
        {
            profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
                scenario.Profile.SchemaVersion,
                scenario.Profile.ProfileId,
                scenario.Profile.Version,
                scenario.Profile.Name,
                scenario.Profile.Scope,
                scenario.Profile.TenantId,
                scenario.Profile.IsEnabled,
                scenario.Profile.BasedOnProfileId,
                scenario.Profile.BasedOnVersion,
                scenario.Profile.Rules.Select(rule =>
                        rule.RuleId == "L-WALL"
                            ? rule with { ConfidenceWeight = 1m }
                            : rule)
                    .ToArray()));
            mappingPreview = SpaceCadMapping.Preview(
                scenario.Request.TenantId,
                scenario.Inventory,
                profile);
        }
        var preview = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            profile,
            mappingPreview);
        var featurePackage = SpaceAiCadFeatureMinimizer.Minimize(
            scenario.Request,
            scenario.Preparation,
            SpaceAiDataPolicy.StructuredFeatures,
            HmacKey,
            SiteId,
            ModelVersionId,
            RunId,
            new WarehouseGenerationLimits(100, 8),
            lockedFacts: lockedFacts ?? []);
        return new FixtureData(
            preview,
            featurePackage,
            lockedFacts ?? []);
    }

    internal static WarehouseDraftSynthesisRequestV1 Request(
        FixtureData fixture,
        IReadOnlyList<WarehouseGenerationSuggestion> suggestions,
        IReadOnlyList<WarehouseTemplateDefaultFactV1>? defaults = null,
        IReadOnlyList<WarehouseRackProfileBindingV1>? profiles = null,
        IReadOnlyList<WarehouseGenerationDiagnostic>? diagnostics = null)
    {
        var output = new WarehouseGenerationResult(
            WarehouseGenerationInput.CurrentSchemaVersion,
            "test-request-0000000000000000000000000001",
            "cp6-synthesis-test-v1",
            new WarehouseGenerationUsage(
                fixture.FeaturePackage.ProviderInput.Features.Count,
                suggestions.Count + (diagnostics?.Count ?? 0)),
            suggestions,
            diagnostics ?? []);
        var ai = new WarehouseGenerationOutputValidator().Validate(
            fixture.FeaturePackage.ProviderInput,
            output);
        return new WarehouseDraftSynthesisRequestV1(
            ModelVersionId,
            SpaceAiGenerationRunContract.DeterministicParentRuleVersion,
            fixture.FeaturePackage,
            fixture.Preview,
            ai,
            fixture.LockedFacts,
            defaults ?? [],
            profiles ?? []);
    }

    private static WarehouseGenerationSuggestion Suggestion(
        FixtureData fixture,
        string sourceRef,
        WarehouseSpaceType type,
        decimal confidence,
        WarehouseSuggestionAttributes? attributes = null,
        IReadOnlyList<WarehouseSuggestionRelation>? relations = null) => new(
        fixture.SourceKey(sourceRef),
        type,
        confidence,
        attributes ?? new WarehouseSuggestionAttributes(),
        relations ?? [],
        [WarehouseEvidenceCode.LAYER_NAME]);

    private static WarehouseSuggestionRelation Relation(
        FixtureData fixture,
        WarehouseRelationType type,
        string targetSourceRef) => new(
        type,
        fixture.SourceKey(targetSourceRef),
        0.90m);

    internal static WarehouseRackGenerationProfileV1 Profile(string id) => new(
        Guid.Parse(id),
        RackWidthMillimeters: 3_000,
        RackDepthMillimeters: 2_000,
        RackHeightMillimeters: 4_000,
        [
            new(
                LevelNo: 1,
                BottomZMillimeters: 0,
                ClearHeightMillimeters: 1_500,
                BinCount: 3,
                DepthCount: 2,
                CellWidthMillimeters: 1_000,
                CellDepthMillimeters: 1_000,
                BeamHeightMillimeters: 100,
                MaxLoadKilograms: 1_000),
            new(
                LevelNo: 2,
                BottomZMillimeters: 1_700,
                ClearHeightMillimeters: 1_500,
                BinCount: 1,
                DepthCount: 2,
                CellWidthMillimeters: 1_000,
                CellDepthMillimeters: 1_000,
                BeamHeightMillimeters: 100,
                MaxLoadKilograms: 750),
        ]);

    private static WarehouseDraftProposalV1 Proposal(
        WarehouseDraftProposalSetV1 result,
        string sourceRef) => result.Proposals.Single(item =>
            item.SourceRef == sourceRef);

    internal sealed record FixtureData(
        SpaceCadSemanticPreviewV1 Preview,
        SpaceAiCadFeatureMinimizationV1 FeaturePackage,
        IReadOnlyList<SpaceAiCadLockedFactV1> LockedFacts)
    {
        public string SourceKey(string sourceRef) =>
            FeaturePackage.LocalSourceMap.Entries.Single(item =>
                item.SourceRefs.Contains(sourceRef, StringComparer.Ordinal)).SourceKey;
    }
}
