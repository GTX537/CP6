using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpacePublishPreviewSqlServerTests
{
    [SqlServerFact]
    public async Task Preview_is_deterministic_filterable_and_tenant_scoped()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededVersions seeded;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seeded = await SeedVersionsAsync(seed, renameLocation: false);
            }

            await ValidateAsync(
                connectionString,
                execution,
                clock,
                seeded.SiteId,
                seeded.TargetVersionId);

            string planHash;
            string nextCursor;
            await using (var firstContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewPreviewService(
                    firstContext,
                    execution,
                    seeded.SiteId);
                var first = await service.GetPreviewAsync(
                    seeded.TargetVersionId,
                    null,
                    null,
                    null,
                    null,
                    includeNoOp: false,
                    limit: 2,
                    cursor: null);
                var blockingCodes = await firstContext.Issues
                    .Where(issue =>
                        issue.ValidationRunId == first.ValidationRunId &&
                        issue.Severity == SpaceIssueSeverity.Blocking)
                    .Select(issue => issue.Code)
                    .ToArrayAsync();
                Assert.True(
                    first.Publishable,
                    $"validation={first.ValidationStatus}/" +
                    $"{first.ValidationBlockingCount}; " +
                    $"impactBlocking={first.WmsImpact.BlockingCount}; " +
                    $"codes={string.Join(',', blockingCodes)}; " +
                    $"items={string.Join(',', first.Items.Select(item =>
                        $"{item.ObjectType}:{item.Action}:{item.ImpactCode}"))}");
                Assert.Equal("Passed", first.ValidationStatus);
                Assert.Equal(seeded.BaseVersionId, first.BaseVersionId);
                Assert.Equal(8, first.ItemCount);
                Assert.Equal(3, first.ChangeCount);
                Assert.Equal(3, first.MatchedItemCount);
                Assert.Equal(1, first.Changes.CreateCount);
                Assert.Equal(1, first.Changes.UpdateGeometryOnlyCount);
                Assert.Equal(1, first.Changes.DisableCount);
                Assert.Equal(5, first.Changes.NoOpCount);
                Assert.Equal(1, first.WmsImpact.WmsCreateCount);
                Assert.Equal(1, first.WmsImpact.WmsDisableCount);
                Assert.Equal(1, first.WmsImpact.RuntimeOnlyCount);
                Assert.Equal(2, first.Items.Count);
                Assert.NotNull(first.NextCursor);
                planHash = first.PlanHash;
                nextCursor = first.NextCursor!;
            }

            await using (var secondContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewPreviewService(
                    secondContext,
                    execution,
                    seeded.SiteId);
                var second = await service.GetPreviewAsync(
                    seeded.TargetVersionId,
                    null,
                    null,
                    null,
                    null,
                    includeNoOp: false,
                    limit: 2,
                    cursor: nextCursor);
                Assert.Equal(planHash, second.PlanHash);
                Assert.Single(second.Items);
                Assert.Null(second.NextCursor);

                var locations = await service.GetPreviewAsync(
                    seeded.TargetVersionId,
                    seeded.FloorLogicalId,
                    SpacePublishObjectTypes.Location,
                    null,
                    null,
                    includeNoOp: false,
                    limit: 100,
                    cursor: null);
                Assert.Equal(2, locations.MatchedItemCount);
                Assert.All(
                    locations.Items,
                    item => Assert.Equal(
                        SpacePublishObjectTypes.Location,
                        item.ObjectType));
                Assert.Contains(
                    locations.Items,
                    item =>
                        item.LogicalId == seeded.NewLocationLogicalId &&
                        item.Action == SpacePublishActions.Create &&
                        item.ImpactCode ==
                        SpacePublishImpactCodes.WmsCreateLocation);
                Assert.Contains(
                    locations.Items,
                    item =>
                        item.LogicalId ==
                        seeded.RemovedLocationLogicalId &&
                        item.Action == SpacePublishActions.Disable &&
                        item.ImpactCode ==
                        SpacePublishImpactCodes.WmsDisableLocation);

                var full = await service.GetPreviewAsync(
                    seeded.TargetVersionId,
                    null,
                    null,
                    null,
                    null,
                    includeNoOp: true,
                    limit: 100,
                    cursor: null);
                Assert.Equal(8, full.Items.Count);
                Assert.Equal(planHash, full.PlanHash);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using (var other = CreateContext(
                             connectionString,
                             otherExecution,
                             clock))
            {
                var hidden = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => NewPreviewService(
                            other,
                            otherExecution,
                            seeded.SiteId)
                        .GetPreviewAsync(
                            seeded.TargetVersionId,
                            null,
                            null,
                            null,
                            null,
                            false,
                            100,
                            null));
                Assert.Equal(SpaceErrorCodes.VersionNotFound, hidden.Code);
                Assert.Equal(404, hidden.StatusCode);
            }

            await using var staleContext = CreateContext(
                connectionString,
                execution,
                clock);
            var target = await staleContext.Versions.SingleAsync(
                value => value.Id == seeded.TargetVersionId);
            target.TouchContent();
            await staleContext.SaveChangesAsync();
            var stale = await Assert.ThrowsAsync<SpaceProblemException>(
                () => NewPreviewService(
                        staleContext,
                        execution,
                        seeded.SiteId)
                    .GetPreviewAsync(
                        seeded.TargetVersionId,
                        null,
                        null,
                        null,
                        null,
                        false,
                        100,
                        null));
            Assert.Equal(SpaceErrorCodes.ValidationStale, stale.Code);
            Assert.Equal(409, stale.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task Adopted_location_create_is_wms_noop_in_publish_preview()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededVersions seeded;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seeded = await SeedVersionsAsync(seed, renameLocation: false);
                var location = await seed.LocationRevisions.SingleAsync(
                    value =>
                        value.ModelVersionId == seeded.TargetVersionId &&
                        value.LogicalId == seeded.NewLocationLogicalId);
                location.BindAdoptedLocationCode("R1-03");
                var adoption = SpaceWmsAdoption.Discover(
                    execution.TenantId,
                    seeded.SiteId,
                    "cp6-wms-v1",
                    "CP6_WMS",
                    "Real",
                    Guid.NewGuid(),
                    "external-existing-bin-01",
                    "R1-03",
                    true,
                    "7",
                    new string('e', 64),
                    clock.UtcNow);
                adoption.Bind(
                    seeded.TargetVersionId,
                    seeded.NewLocationLogicalId,
                    clock.UtcNow);
                seed.WmsAdoptions.Add(adoption);
                (await seed.Versions.SingleAsync(
                    value => value.Id == seeded.TargetVersionId))
                    .TouchContent();
                await seed.SaveChangesAsync();
            }

            await ValidateAsync(
                connectionString,
                execution,
                clock,
                seeded.SiteId,
                seeded.TargetVersionId);
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var preview = await NewPreviewService(
                    context,
                    execution,
                    seeded.SiteId)
                .GetPreviewAsync(
                    seeded.TargetVersionId,
                    null,
                    SpacePublishObjectTypes.Location,
                    SpacePublishActions.Create,
                    null,
                    includeNoOp: false,
                    limit: 100,
                    cursor: null);

            var item = Assert.Single(preview.Items);
            Assert.Equal(seeded.NewLocationLogicalId, item.LogicalId);
            Assert.Equal(SpacePublishImpactCodes.WmsNoOp, item.ImpactCode);
            Assert.Equal("external-existing-bin-01", item.ExternalBindingId);
            Assert.Equal(0, preview.WmsImpact.WmsCreateCount);
            Assert.Equal(1, preview.WmsImpact.WmsNoOpCount);
        });
    }

    [SqlServerFact]
    public async Task Blocked_rename_preview_exposes_wms_impact()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededVersions seeded;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seeded = await SeedVersionsAsync(seed, renameLocation: true);
            }

            await ValidateAsync(
                connectionString,
                execution,
                clock,
                seeded.SiteId,
                seeded.TargetVersionId);

            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var preview = await NewPreviewService(
                    context,
                    execution,
                    seeded.SiteId)
                .GetPreviewAsync(
                    seeded.TargetVersionId,
                    null,
                    SpacePublishObjectTypes.Location,
                    SpacePublishActions.UpdateMaster,
                    SpacePublishImpactCodes.WmsRenameBlocked,
                    includeNoOp: false,
                    limit: 100,
                    cursor: null);

            Assert.False(preview.Publishable);
            Assert.Equal("Blocked", preview.ValidationStatus);
            Assert.True(preview.ValidationBlockingCount > 0);
            var rename = Assert.Single(preview.Items);
            Assert.Equal(seeded.StableLocationLogicalId, rename.LogicalId);
            Assert.Equal("R1-01", rename.BeforeCode);
            Assert.Equal("R1-RENAMED", rename.AfterCode);
            Assert.Equal(
                SpacePublishImpactCodes.WmsRenameBlocked,
                rename.ImpactCode);
            Assert.True(rename.Blocking);
            Assert.Equal(1, preview.WmsImpact.BlockingCount);
        });
    }

    private static async Task ValidateAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock,
        Guid siteId,
        Guid targetVersionId)
    {
        await using (var request = CreateContext(
                         connectionString,
                         execution,
                         clock))
        {
            var validation = new SpaceValidationService(
                request,
                execution,
                clock,
                new TestAccessEvaluator(siteId),
                new TestProfileProvider(),
                new SpaceValidationEngine());
            await validation.RequestValidationAsync(targetVersionId);
        }

        await using var worker = CreateContext(
            connectionString,
            execution,
            clock);
        var leases = new EfSpaceJobLeaseStore(worker, clock);
        var lease = await leases.TryClaimNextAsync(
            "publish-preview-test-worker",
            SpaceValidationRuleSet.ProcessorVersion,
            TimeSpan.FromMinutes(2));
        Assert.NotNull(lease);
        var runner = new SpaceJobProcessorRunner(
            leases,
            [
                new SpaceValidationJobProcessor(
                    worker,
                    clock,
                    new TestProfileProvider(),
                    new SpaceValidationEngine()),
            ],
            new SpaceJobProcessorOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(2),
                HeartbeatInterval = TimeSpan.FromSeconds(10),
            });
        await runner.RunClaimedAsync(lease!);
    }

    private static SpacePublishPreviewService NewPreviewService(
        SpaceContext context,
        TestExecutionContext execution,
        Guid siteId) =>
        new(
            context,
            execution,
            new TestAccessEvaluator(siteId),
            new TestProfileProvider(),
            new SpaceValidationEngine(),
            new SpacePublishPlanEngine(),
            new TestCursorCodec());

    private static async Task<SeededVersions> SeedVersionsAsync(
        SpaceContext context,
        bool renameLocation)
    {
        var tenantId = context.CurrentTenantId;
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        context.Add(model);
        await context.SaveChangesAsync();

        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var levelId = Guid.NewGuid();
        var stableLocationId = Guid.NewGuid();
        var removedLocationId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var baseVersion = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published base");
        var baseSource = SpaceModelSource.CreateInlineSource(
            tenantId,
            baseVersion.Id,
            SpaceSourceType.Editor,
            "Editor session",
            new string('d', 64));
        var baseFloor = CreateFloor(
            tenantId,
            baseVersion.Id,
            floorId,
            model.SiteId);
        baseFloor.AttachSource(baseSource, "floor:1");
        context.Add(baseVersion);
        context.AddRange(
            baseSource,
            baseFloor,
            CreateZone(tenantId, baseVersion.Id, zoneId, floorId),
            CreateRack(
                tenantId,
                baseVersion.Id,
                rackId,
                floorId,
                zoneId),
            CreateLevel(
                tenantId,
                baseVersion.Id,
                levelId,
                rackId),
            CreateLocation(
                tenantId,
                baseVersion.Id,
                stableLocationId,
                floorId,
                rackId,
                "R1-01",
                1,
                SpaceExternalBindingState.Bound),
            CreateLocation(
                tenantId,
                baseVersion.Id,
                removedLocationId,
                floorId,
                rackId,
                "R1-02",
                2,
                SpaceExternalBindingState.Bound),
            CreateElement(
                tenantId,
                baseVersion.Id,
                elementId,
                floorId,
                1000));
        await context.SaveChangesAsync();
        baseVersion.BeginValidation();
        baseVersion.MarkReady(
            new string('a', 64),
            SpaceValidationRuleSet.Version,
            new string('b', 64));
        baseVersion.BeginPublishing();
        baseVersion.MarkPublished(Guid.NewGuid(), DateTime.UtcNow);
        model.SetPublishedVersion(baseVersion, new string('c', 64));
        await context.SaveChangesAsync();

        var targetVersion = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "Candidate",
            baseVersion.Id);
        var targetSource = SpaceModelSource.CreateInlineSource(
            tenantId,
            targetVersion.Id,
            SpaceSourceType.Editor,
            "Editor session",
            new string('d', 64));
        var targetFloor = CreateFloor(
            tenantId,
            targetVersion.Id,
            floorId,
            model.SiteId);
        targetFloor.AttachSource(targetSource, "floor:1");
        model.ReserveDraft(targetVersion);
        context.Add(targetVersion);
        context.AddRange(
            targetSource,
            targetFloor,
            CreateZone(tenantId, targetVersion.Id, zoneId, floorId),
            CreateRack(
                tenantId,
                targetVersion.Id,
                rackId,
                floorId,
                zoneId),
            CreateLevel(
                tenantId,
                targetVersion.Id,
                levelId,
                rackId),
            CreateLocation(
                tenantId,
                targetVersion.Id,
                stableLocationId,
                floorId,
                rackId,
                renameLocation ? "R1-RENAMED" : "R1-01",
                1,
                SpaceExternalBindingState.Bound),
            CreateLocation(
                tenantId,
                targetVersion.Id,
                renameLocation ? removedLocationId : newLocationId,
                floorId,
                rackId,
                renameLocation ? "R1-02" : "R1-03",
                2,
                renameLocation
                    ? SpaceExternalBindingState.Bound
                    : SpaceExternalBindingState.Unbound),
            CreateElement(
                tenantId,
                targetVersion.Id,
                elementId,
                floorId,
                renameLocation ? 1000 : 1200));
        await context.SaveChangesAsync();
        return new SeededVersions(
            model.SiteId,
            floorId,
            baseVersion.Id,
            targetVersion.Id,
            stableLocationId,
            removedLocationId,
            newLocationId);
    }

    private static SpaceFloorRevision CreateFloor(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid siteId)
    {
        var value = SpaceFloorRevision.Create(
            tenantId,
            versionId,
            logicalId,
            siteId,
            1,
            "F1",
            "Floor 1",
            height: 5000);
        value.ConfigureBoundary(
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""",
            "LOCAL_MM_Z_UP");
        return value;
    }

    private static SpaceZoneRevision CreateZone(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid floorId)
    {
        var value = SpaceZoneRevision.Create(
            tenantId,
            versionId,
            logicalId,
            floorId,
            "Z1",
            1);
        value.ConfigureShape(
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""");
        return value;
    }

    private static SpaceRackRevision CreateRack(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid floorId,
        Guid zoneId)
    {
        var value = SpaceRackRevision.Create(
            tenantId,
            versionId,
            logicalId,
            floorId,
            zoneId,
            "R1");
        value.ConfigureGeometry(
            1000,
            1000,
            0,
            0,
            2000,
            1000,
            2000);
        return value;
    }

    private static SpaceRackLevelRevision CreateLevel(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid rackId) =>
        SpaceRackLevelRevision.Create(
            tenantId,
            versionId,
            logicalId,
            rackId,
            1,
            0,
            1800,
            2,
            1,
            1000,
            1000,
            100);

    private static SpaceLocationRevision CreateLocation(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid floorId,
        Guid rackId,
        string code,
        int column,
        SpaceExternalBindingState bindingState) =>
        SpaceLocationRevision.Create(
            tenantId,
            versionId,
            logicalId,
            floorId,
            rackId,
            code,
            column,
            1,
            1,
            1000,
            1800,
            1000,
            externalBindingState: bindingState);

    private static SpaceElementRevision CreateElement(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid floorId,
        int width) =>
        SpaceElementRevision.Create(
            tenantId,
            versionId,
            logicalId,
            floorId,
            SpaceElementTypes.Column,
            $$"""
            {"schemaVersion":1,"kind":"box","width":{{width}},"height":1000,"depth":1000}
            """);

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpacePublishPreview_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await setup.Database.MigrateAsync();
            await action(connectionString, execution, clock);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed record SeededVersions(
        Guid SiteId,
        Guid FloorLogicalId,
        Guid BaseVersionId,
        Guid TargetVersionId,
        Guid StableLocationLogicalId,
        Guid RemovedLocationLogicalId,
        Guid NewLocationLogicalId);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }

    private sealed class TestProfileProvider :
        ISpaceValidationProfileProvider
    {
        private static readonly SpaceValidationProfile Profile =
            SpaceValidationProfile.Create(
                "cp6-wms-v1",
                30,
                "^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$",
                100_000);

        public Task<SpaceValidationProfile> GetProfileAsync(
            Guid tenantId,
            Guid siteId,
            Guid correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile);
    }

    private sealed class TestAccessEvaluator(Guid allowedSiteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            if (siteId != allowedSiteId)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.TenantScopeDenied,
                    403,
                    "Site denied.");
            }
        }
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            try
            {
                var state = JsonSerializer.Deserialize<SpaceCursorState>(
                                Encoding.UTF8.GetString(
                                    Convert.FromBase64String(cursor)))
                            ?? throw new JsonException();
                if (state.Resource != expectedResource ||
                    state.FilterHash != expectedFilterHash)
                {
                    throw new SpaceProblemException(
                        SpaceErrorCodes.CursorScopeMismatch,
                        400,
                        "Cursor scope mismatch.");
                }
                return state;
            }
            catch (SpaceProblemException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is FormatException or JsonException)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.CursorInvalid,
                    400,
                    "Cursor invalid.");
            }
        }
    }
}
