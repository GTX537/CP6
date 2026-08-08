using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceValidationEngineTests
{
    private static readonly Guid TenantId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid FloorId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ZoneId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid RackId =
        Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid LevelId =
        Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid LocationOneId =
        Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid LocationTwoId =
        Guid.Parse("20000000-0000-0000-0000-000000000006");

    private readonly SpaceValidationEngine _engine = new();
    private readonly SpaceValidationProfile _profile =
        SpaceValidationProfile.Create(
            "test-wms",
            40,
            "^[A-Z0-9-]+$",
            1000);

    [Fact]
    public void Valid_snapshot_passes_and_hash_is_order_independent()
    {
        var snapshot = ValidSnapshot();
        var reordered = snapshot with
        {
            Locations = snapshot.Locations.Reverse().ToArray(),
        };

        var result = _engine.Validate(snapshot, _profile);
        var reorderedResult = _engine.Validate(reordered, _profile);

        Assert.Empty(result.Issues);
        Assert.Equal(result.ContentHash, reorderedResult.ContentHash);
        Assert.Equal(64, result.ContentHash.Length);
    }

    [Fact]
    public void Excel_metadata_is_validated_and_participates_in_content_hash()
    {
        var snapshot = ValidSnapshot();
        snapshot = snapshot with
        {
            Locations =
            [
                snapshot.Locations[0] with
                {
                    LocationType = SpaceLocationTypes.Storage,
                },
                snapshot.Locations[1],
            ],
            LocationBindings =
            [
                new SpaceValidationLocationBinding(
                    LocationOneId,
                    "test-wms",
                    "WH-01",
                    "EXT-01",
                    SpaceLocationBindingMode.WmsPrimary),
            ],
            DesignAttributes =
            [
                new SpaceValidationDesignAttribute(
                    SpaceDesignAttributeObjectTypes.Location,
                    LocationOneId,
                    SpaceDesignAttributeNamespaces.Custom,
                    "TemperatureClass",
                    "Ambient",
                    null),
            ],
        };
        var changed = snapshot with
        {
            DesignAttributes =
            [
                snapshot.DesignAttributes![0] with { Value = "Cold" },
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        Assert.Empty(result.Issues);
        Assert.NotEqual(
            result.ContentHash,
            _engine.ComputeContentHash(changed));
    }

    [Fact]
    public void Hierarchy_codes_and_rack_slots_are_reported_together()
    {
        var snapshot = ValidSnapshot();
        snapshot = snapshot with
        {
            Zones =
            [
                snapshot.Zones[0] with { FloorLogicalId = Guid.NewGuid() },
            ],
            Locations =
            [
                snapshot.Locations[0],
                snapshot.Locations[1] with
                {
                    LocationCode = snapshot.Locations[0].LocationCode,
                    ColumnNo = 1,
                },
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        Assert.Contains(
            result.Issues,
            issue => issue.Code == SpaceValidationIssueCodes.ParentMissing);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == SpaceValidationIssueCodes.CodeDuplicate);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.RackLocationIncomplete);
        Assert.True(result.BlockingCount >= 3);
    }

    [Fact]
    public void Rack_collision_and_boundary_escape_are_blocking()
    {
        var snapshot = ValidSnapshot();
        var collidingRackId = Guid.NewGuid();
        snapshot = snapshot with
        {
            Racks =
            [
                snapshot.Racks[0],
                snapshot.Racks[0] with
                {
                    Revision = Active(collidingRackId),
                    RackCode = "R2",
                    X = 1500,
                },
            ],
            RackLevels =
            [
                snapshot.RackLevels[0],
                snapshot.RackLevels[0] with
                {
                    Revision = Active(Guid.NewGuid()),
                    RackLogicalId = collidingRackId,
                },
            ],
            Locations =
            [
                .. snapshot.Locations,
                NewLocation(Guid.NewGuid(), collidingRackId, "R2-01", 1),
                NewLocation(Guid.NewGuid(), collidingRackId, "R2-02", 2),
            ],
        };
        snapshot = snapshot with
        {
            Racks =
            [
                snapshot.Racks[0] with { X = 9500 },
                snapshot.Racks[1] with { X = 9600 },
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == SpaceValidationIssueCodes.GeometryOutOfBounds);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == SpaceValidationIssueCodes.GeometryCollision);
    }

    [Fact]
    public void Asset_binding_and_ai_model_issue_share_the_result()
    {
        var assetId = Guid.NewGuid();
        var generationRunId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var snapshot = ValidSnapshot() with
        {
            Elements =
            [
                new SpaceValidationElement(
                    Active(elementId),
                    FloorId,
                    null,
                    SpaceElementTypes.StaticEquipment,
                    $$$"""
                    {"schemaVersion":1,"kind":"asset","assetVersionId":"{{{assetId}}}","transform":{}}
                    """,
                    assetId,
                    SpaceAssetScope.Tenant,
                    Guid.NewGuid(),
                    100,
                    100,
                    0,
                    0,
                    1000,
                    1000,
                    1000,
                    "EQ-1",
                    null,
                    null,
                    []),
            ],
            AssetVersions =
            [
                new SpaceValidationAssetVersion(
                    assetId,
                    SpaceAssetScope.Tenant,
                    TenantId,
                    SpaceAssetVersionStatus.Ready),
            ],
            ExistingIssues =
            [
                new SpaceValidationExistingIssue(
                    SpaceIssueSeverity.Warning,
                    null,
                    "AI_LOW_CONFIDENCE",
                    null,
                    "layer:rack",
                    elementId,
                    "/attributes/rackType",
                    """{"score":0.42}""",
                    "review-ai-proposal",
                    generationRunId,
                    proposalId,
                    """{"provider":"mock"}"""),
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        var binding = Assert.Single(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.AssetBindingInvalid);
        Assert.Equal(SpaceIssueSeverity.Blocking, binding.Severity);
        var ai = Assert.Single(
            result.Issues,
            issue => issue.Code == "AI_LOW_CONFIDENCE");
        Assert.Equal(SpaceValidationCategories.AiProvenance, ai.Category);
        Assert.Equal(generationRunId, ai.GenerationRunId);
        Assert.Equal(proposalId, ai.GenerationProposalId);
    }

    [Fact]
    public void Ready_asset_can_be_attached_to_non_asset_geometry()
    {
        var assetId = Guid.NewGuid();
        var snapshot = ValidSnapshot() with
        {
            Elements =
            [
                new SpaceValidationElement(
                    Active(Guid.NewGuid()),
                    FloorId,
                    null,
                    SpaceElementTypes.StaticEquipment,
                    """
                    {"schemaVersion":1,"kind":"box","width":1000,"height":1000,"depth":1000}
                    """,
                    assetId,
                    SpaceAssetScope.Tenant,
                    TenantId,
                    100,
                    100,
                    0,
                    0,
                    1000,
                    1000,
                    1000,
                    "EQ-1",
                    null,
                    null,
                    []),
            ],
            AssetVersions =
            [
                new SpaceValidationAssetVersion(
                    assetId,
                    SpaceAssetScope.Tenant,
                    TenantId,
                    SpaceAssetVersionStatus.Ready),
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        Assert.DoesNotContain(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.AssetBindingInvalid);
    }

    [Fact]
    public void Published_location_code_and_identity_are_frozen()
    {
        var snapshot = ValidSnapshot() with
        {
            PublishedLocations =
            [
                new SpaceValidationPublishedLocation(
                    LocationOneId,
                    "OLD-01",
                    SpaceExternalBindingState.Bound),
                new SpaceValidationPublishedLocation(
                    Guid.NewGuid(),
                    "R1-02",
                    SpaceExternalBindingState.Bound),
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.LocationCodeFrozen);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.LocationIdentityConflict);
    }

    [Fact]
    public void Disabling_an_unbound_existing_wms_location_is_blocking()
    {
        var snapshot = ValidSnapshot();
        snapshot = snapshot with
        {
            Locations =
            [
                snapshot.Locations[0] with
                {
                    Revision = snapshot.Locations[0].Revision with
                    {
                        LifecycleState = SpaceLifecycleState.Disabled,
                    },
                },
                snapshot.Locations[1],
            ],
            PublishedLocations =
            [
                new SpaceValidationPublishedLocation(
                    LocationOneId,
                    "R1-01",
                    SpaceExternalBindingState.Unbound),
            ],
            ExistingIssues =
            [
                new SpaceValidationExistingIssue(
                    SpaceIssueSeverity.Warning,
                    "WmsAdoption",
                    SpaceErrorCodes.WmsLocationUnbound,
                    null,
                    "EXT-01",
                    LocationOneId,
                    "wmsAdoption",
                    "{}",
                    "bind-wms-location",
                    null,
                    null,
                    "{}"),
            ],
        };

        var result = _engine.Validate(snapshot, _profile);

        var issue = Assert.Single(
            result.Issues,
            value =>
                value.Code == SpaceErrorCodes.WmsLocationUnbound);
        Assert.Equal(SpaceIssueSeverity.Blocking, issue.Severity);
    }

    [Fact]
    public void Validation_run_enforces_terminal_counts()
    {
        var now = DateTime.UtcNow;
        var run = SpaceValidationRun.CreateQueued(
            TenantId,
            Guid.NewGuid(),
            3,
            new string('a', 64),
            SpaceValidationRuleSet.Version,
            _profile.AdapterId,
            _profile.CapabilityHash,
            Guid.NewGuid(),
            now,
            Guid.NewGuid(),
            Guid.NewGuid());

        run.Start(now);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.Pass(1, 0, 0, now));
        run.Block(1, 2, 3, now);
        Assert.Equal(SpaceValidationStatus.Blocked, run.Status);
        Assert.True(run.IsReusable);
        Assert.Equal(1, run.BlockingCount);
    }

    [Fact]
    public void Referenced_source_must_be_ready_and_have_safe_metadata()
    {
        var sourceId = Guid.NewGuid();
        var snapshot = ValidSnapshot();
        snapshot = snapshot with
        {
            Floors =
            [
                snapshot.Floors[0] with
                {
                    Revision = new SpaceValidationRevisionRef(
                        FloorId,
                        sourceId,
                        "layer:floor",
                        SpaceLifecycleState.Active),
                },
            ],
            Sources =
            [
                new SpaceValidationSource(
                    sourceId,
                    SpaceSourceType.Dxf,
                    "not-a-hash",
                    SpaceSourceState.Ready,
                    null,
                    null),
            ],
        };

        var result = new SpaceValidationEngine().Validate(
            snapshot,
            SpaceValidationProfile.Create(
                "test",
                30,
                "^[A-Z0-9-]+$",
                100_000));

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == SpaceValidationIssueCodes.SourceNotReady);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                SpaceValidationIssueCodes.SourceMetadataInvalid);
    }

    [Theory]
    [InlineData(SpaceSourceType.Editor)]
    [InlineData(SpaceSourceType.Template)]
    public void Ready_inline_source_is_publishable(
        SpaceSourceType sourceType)
    {
        var sourceId = Guid.NewGuid();
        var snapshot = ValidSnapshot();
        snapshot = snapshot with
        {
            Floors =
            [
                snapshot.Floors[0] with
                {
                    Revision = new SpaceValidationRevisionRef(
                        FloorId,
                        sourceId,
                        "inline:floor",
                        SpaceLifecycleState.Active),
                },
            ],
            Sources =
            [
                new SpaceValidationSource(
                    sourceId,
                    sourceType,
                    new string('a', 64),
                    SpaceSourceState.Ready,
                    null,
                    null),
            ],
        };

        var result = new SpaceValidationEngine().Validate(
            snapshot,
            SpaceValidationProfile.Create(
                "test",
                30,
                "^[A-Z0-9-]+$",
                100_000));

        Assert.DoesNotContain(
            result.Issues,
            issue =>
                issue.Code == SpaceValidationIssueCodes.SourceNotReady);
    }

    private static SpaceValidationSnapshot ValidSnapshot()
    {
        var floor = new SpaceValidationFloor(
            Active(FloorId),
            SiteId,
            1,
            "F1",
            0,
            5000,
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""",
            "LOCAL_MM_Z_UP",
            null,
            null);
        var zone = new SpaceValidationZone(
            Active(ZoneId),
            FloorId,
            "Z1",
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""");
        var rack = new SpaceValidationRack(
            Active(RackId),
            FloorId,
            ZoneId,
            null,
            "R1",
            1000,
            1000,
            0,
            0,
            2000,
            1000,
            2000);
        var level = new SpaceValidationRackLevel(
            Active(LevelId),
            RackId,
            1,
            0,
            1800,
            2,
            1,
            1000,
            1000,
            100);
        return new SpaceValidationSnapshot(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            SiteId,
            4,
            [floor],
            [zone],
            [],
            [rack],
            [level],
            [
                NewLocation(LocationOneId, RackId, "R1-01", 1),
                NewLocation(LocationTwoId, RackId, "R1-02", 2),
            ],
            [],
            [],
            [],
            [],
            []);
    }

    private static SpaceValidationLocation NewLocation(
        Guid logicalId,
        Guid rackId,
        string code,
        int column) =>
        new(
            Active(logicalId),
            FloorId,
            rackId,
            code,
            column,
            1,
            1,
            1000,
            1800,
            1000,
            SpaceLocationCodeOrigin.Generated,
            SpaceExternalBindingState.Unbound);

    private static SpaceValidationRevisionRef Active(Guid logicalId) =>
        new(
            logicalId,
            null,
            null,
            SpaceLifecycleState.Active);
}
