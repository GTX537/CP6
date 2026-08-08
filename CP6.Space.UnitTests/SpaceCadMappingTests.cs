using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadMappingTests
{
    private static readonly Guid TenantId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Seal_canonicalizes_rules_and_is_deterministic()
    {
        var draft = Draft(Rules().Reverse().ToArray());

        var first = SpaceCadMapping.Seal(draft);
        var second = SpaceCadMapping.Seal(draft);

        Assert.Equal(first.DefinitionSha256, second.DefinitionSha256);
        Assert.Equal(
            first.Rules.OrderBy(rule => rule.RuleId).Select(rule => rule.RuleId),
            first.Rules.Select(rule => rule.RuleId));
        Assert.Equal(
            SpaceCadMapping.SerializeProfile(first),
            SpaceCadMapping.SerializeProfile(second));
    }

    [Fact]
    public void Tenant_copy_records_system_base_and_next_version_is_immutable()
    {
        var system = Profile();
        var copy = SpaceCadMapping.CreateTenantCopy(
            system,
            TenantId,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Tenant warehouse mapping");
        var next = SpaceCadMapping.CreateNextTenantVersion(
            copy,
            TenantId,
            copy.Rules,
            name: "Tenant warehouse mapping v2");

        Assert.Equal(SpaceCadMappingScope.Tenant, copy.Scope);
        Assert.Equal(TenantId, copy.TenantId);
        Assert.Equal(system.ProfileId, copy.BasedOnProfileId);
        Assert.Equal(system.Version, copy.BasedOnVersion);
        Assert.Equal(1, copy.Version);
        Assert.Equal(2, next.Version);
        Assert.NotEqual(copy.DefinitionSha256, next.DefinitionSha256);
        Assert.Throws<InvalidOperationException>(() =>
            SpaceCadMapping.CreateNextTenantVersion(system, TenantId, system.Rules));
    }

    [Fact]
    public void Preview_maps_layers_and_attribute_qualified_blocks_without_hiding_empty_layers()
    {
        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile());

        Assert.True(preview.ReadyForSemanticParsing);
        Assert.Equal(4, preview.Summary.LayerCount);
        Assert.Equal(3, preview.Summary.MappedLayerCount);
        Assert.Equal(1, preview.Summary.UnmappedLayerCount);
        Assert.Equal(1, preview.Summary.BlockCount);
        Assert.Equal(1, preview.Summary.MappedBlockCount);
        Assert.Equal(4, preview.Summary.MappedLayerEntityCount);
        Assert.Equal(2, preview.Summary.MappedBlockReferenceCount);
        Assert.Equal(1, preview.Summary.InfoCount);
        Assert.Equal(0, preview.Summary.WarningCount);
        Assert.Equal(0, preview.Summary.BlockingCount);
        Assert.Matches("^[0-9a-f]{64}$", preview.SourceStructureSha256);
        Assert.Matches("^[0-9a-f]{64}$", preview.ReuseKeySha256);
        Assert.Matches("^[0-9a-f]{64}$", preview.PreviewSha256);

        var empty = Assert.Single(preview.Decisions, decision => decision.SourceKey == "0");
        Assert.Equal(SpaceCadMappingDecisionStatus.Unmapped, empty.Status);
        Assert.Contains(
            preview.Issues,
            issue => issue.SourceKey == "0" && issue.Severity == SpaceCadIssueSeverity.Info);
        var block = Assert.Single(
            preview.Decisions,
            decision => decision.SourceKind == SpaceCadMappingSourceKind.Block);
        Assert.Equal("B-RACK", block.RuleId);
        Assert.Equal(SpaceCadGeometryRule.BlockFootprint, block.GeometryRule);
    }

    [Fact]
    public void Layer_overrides_win_and_can_explicitly_ignore_a_layer()
    {
        var overrides = new SpaceCadLayerMappingOverrideV1[]
        {
            new(
                "wall",
                Ignore: false,
                SpaceCadSemanticTarget.Door,
                TargetSubtype: null,
                SpaceCadGeometryRule.DirectGeometry,
                DefaultHeightMillimeters: 2_400,
                DefaultThicknessMillimeters: 120,
                ConfidenceWeight: 0.8m),
            new(
                "HIDDEN",
                Ignore: true,
                Target: null,
                TargetSubtype: null,
                GeometryRule: null,
                DefaultHeightMillimeters: null,
                DefaultThicknessMillimeters: null,
                ConfidenceWeight: null),
        };

        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile(),
            overrides);

        var wall = Assert.Single(preview.Decisions, decision => decision.SourceKey == "WALL");
        Assert.Equal(SpaceCadMappingDecisionSource.LayerOverride, wall.DecisionSource);
        Assert.Equal(SpaceCadSemanticTarget.Door, wall.Target);
        var hidden = Assert.Single(preview.Decisions, decision => decision.SourceKey == "HIDDEN");
        Assert.Equal(SpaceCadMappingDecisionStatus.Ignored, hidden.Status);
        Assert.Equal(1, preview.Summary.IgnoredLayerCount);
        Assert.Equal("WALL", preview.LayerOverrides.Single(item => !item.Ignore).LayerId);
    }

    [Fact]
    public void Equal_priority_and_specificity_rules_fail_closed_as_a_conflict()
    {
        var rules = Rules().Append(Rule(
            "L-WALL-CONFLICT",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "WALL",
            SpaceCadSemanticTarget.Door,
            SpaceCadGeometryRule.DirectGeometry,
            priority: 100)).ToArray();

        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile(rules));

        Assert.False(preview.ReadyForSemanticParsing);
        Assert.Equal(1, preview.Summary.ConflictLayerCount);
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_MAPPING_RULE_CONFLICT"
                     && issue.Severity == SpaceCadIssueSeverity.Blocking);
    }

    [Fact]
    public void Exact_match_outranks_glob_at_the_same_priority()
    {
        var rules = Rules().Append(Rule(
            "L-WALL-GLOB",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Glob,
            "W*",
            SpaceCadSemanticTarget.Door,
            SpaceCadGeometryRule.DirectGeometry,
            priority: 100)).ToArray();

        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile(rules));

        var wall = Assert.Single(preview.Decisions, decision => decision.SourceKey == "WALL");
        Assert.Equal("L-WALL", wall.RuleId);
        Assert.Equal(SpaceCadSemanticTarget.Wall, wall.Target);
    }

    [Fact]
    public void Missing_required_source_blocks_semantic_readiness()
    {
        var rules = Rules().Append(Rule(
            "L-MISSING",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "DOES_NOT_EXIST",
            SpaceCadSemanticTarget.Zone,
            SpaceCadGeometryRule.ClosedBoundary,
            priority: 100,
            required: true)).ToArray();

        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile(rules));

        Assert.False(preview.ReadyForSemanticParsing);
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_MAPPING_REQUIRED_SOURCE_MISSING"
                     && issue.RuleId == "L-MISSING");
    }

    [Fact]
    public void Empty_layer_does_not_satisfy_a_required_mapping_rule()
    {
        var rules = Rules().Append(Rule(
            "L-EMPTY-REQUIRED",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "0",
            SpaceCadSemanticTarget.Guide,
            SpaceCadGeometryRule.DirectGeometry,
            priority: 100,
            required: true)).ToArray();

        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            Profile(rules));

        Assert.False(preview.ReadyForSemanticParsing);
        Assert.Contains(
            preview.Issues,
            issue => issue.Code == "SPACE_CAD_MAPPING_REQUIRED_SOURCE_MISSING"
                     && issue.RuleId == "L-EMPTY-REQUIRED");
    }

    [Fact]
    public void Tenant_profile_cannot_cross_tenants_while_system_profile_is_shared()
    {
        var system = Profile();
        var tenant = SpaceCadMapping.CreateTenantCopy(
            system,
            TenantId,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Tenant mapping");
        var otherTenant = Guid.Parse("77777777-7777-7777-7777-777777777777");

        Assert.NotNull(SpaceCadMapping.Preview(
            otherTenant,
            SpaceCadInventoryTests.Inventory(),
            system));
        Assert.Throws<UnauthorizedAccessException>(() => SpaceCadMapping.Preview(
            otherTenant,
            SpaceCadInventoryTests.Inventory(),
            tenant));
    }

    [Fact]
    public void Reuse_key_survives_floor_coordinate_changes_but_changes_with_overrides()
    {
        var profile = Profile();
        var first = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            profile);
        var moved = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(new SpaceCadMillimeterPointV1(1_000, 1_000)),
            profile);
        var overridden = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            profile,
            [new SpaceCadLayerMappingOverrideV1(
                "0",
                Ignore: true,
                Target: null,
                TargetSubtype: null,
                GeometryRule: null,
                DefaultHeightMillimeters: null,
                DefaultThicknessMillimeters: null,
                ConfidenceWeight: null)]);

        Assert.NotEqual(first.InventorySha256, moved.InventorySha256);
        Assert.Equal(first.SourceStructureSha256, moved.SourceStructureSha256);
        Assert.Equal(first.ReuseKeySha256, moved.ReuseKeySha256);
        Assert.NotEqual(first.PreviewSha256, moved.PreviewSha256);
        Assert.NotEqual(first.ReuseKeySha256, overridden.ReuseKeySha256);
    }

    [Fact]
    public void Profile_and_preview_hashes_reject_tampering()
    {
        var profile = Profile();
        var preview = SpaceCadMapping.Preview(
            TenantId,
            SpaceCadInventoryTests.Inventory(),
            profile);

        Assert.Throws<InvalidDataException>(() => SpaceCadMapping.Validate(
            profile with { Name = "Tampered" }));
        Assert.Throws<InvalidDataException>(() => SpaceCadMapping.ValidatePreview(
            preview with { ReadyForSemanticParsing = false }));
    }

    [Fact]
    public void Invalid_regex_and_layer_attribute_conditions_are_rejected()
    {
        var invalidRegex = Rule(
            "BAD-REGEX",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Regex,
            "(?=unsafe)",
            SpaceCadSemanticTarget.Wall,
            SpaceCadGeometryRule.Centerline,
            priority: 1);
        var invalidAttribute = Rule(
            "BAD-ATTRIBUTE",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "WALL",
            SpaceCadSemanticTarget.Wall,
            SpaceCadGeometryRule.Centerline,
            priority: 1) with
        {
            AttributeName = "CODE",
            AttributeMatchKind = SpaceCadMappingMatchKind.Glob,
            AttributePattern = "R-*",
        };

        Assert.Throws<ArgumentException>(() =>
            SpaceCadMapping.Seal(Draft([invalidRegex])));
        Assert.Throws<InvalidDataException>(() =>
            SpaceCadMapping.Seal(Draft([invalidAttribute])));
    }

    private static SpaceCadMappingProfileV1 Profile(
        IReadOnlyList<SpaceCadMappingRuleV1>? rules = null) =>
        SpaceCadMapping.Seal(Draft(rules ?? Rules()));

    private static SpaceCadMappingProfileDraftV1 Draft(
        IReadOnlyList<SpaceCadMappingRuleV1> rules) =>
        new(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            Version: 1,
            "CP6 standard warehouse mapping",
            SpaceCadMappingScope.System,
            TenantId: null,
            IsEnabled: true,
            BasedOnProfileId: null,
            BasedOnVersion: null,
            rules);

    private static SpaceCadMappingRuleV1[] Rules() =>
    [
        Rule(
            "L-WALL",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "WALL",
            SpaceCadSemanticTarget.Wall,
            SpaceCadGeometryRule.Centerline,
            priority: 100,
            required: true,
            height: 3_000,
            thickness: 200),
        Rule(
            "L-RACK",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Exact,
            "RACK",
            SpaceCadSemanticTarget.Rack,
            SpaceCadGeometryRule.ClosedBoundary,
            priority: 100,
            required: true),
        Rule(
            "L-HIDDEN",
            SpaceCadMappingSourceKind.Layer,
            SpaceCadMappingMatchKind.Glob,
            "H*",
            SpaceCadSemanticTarget.Guide,
            SpaceCadGeometryRule.DirectGeometry,
            priority: 10),
        Rule(
            "B-RACK",
            SpaceCadMappingSourceKind.Block,
            SpaceCadMappingMatchKind.Exact,
            "RACK",
            SpaceCadSemanticTarget.Rack,
            SpaceCadGeometryRule.BlockFootprint,
            priority: 100,
            required: true) with
        {
            AttributeName = "CODE",
            AttributeMatchKind = SpaceCadMappingMatchKind.Glob,
            AttributePattern = "R-*",
        },
    ];

    private static SpaceCadMappingRuleV1 Rule(
        string ruleId,
        SpaceCadMappingSourceKind sourceKind,
        SpaceCadMappingMatchKind matchKind,
        string pattern,
        SpaceCadSemanticTarget target,
        SpaceCadGeometryRule geometryRule,
        int priority,
        bool required = false,
        decimal? height = null,
        decimal? thickness = null) =>
        new(
            ruleId,
            priority,
            sourceKind,
            matchKind,
            pattern,
            AttributeName: null,
            AttributeMatchKind: null,
            AttributePattern: null,
            target,
            TargetSubtype: null,
            geometryRule,
            height,
            thickness,
            ConfidenceWeight: 0.95m,
            required);
}
