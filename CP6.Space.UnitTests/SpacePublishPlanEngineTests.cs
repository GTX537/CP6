using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePublishPlanEngineTests
{
    private readonly SpacePublishPlanEngine _engine = new();

    [Fact]
    public void Identical_snapshots_are_noop_and_order_independent()
    {
        var location = Object(
            SpacePublishObjectTypes.Location,
            Guid.NewGuid(),
            "L-01",
            """{"rack":"R1","code":"L-01"}""",
            "{}",
            """{"code":"L-01","rack":"R1"}""");
        var element = Object(
            SpacePublishObjectTypes.Element,
            Guid.NewGuid(),
            "E-01",
            """{"type":"Column","code":"E-01"}""",
            """{"x":100,"y":200}""",
            "{}");
        var first = _engine.Build(Input(
            [location, element],
            [element, location]));
        var second = _engine.Build(Input(
            [element, location],
            [location, element]));

        Assert.Equal(first.PlanHash, second.PlanHash);
        Assert.Equal(2, first.Changes.NoOpCount);
        Assert.Equal(0, first.ChangeCount);
        Assert.All(
            first.Items,
            item => Assert.Equal(SpacePublishActions.NoOp, item.Action));
    }

    [Fact]
    public void Plan_classifies_all_actions_and_wms_impacts()
    {
        var createId = Guid.NewGuid();
        var disableId = Guid.NewGuid();
        var restoreId = Guid.NewGuid();
        var updateId = Guid.NewGuid();
        var renameId = Guid.NewGuid();
        var geometryId = Guid.NewGuid();
        var before = new[]
        {
            Object(
                SpacePublishObjectTypes.Location,
                disableId,
                "L-DISABLE",
                """{"slot":1}""",
                "{}",
                """{"slot":1,"code":"L-DISABLE"}"""),
            Object(
                SpacePublishObjectTypes.Location,
                restoreId,
                "L-RESTORE",
                """{"slot":2}""",
                "{}",
                """{"slot":2,"code":"L-RESTORE"}""",
                SpaceLifecycleState.Disabled),
            Object(
                SpacePublishObjectTypes.Location,
                updateId,
                "L-UPDATE",
                """{"slot":3,"width":1000}""",
                "{}",
                """{"slot":3,"width":1000,"code":"L-UPDATE"}"""),
            Object(
                SpacePublishObjectTypes.Location,
                renameId,
                "L-OLD",
                """{"slot":4,"code":"L-OLD"}""",
                "{}",
                """{"slot":4,"code":"L-OLD"}"""),
            Object(
                SpacePublishObjectTypes.Element,
                geometryId,
                "E-GEO",
                """{"type":"Column"}""",
                """{"x":100}""",
                "{}"),
        };
        var after = new[]
        {
            Object(
                SpacePublishObjectTypes.Location,
                createId,
                "L-CREATE",
                """{"slot":5}""",
                "{}",
                """{"slot":5,"code":"L-CREATE"}"""),
            Object(
                SpacePublishObjectTypes.Location,
                restoreId,
                "L-RESTORE",
                """{"slot":2}""",
                "{}",
                """{"slot":2,"code":"L-RESTORE"}"""),
            Object(
                SpacePublishObjectTypes.Location,
                updateId,
                "L-UPDATE",
                """{"slot":3,"width":1200}""",
                "{}",
                """{"slot":3,"width":1200,"code":"L-UPDATE"}"""),
            Object(
                SpacePublishObjectTypes.Location,
                renameId,
                "L-NEW",
                """{"slot":4,"code":"L-NEW"}""",
                "{}",
                """{"slot":4,"code":"L-NEW"}"""),
            Object(
                SpacePublishObjectTypes.Element,
                geometryId,
                "E-GEO",
                """{"type":"Column"}""",
                """{"x":200}""",
                "{}"),
        };

        var result = _engine.Build(Input(after, before));

        Assert.Equal(1, result.Changes.CreateCount);
        Assert.Equal(2, result.Changes.UpdateMasterCount);
        Assert.Equal(1, result.Changes.UpdateGeometryOnlyCount);
        Assert.Equal(1, result.Changes.DisableCount);
        Assert.Equal(1, result.Changes.RestoreCount);
        Assert.Contains(
            result.Items,
            item =>
                item.LogicalId == createId &&
                item.ImpactCode ==
                SpacePublishImpactCodes.WmsCreateLocation);
        Assert.Contains(
            result.Items,
            item =>
                item.LogicalId == disableId &&
                item.ImpactCode ==
                SpacePublishImpactCodes.WmsDisableLocation);
        Assert.Contains(
            result.Items,
            item =>
                item.LogicalId == restoreId &&
                item.ImpactCode ==
                SpacePublishImpactCodes.WmsRestoreLocation);
        Assert.Contains(
            result.Items,
            item =>
                item.LogicalId == updateId &&
                item.ImpactCode ==
                SpacePublishImpactCodes.WmsUpdateLocation);
        var rename = Assert.Single(
            result.Items,
            item => item.LogicalId == renameId);
        Assert.Equal(
            SpacePublishImpactCodes.WmsRenameBlocked,
            rename.ImpactCode);
        Assert.True(rename.Blocking);
        Assert.True(result.HasBlockingImpact);
        Assert.Contains(
            result.Items,
            item =>
                item.LogicalId == geometryId &&
                item.Action ==
                SpacePublishActions.UpdateGeometryOnly &&
                item.ImpactCode ==
                SpacePublishImpactCodes.RuntimeOnly);
    }

    [Fact]
    public void Plan_hash_binds_validation_and_capability_evidence()
    {
        var item = Object(
            SpacePublishObjectTypes.Location,
            Guid.NewGuid(),
            "L-01",
            """{"code":"L-01"}""",
            "{}",
            """{"code":"L-01"}""");
        var original = Input([item], []);
        var first = _engine.Build(original);
        var changedValidation = _engine.Build(
            original with { ValidationRunId = Guid.NewGuid() });
        var changedCapability = _engine.Build(
            original with { CapabilityHash = new string('c', 64) });

        Assert.NotEqual(first.PlanHash, changedValidation.PlanHash);
        Assert.NotEqual(first.PlanHash, changedCapability.PlanHash);
    }

    [Fact]
    public void Adopted_location_create_does_not_recreate_existing_wms_bin()
    {
        var location = Object(
            SpacePublishObjectTypes.Location,
            Guid.NewGuid(),
            "WMS-EXISTING-01",
            """{"code":"WMS-EXISTING-01"}""",
            "{}",
            """{"code":"WMS-EXISTING-01"}""",
            externalBindingId: "external-bin-01");

        var result = _engine.Build(Input([location], []));

        var item = Assert.Single(result.Items);
        Assert.Equal(SpacePublishActions.Create, item.Action);
        Assert.Equal(SpacePublishImpactCodes.WmsNoOp, item.ImpactCode);
        Assert.Equal(0, result.WmsImpact.WmsCreateCount);
        Assert.Equal(1, result.WmsImpact.WmsNoOpCount);
    }

    [Fact]
    public void Canonical_json_ignores_property_order_and_number_format()
    {
        var logicalId = Guid.NewGuid();
        var before = Object(
            SpacePublishObjectTypes.Element,
            logicalId,
            null,
            """{"b":2.0,"a":1}""",
            """{"position":{"y":20,"x":10}}""",
            "{}");
        var after = Object(
            SpacePublishObjectTypes.Element,
            logicalId,
            null,
            """
            {
              "a": 1.0,
              "b": 2
            }
            """,
            """{"position":{"x":10,"y":20}}""",
            "{}");

        var result = _engine.Build(Input([after], [before]));

        Assert.Equal(SpacePublishActions.NoOp, Assert.Single(result.Items).Action);
    }

    [Fact]
    public void Wms_only_change_is_update_master_and_changes_plan_hash()
    {
        var logicalId = Guid.NewGuid();
        var before = Object(
            SpacePublishObjectTypes.Location,
            logicalId,
            "L-01",
            "{}",
            "{}",
            """{"code":"L-01","pickSequence":1}""");
        var after = Object(
            SpacePublishObjectTypes.Location,
            logicalId,
            "L-01",
            "{}",
            "{}",
            """{"code":"L-01","pickSequence":2}""");

        var unchanged = _engine.Build(Input([before], [before]));
        var changed = _engine.Build(Input([after], [before]));
        var item = Assert.Single(changed.Items);

        Assert.Equal(SpacePublishActions.UpdateMaster, item.Action);
        Assert.True(item.WmsChanged);
        Assert.Equal(
            SpacePublishImpactCodes.WmsUpdateLocation,
            item.ImpactCode);
        Assert.NotEqual(unchanged.PlanHash, changed.PlanHash);
    }

    private static SpacePublishPlanInput Input(
        IReadOnlyList<SpacePublishObjectSnapshot> after,
        IReadOnlyList<SpacePublishObjectSnapshot> before) =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            "Passed",
            0,
            new string('a', 64),
            "test-wms",
            new string('b', 64),
            after,
            before);

    private static SpacePublishObjectSnapshot Object(
        string objectType,
        Guid logicalId,
        string? code,
        string masterJson,
        string geometryJson,
        string wmsJson,
        SpaceLifecycleState lifecycleState =
            SpaceLifecycleState.Active,
        string? externalBindingId = null) =>
        SpacePublishObjectSnapshot.Create(
            objectType,
            logicalId,
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            lifecycleState,
            code,
            masterJson,
            geometryJson,
            wmsJson,
            "{}",
            externalBindingId);
}
