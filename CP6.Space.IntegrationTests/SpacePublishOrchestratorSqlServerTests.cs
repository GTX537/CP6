using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePublishOrchestratorSqlServerTests
{
    [SqlServerFact]
    public async Task Success_activates_runtime_and_partial_requires_reconciliation()
    {
        await WithDatabaseAsync(
            async (connectionString, execution, clock) =>
            {
                SeededVersions seeded;
                await using (var cp6 = CreateCp6Context(
                                 connectionString,
                                 execution.TenantId))
                {
                    seeded = await SeedCp6AndDesignAsync(
                        connectionString,
                        cp6,
                        execution,
                        clock);
                }

                ValidationEvidence firstEvidence;
                await using (var cp6 = CreateCp6Context(
                                 connectionString,
                                 execution.TenantId))
                {
                    firstEvidence = await ValidateAndPreviewAsync(
                        connectionString,
                        execution,
                        clock,
                        cp6,
                        seeded.SiteId,
                        seeded.TargetVersionId);
                }

                Guid completedAttemptId;
                await using (var space = CreateSpaceContext(
                                 connectionString,
                                 execution,
                                 clock))
                await using (var cp6 = CreateCp6Context(
                                 connectionString,
                                 execution.TenantId))
                {
                    var adapter = new Cp6SpaceWmsAdapter(cp6);
                    var orchestrator = new SpacePublishOrchestrator(
                        space,
                        execution,
                        clock,
                        new TestAccessEvaluator(seeded.SiteId),
                        new Cp6SpaceWarehouseResolver(cp6),
                        adapter,
                        new Cp6SpaceRuntimeMaterializer(
                            space,
                            execution,
                            clock),
                        new SpacePublishPlanEngine());
                    var request = new CreateSpacePublishAttemptRequest(
                        seeded.BaseVersionId,
                        firstEvidence.ValidationRunId,
                        firstEvidence.PlanHash,
                        ApprovalReference: null);

                    var queued = await orchestrator.StartAsync(
                        seeded.TargetVersionId,
                        request,
                        "publish-success");
                    var replay = await orchestrator.StartAsync(
                        seeded.TargetVersionId,
                        request,
                        "publish-success");
                    var result = await orchestrator.GetAsync(
                        queued.Attempt.Id);

                    Assert.Equal("Completed", result.Status);
                    Assert.False(queued.IdempotentReplay);
                    Assert.True(replay.IdempotentReplay);
                    Assert.Equal(
                        result.Id,
                        replay.Attempt.Id);
                    Assert.All(
                        result.Batches,
                        batch => Assert.Equal("Verified", batch.Status));
                    Assert.Equal(0, result.OpenReconciliationIssueCount);
                    completedAttemptId = result.Id;
                }

                await AssertCompletedRuntimeAsync(
                    connectionString,
                    execution,
                    clock,
                    seeded,
                    completedAttemptId);

                var thirdVersionId = await SeedNextCandidateAsync(
                    connectionString,
                    execution,
                    clock,
                    seeded);
                ValidationEvidence secondEvidence;
                await using (var cp6 = CreateCp6Context(
                                 connectionString,
                                 execution.TenantId))
                {
                    secondEvidence = await ValidateAndPreviewAsync(
                        connectionString,
                        execution,
                        clock,
                        cp6,
                        seeded.SiteId,
                        thirdVersionId);
                }

                await using (var space = CreateSpaceContext(
                                 connectionString,
                                 execution,
                                 clock))
                await using (var cp6 = CreateCp6Context(
                                 connectionString,
                                 execution.TenantId))
                {
                    var realAdapter = new Cp6SpaceWmsAdapter(cp6);
                    var noActivation = new FailIfCalledMaterializer();
                    var orchestrator = new SpacePublishOrchestrator(
                        space,
                        execution,
                        clock,
                        new TestAccessEvaluator(seeded.SiteId),
                        new Cp6SpaceWarehouseResolver(cp6),
                        new PartialAdapter(realAdapter),
                        noActivation,
                        new SpacePublishPlanEngine());

                    var queued = await orchestrator.StartAsync(
                        thirdVersionId,
                        new CreateSpacePublishAttemptRequest(
                            seeded.TargetVersionId,
                            secondEvidence.ValidationRunId,
                            secondEvidence.PlanHash,
                            ApprovalReference: null),
                        "publish-partial");
                    var result = await orchestrator.GetAsync(
                        queued.Attempt.Id);

                    Assert.Equal(
                        "ReconciliationRequired",
                        result.Status);
                    Assert.Equal("Reconcile", result.CurrentStep);
                    Assert.True(
                        result.OpenReconciliationIssueCount > 0);
                    Assert.False(noActivation.Called);
                }

                await using var verify = CreateSpaceContext(
                    connectionString,
                    execution,
                    clock);
                var model = await verify.Models.SingleAsync(
                    value => value.SiteId == seeded.SiteId);
                var third = await verify.Versions.SingleAsync(
                    value => value.Id == thirdVersionId);
                Assert.Equal(
                    seeded.TargetVersionId,
                    model.CurrentPublishedVersionId);
                Assert.Equal(
                    SpaceVersionStatus.ReconciliationRequired,
                    third.Status);
            });
    }

    private static async Task<SeededVersions> SeedCp6AndDesignAsync(
        string connectionString,
        CP6Context cp6,
        TestExecutionContext execution,
        TestClock clock)
    {
        var siteId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var levelId = Guid.NewGuid();
        var stableLocationId = Guid.NewGuid();
        var removedLocationId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        cp6.Space_Sites.Add(new Space_Site
        {
            Id = siteId,
            SiteCode = "SITE1",
            SiteName = "Publish Site",
            WarehouseCd = "WH1",
        });
        cp6.WmsBins.AddRange(
            WmsBin(stableLocationId, "R1-01"),
            WmsBin(removedLocationId, "R1-02"));
        await cp6.SaveChangesAsync();

        await using var context = CreateSpaceContext(
            connectionString,
            execution,
            clock);
        var tenantId = execution.TenantId;
        var model = SpaceModel.Create(tenantId, siteId);
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
            siteId);
        baseFloor.AttachSource(baseSource, "floor:1");
        context.AddRange(
            model,
            baseVersion,
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
                rackId,
                2),
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
        baseVersion.MarkPublished(execution.ActorId, clock.UtcNow);
        model.SetPublishedVersion(baseVersion, new string('c', 64));
        await context.SaveChangesAsync();

        var target = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "Candidate",
            baseVersion.Id);
        var targetSource = SpaceModelSource.CreateInlineSource(
            tenantId,
            target.Id,
            SpaceSourceType.Editor,
            "Editor session",
            new string('d', 64));
        var targetFloor = CreateFloor(
            tenantId,
            target.Id,
            floorId,
            siteId);
        targetFloor.AttachSource(targetSource, "floor:1");
        model.ReserveDraft(target);
        context.AddRange(
            target,
            targetSource,
            targetFloor,
            CreateZone(tenantId, target.Id, zoneId, floorId),
            CreateRack(
                tenantId,
                target.Id,
                rackId,
                floorId,
                zoneId),
            CreateLevel(
                tenantId,
                target.Id,
                levelId,
                rackId,
                2),
            CreateLocation(
                tenantId,
                target.Id,
                stableLocationId,
                floorId,
                rackId,
                "R1-01",
                1,
                SpaceExternalBindingState.Bound),
            CreateLocation(
                tenantId,
                target.Id,
                newLocationId,
                floorId,
                rackId,
                "R1-03",
                2,
                SpaceExternalBindingState.Unbound),
            CreateElement(
                tenantId,
                target.Id,
                elementId,
                floorId,
                1200));
        await context.SaveChangesAsync();
        return new SeededVersions(
            siteId,
            floorId,
            zoneId,
            rackId,
            levelId,
            baseVersion.Id,
            target.Id,
            stableLocationId,
            removedLocationId,
            newLocationId,
            elementId,
            model.Id);
    }

    private static async Task<Guid> SeedNextCandidateAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock,
        SeededVersions seeded)
    {
        await using var context = CreateSpaceContext(
            connectionString,
            execution,
            clock);
        var model = await context.Models.SingleAsync(
            value => value.Id == seeded.ModelId);
        var version = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            3,
            "Partial candidate",
            seeded.TargetVersionId);
        var source = SpaceModelSource.CreateInlineSource(
            execution.TenantId,
            version.Id,
            SpaceSourceType.Editor,
            "Editor session",
            new string('e', 64));
        var floor = CreateFloor(
            execution.TenantId,
            version.Id,
            seeded.FloorId,
            seeded.SiteId);
        floor.AttachSource(source, "floor:1");
        model.ReserveDraft(version);
        context.AddRange(
            version,
            source,
            floor,
            CreateZone(
                execution.TenantId,
                version.Id,
                seeded.ZoneId,
                seeded.FloorId),
            CreateRack(
                execution.TenantId,
                version.Id,
                seeded.RackId,
                seeded.FloorId,
                seeded.ZoneId),
            CreateLevel(
                execution.TenantId,
                version.Id,
                seeded.LevelId,
                seeded.RackId,
                4),
            CreateLocation(
                execution.TenantId,
                version.Id,
                seeded.StableLocationId,
                seeded.FloorId,
                seeded.RackId,
                "R1-01",
                1,
                SpaceExternalBindingState.Bound),
            CreateLocation(
                execution.TenantId,
                version.Id,
                seeded.NewLocationId,
                seeded.FloorId,
                seeded.RackId,
                "R1-03",
                2,
                SpaceExternalBindingState.Bound),
            CreateLocation(
                execution.TenantId,
                version.Id,
                Guid.NewGuid(),
                seeded.FloorId,
                seeded.RackId,
                "R1-04",
                3,
                SpaceExternalBindingState.Unbound),
            CreateLocation(
                execution.TenantId,
                version.Id,
                Guid.NewGuid(),
                seeded.FloorId,
                seeded.RackId,
                "R1-05",
                4,
                SpaceExternalBindingState.Unbound),
            CreateElement(
                execution.TenantId,
                version.Id,
                seeded.ElementId,
                seeded.FloorId,
                1200));
        await context.SaveChangesAsync();
        return version.Id;
    }

    private static async Task<ValidationEvidence> ValidateAndPreviewAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock,
        CP6Context cp6,
        Guid siteId,
        Guid targetVersionId)
    {
        var adapter = new Cp6SpaceWmsAdapter(cp6);
        var warehouse = new Cp6SpaceWarehouseResolver(cp6);
        var profile = new AdapterProfileProvider(adapter);
        await using (var request = CreateSpaceContext(
                         connectionString,
                         execution,
                         clock))
        {
            var validation = new SpaceValidationService(
                request,
                execution,
                clock,
                new TestAccessEvaluator(siteId),
                profile,
                new SpaceValidationEngine());
            await validation.RequestValidationAsync(targetVersionId);
        }

        await using (var worker = CreateSpaceContext(
                         connectionString,
                         execution,
                         clock))
        {
            var leases = new EfSpaceJobLeaseStore(worker, clock);
            var lease = await leases.TryClaimNextAsync(
                "publish-orchestrator-test-worker",
                SpaceValidationRuleSet.ProcessorVersion,
                TimeSpan.FromMinutes(2));
            Assert.NotNull(lease);
            var runner = new SpaceJobProcessorRunner(
                leases,
                [
                    new SpaceValidationJobProcessor(
                        worker,
                        clock,
                        profile,
                        new SpaceValidationEngine()),
                ],
                new SpaceJobProcessorOptions
                {
                    LeaseDuration = TimeSpan.FromMinutes(2),
                    HeartbeatInterval = TimeSpan.FromSeconds(10),
                });
            await runner.RunClaimedAsync(lease!);
        }

        await using var previewContext = CreateSpaceContext(
            connectionString,
            execution,
            clock);
        var preview = await new SpacePublishPreviewService(
                previewContext,
                execution,
                new TestAccessEvaluator(siteId),
                profile,
                new SpaceValidationEngine(),
                new SpacePublishPlanEngine(),
                new TestCursorCodec())
            .GetPreviewAsync(
                targetVersionId,
                null,
                null,
                null,
                null,
                includeNoOp: true,
                limit: 500,
                cursor: null);
        Assert.True(preview.Publishable);
        return new ValidationEvidence(
            preview.ValidationRunId,
            preview.PlanHash);
    }

    private static async Task AssertCompletedRuntimeAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock,
        SeededVersions seeded,
        Guid attemptId)
    {
        await using (var space = CreateSpaceContext(
                         connectionString,
                         execution,
                         clock))
        {
            var model = await space.Models.SingleAsync(
                value => value.SiteId == seeded.SiteId);
            var published = await space.Versions.SingleAsync(
                value => value.Id == seeded.TargetVersionId);
            var previous = await space.Versions.SingleAsync(
                value => value.Id == seeded.BaseVersionId);
            var attempt = await space.PublishAttempts.SingleAsync(
                value => value.Id == attemptId);
            Assert.Equal(
                seeded.TargetVersionId,
                model.CurrentPublishedVersionId);
            Assert.Equal(SpaceVersionStatus.Published, published.Status);
            Assert.Equal(SpaceVersionStatus.Superseded, previous.Status);
            Assert.Equal(
                SpacePublishAttemptStatus.Completed,
                attempt.Status);
            Assert.False(attempt.OwnsPublishSlot);
            Assert.Single(
                await space.RuntimeElements.Where(
                        value => value.SiteId == seeded.SiteId)
                    .ToArrayAsync());
        }

        await using var cp6 = CreateCp6Context(
            connectionString,
            execution.TenantId);
        var removed = await cp6.WmsBins.SingleAsync(
            value => value.Id == seeded.RemovedLocationId);
        Assert.False(removed.IsActive);
        Assert.Equal(42, removed.Version);
        Assert.True((await cp6.WmsBins.SingleAsync(
            value => value.Id == seeded.NewLocationId)).IsActive);
        Assert.Equal(
            1,
            (await cp6.Space_Locations.SingleAsync(
                value => value.Id == seeded.NewLocationId)).Status);
        Assert.Equal(
            seeded.SiteId,
            (await cp6.Space_Floors.SingleAsync(
                value => value.Id == seeded.FloorId)).SiteId);
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
            4000,
            1000,
            2000);
        return value;
    }

    private static SpaceRackLevelRevision CreateLevel(
        Guid tenantId,
        Guid versionId,
        Guid logicalId,
        Guid rackId,
        int binCount) =>
        SpaceRackLevelRevision.Create(
            tenantId,
            versionId,
            logicalId,
            rackId,
            1,
            0,
            1800,
            binCount,
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

    private static WmsBin WmsBin(Guid id, string code) =>
        new()
        {
            Id = id,
            LocationCode = code,
            WarehouseCd = "WH1",
            Version = 41,
            PathJson = "{}",
            AttrsJson = "{}",
            IsActive = true,
        };

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog =
                $"CP6SpacePublishSaga_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var cp6 = CreateCp6Context(
            connectionString,
            execution.TenantId);
        await using var space = CreateSpaceContext(
            connectionString,
            execution,
            clock);
        try
        {
            await cp6.Database.MigrateAsync();
            await space.Database.MigrateAsync();
            await action(connectionString, execution, clock);
        }
        finally
        {
            await cp6.Database.EnsureDeletedAsync();
        }
    }

    private static CP6Context CreateCp6Context(
        string connectionString,
        Guid tenantId)
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = tenantId,
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connectionString)
            .Options;
        return new CP6Context(options, tenant);
    }

    private static SpaceContext CreateSpaceContext(
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

    private sealed class PartialAdapter(ISpaceWmsAdapter inner) :
        ISpaceWmsAdapter
    {
        public string RuntimeAdapterId => inner.RuntimeAdapterId;
        public string RuntimeDataSourceId => inner.RuntimeDataSourceId;
        public SpaceWmsDataSourceKind RuntimeDataSourceKind =>
            inner.RuntimeDataSourceKind;

        public Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(
            SpaceWmsContext context,
            CancellationToken ct = default) =>
            inner.GetCapabilitiesAsync(context, ct);

        public Task<SpaceWmsHealth> CheckHealthAsync(
            SpaceWmsContext context,
            CancellationToken ct = default) =>
            inner.CheckHealthAsync(context, ct);

        public Task<SpaceWmsPreflightResult> PreflightAsync(
            SpaceWmsPreflightRequest request,
            CancellationToken ct = default) =>
            inner.PreflightAsync(request, ct);

        public Task<SpaceWmsBatchResult> ApplyBatchAsync(
            SpaceWmsBatch batch,
            CancellationToken ct = default)
        {
            Assert.True(batch.Items.Count >= 2);
            var receipts = batch.Items
                .Select((item, index) => new SpaceWmsItemReceipt(
                    item.LogicalId,
                    item.LocationCode,
                    item.Action,
                    index == 0
                        ? SpaceWmsItemOutcome.Applied
                        : SpaceWmsItemOutcome.NotApplied,
                    index == 0
                        ? item.LogicalId.ToString("D")
                        : null,
                    index == 0
                        ? item.Version.ToString()
                        : null,
                    index == 0 ? new string('a', 64) : null,
                    index == 0 ? null : "INJECTED_PARTIAL"))
                .ToArray();
            return Task.FromResult(
                new SpaceWmsBatchResult(
                    batch.OperationKey,
                    batch.PayloadHash,
                    "partial-operation",
                    receipts,
                    DateTimeOffset.UtcNow));
        }

        public Task<SpaceWmsOperationStatus> GetOperationStatusAsync(
            SpaceWmsOperationQuery request,
            CancellationToken ct = default) =>
            inner.GetOperationStatusAsync(request, ct);

        public Task<SpaceWmsReadBackResult> ReadBackAsync(
            SpaceWmsReadBackRequest request,
            CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "Partial results must not enter readback verification.");

        public Task<SpaceWmsBlockingReferences>
            GetBlockingReferencesAsync(
                SpaceWmsBlockingReferencesRequest request,
                CancellationToken ct = default) =>
            inner.GetBlockingReferencesAsync(request, ct);

        public Task<SpaceWmsLocationResult> QueryLocationsAsync(
            SpaceWmsLocationQuery request,
            CancellationToken ct = default) =>
            inner.QueryLocationsAsync(request, ct);

        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
            SpaceWmsInventoryQuery request,
            CancellationToken ct = default) =>
            inner.QueryInventoryAsync(request, ct);

        public Task<SpaceWmsTaskResult> QueryTasksAsync(
            SpaceWmsTaskQuery request,
            CancellationToken ct = default) =>
            inner.QueryTasksAsync(request, ct);

        public Task<SpaceWmsAbcResult> QueryAbcAsync(
            SpaceWmsAbcQuery request,
            CancellationToken ct = default) =>
            inner.QueryAbcAsync(request, ct);
    }

    private sealed class FailIfCalledMaterializer :
        ISpaceRuntimeMaterializer
    {
        public bool Called { get; private set; }

        public Task<SpaceRuntimeActivationResult> ActivateAsync(
            SpaceRuntimeActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException(
                "Runtime activation must not follow a partial WMS result.");
        }
    }

    private sealed record SeededVersions(
        Guid SiteId,
        Guid FloorId,
        Guid ZoneId,
        Guid RackId,
        Guid LevelId,
        Guid BaseVersionId,
        Guid TargetVersionId,
        Guid StableLocationId,
        Guid RemovedLocationId,
        Guid NewLocationId,
        Guid ElementId,
        Guid ModelId);

    private sealed record ValidationEvidence(
        Guid ValidationRunId,
        string PlanHash);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext;

    private sealed class AdapterProfileProvider(ISpaceWmsAdapter adapter) :
        ISpaceValidationProfileProvider
    {
        public async Task<SpaceValidationProfile> GetProfileAsync(
            Guid tenantId,
            Guid siteId,
            Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            var capabilities = await adapter.GetCapabilitiesAsync(
                new SpaceWmsContext(
                    tenantId,
                    siteId,
                    "WH1",
                    correlationId),
                cancellationToken);
            return SpaceValidationProfile.FromCapabilities(capabilities);
        }
    }

    private sealed class TestClock : ISpaceClock
    {
        private DateTime _utcNow = DateTime.UtcNow;

        public DateTime UtcNow => _utcNow;

        public void Advance(TimeSpan duration) =>
            _utcNow = _utcNow.Add(duration);
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
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(state)));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
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
    }
}
