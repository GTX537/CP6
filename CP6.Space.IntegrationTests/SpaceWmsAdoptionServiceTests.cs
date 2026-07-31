using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceWmsAdoptionServiceTests
{
    private const string PlanHash =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTime Now =
        new(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Refresh_and_bind_preserve_wms_identity_and_code()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 1);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-A-01"));

        var refresh = await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                SpaceErrorCodes.WmsLocationUnbound,
                50,
                null)).Items);

        Assert.Equal(1, refresh.DiscoveredCount);
        Assert.Equal("Simulated", refresh.DataSourceKind);
        Assert.Equal("WMS-A-01", discovered.WmsLocationCode);
        Assert.Equal(
            SpaceErrorCodes.WmsLocationUnbound,
            discovered.DifferenceCode);
        var openBeforeBind = Assert.Single(
            await fixture.Context.Issues
                .Where(value =>
                    value.ModelVersionId == fixture.VersionId &&
                    value.Status == SpaceIssueStatus.Open)
                .ToListAsync());
        Assert.Equal(SpaceErrorCodes.WmsLocationUnbound, openBeforeBind.Code);
        Assert.Equal(SpaceIssueSeverity.Warning, openBeforeBind.Severity);

        var response = await fixture.Service.BindAsync(
            fixture.VersionId,
            discovered.Id,
            new BindSpaceWmsAdoptionRequest(
                fixture.LocationIds[0],
                discovered.RowVersion));

        var bound = Assert.Single(response.Items);
        Assert.Equal("Bound", bound.Status);
        Assert.Equal("WMS-A-01", bound.SpaceLocationCode);
        Assert.Null(bound.DifferenceCode);
        Assert.Equal(1, response.ContentRevision);
        Assert.Equal(0, response.OpenWarningCount);
        Assert.Equal(0, response.OpenBlockingCount);
        Assert.Empty(
            await fixture.Context.Issues
                .Where(value =>
                    value.ModelVersionId == fixture.VersionId &&
                    value.Status == SpaceIssueStatus.Open)
                .ToListAsync());
        var location = await fixture.Context.LocationRevisions.SingleAsync(
            value =>
                value.ModelVersionId == fixture.VersionId &&
                value.LogicalId == fixture.LocationIds[0]);
        Assert.Equal(SpaceLocationCodeOrigin.Adopted, location.CodeOrigin);
        Assert.Equal(
            SpaceExternalBindingState.Bound,
            location.ExternalBindingState);
    }

    [Fact]
    public async Task Place_uses_wms_identity_and_rack_level_dimensions()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 0);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-P-01"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null)).Items);

        var response = await fixture.Service.PlaceAsync(
            fixture.VersionId,
            discovered.Id,
            new PlaceSpaceWmsAdoptionRequest(
                fixture.FloorLogicalId,
                fixture.RackLogicalId,
                Column: 2,
                Level: 1,
                Depth: 1,
                discovered.RowVersion));

        var placed = Assert.Single(response.Items);
        Assert.Equal(wmsLogicalId, placed.LocationLogicalId);
        Assert.True(placed.HasGeometry);
        Assert.Null(placed.DifferenceCode);
        var location = await fixture.Context.LocationRevisions.SingleAsync(
            value =>
                value.ModelVersionId == fixture.VersionId &&
                value.LogicalId == wmsLogicalId);
        Assert.Equal(1_000, location.Width);
        Assert.Equal(1_200, location.Height);
        Assert.Equal(1_100, location.Depth);
        Assert.Equal(900m, location.MaxLoad);
    }

    [Fact]
    public async Task Batch_binding_conflict_has_no_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 2);
        await fixture.SeedWmsAsync(
            (Guid.NewGuid(), "WMS-B-01"),
            (Guid.NewGuid(), "WMS-B-02"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null))
            .Items
            .OrderBy(value => value.WmsLocationCode)
            .ToArray();

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.BindBatchAsync(
                fixture.VersionId,
                new BatchBindSpaceWmsAdoptionRequest(
                [
                    new(
                        discovered[0].Id,
                        fixture.LocationIds[0],
                        discovered[0].RowVersion),
                    new(
                        discovered[1].Id,
                        fixture.LocationIds[0],
                        discovered[1].RowVersion),
                ])));

        Assert.Equal(SpaceErrorCodes.WmsAdoptionDuplicate, exception.Code);
        Assert.All(
            await fixture.Context.WmsAdoptions.ToListAsync(),
            value => Assert.Equal(
                SpaceWmsAdoptionStatus.Unbound,
                value.Status));
        Assert.All(
            await fixture.Context.LocationRevisions
                .Where(value => value.ModelVersionId == fixture.VersionId)
                .ToListAsync(),
            value => Assert.Equal(
                SpaceExternalBindingState.Unbound,
                value.ExternalBindingState));
    }

    [Fact]
    public async Task Refresh_tracks_code_drift_then_missing_as_blocking_issues()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 1);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-D-01"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null)).Items);
        await fixture.Service.BindAsync(
            fixture.VersionId,
            discovered.Id,
            new BindSpaceWmsAdoptionRequest(
                fixture.LocationIds[0],
                discovered.RowVersion));

        await fixture.UpdateWmsAsync(wmsLogicalId, "WMS-D-02");
        await fixture.Service.RefreshAsync(fixture.VersionId);

        var diverged = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                "Diverged",
                null,
                50,
                null)).Items);
        Assert.Equal(
            SpaceErrorCodes.WmsBindingCodeMismatch,
            diverged.DifferenceCode);
        var driftIssue = Assert.Single(
            await fixture.Context.Issues
                .Where(value =>
                    value.ModelVersionId == fixture.VersionId &&
                    value.Status == SpaceIssueStatus.Open)
                .ToListAsync());
        Assert.Equal(SpaceErrorCodes.WmsBindingCodeMismatch, driftIssue.Code);
        Assert.Equal(SpaceIssueSeverity.Blocking, driftIssue.Severity);

        fixture.ResetWms();
        await fixture.Service.RefreshAsync(fixture.VersionId);

        var missing = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                "MissingInWms",
                null,
                50,
                null)).Items);
        Assert.Equal(SpaceErrorCodes.WmsLocationMissing, missing.DifferenceCode);
        var missingIssue = Assert.Single(
            await fixture.Context.Issues
                .Where(value =>
                    value.ModelVersionId == fixture.VersionId &&
                    value.Status == SpaceIssueStatus.Open)
                .ToListAsync());
        Assert.Equal(SpaceErrorCodes.WmsLocationMissing, missingIssue.Code);
        Assert.Equal(SpaceIssueSeverity.Blocking, missingIssue.Severity);
    }

    [Fact]
    public async Task Ready_version_rejects_binding_without_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 1);
        await fixture.SeedWmsAsync((Guid.NewGuid(), "WMS-R-01"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null)).Items);
        var version = await fixture.Context.Versions.SingleAsync(
            value => value.Id == fixture.VersionId);
        version.BeginValidation();
        version.MarkReady(ContentHash, "space-v1", WmsHash);
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.BindAsync(
                fixture.VersionId,
                discovered.Id,
                new BindSpaceWmsAdoptionRequest(
                    fixture.LocationIds[0],
                    discovered.RowVersion)));

        Assert.Equal(SpaceErrorCodes.VersionStateInvalid, exception.Code);
        Assert.Equal(
            SpaceExternalBindingState.Unbound,
            (await fixture.Context.LocationRevisions.SingleAsync(
                value =>
                    value.ModelVersionId == fixture.VersionId &&
                    value.LogicalId == fixture.LocationIds[0]))
            .ExternalBindingState);
    }

    [Fact]
    public async Task Inactive_wms_location_is_blocking_and_cannot_be_bound()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 1);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-I-01"));
        await fixture.DisableWmsAsync(wmsLogicalId, "WMS-I-01");
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var inactive = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                SpaceErrorCodes.WmsLocationMissing,
                50,
                null)).Items);

        Assert.False(inactive.WmsIsActive);
        var issue = Assert.Single(
            await fixture.Context.Issues
                .Where(value => value.Status == SpaceIssueStatus.Open)
                .ToListAsync());
        Assert.Equal(SpaceIssueSeverity.Blocking, issue.Severity);
        Assert.Equal(SpaceErrorCodes.WmsLocationMissing, issue.Code);
        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.BindAsync(
                fixture.VersionId,
                inactive.Id,
                new BindSpaceWmsAdoptionRequest(
                    fixture.LocationIds[0],
                    inactive.RowVersion)));
        Assert.Equal(SpaceErrorCodes.WmsAdoptionMissing, exception.Code);
    }

    [Fact]
    public async Task Retired_geometry_cannot_be_adopted()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 1);
        await fixture.SeedWmsAsync((Guid.NewGuid(), "WMS-X-01"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null)).Items);
        var location = await fixture.Context.LocationRevisions.SingleAsync(
            value =>
                value.ModelVersionId == fixture.VersionId &&
                value.LogicalId == fixture.LocationIds[0]);
        location.ChangeLifecycle(SpaceLifecycleState.Disabled);
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.BindAsync(
                fixture.VersionId,
                discovered.Id,
                new BindSpaceWmsAdoptionRequest(
                    fixture.LocationIds[0],
                    discovered.RowVersion)));

        Assert.Equal(
            SpaceErrorCodes.WmsBindingGeometryMissing,
            exception.Code);
        Assert.Equal(SpaceWmsAdoptionStatus.Unbound, (
            await fixture.Context.WmsAdoptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unavailable_wms_fails_closed_without_catalog_writes()
    {
        await using var fixture = await Fixture.CreateAsync(locationCount: 0);
        fixture.ConfigureWmsFault(SpaceWmsSimulatorFaultMode.Unavailable);

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.RefreshAsync(fixture.VersionId));

        Assert.Equal(SpaceErrorCodes.WmsUnavailable, exception.Code);
        Assert.Equal(503, exception.StatusCode);
        Assert.Empty(await fixture.Context.WmsAdoptions.ToListAsync());
        Assert.Empty(await fixture.Context.Issues.ToListAsync());
    }

    [SqlServerFact]
    public async Task SqlServer_refresh_and_bind_persist_rowversion_and_identity()
    {
        await using var fixture = await Fixture.CreateSqlAsync(locationCount: 1);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-SQL-01"));

        var refresh = await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                SpaceErrorCodes.WmsLocationUnbound,
                50,
                null)).Items);

        Assert.Equal("Simulated", refresh.DataSourceKind);
        Assert.False(string.IsNullOrWhiteSpace(discovered.RowVersion));
        var response = await fixture.Service.BindAsync(
            fixture.VersionId,
            discovered.Id,
            new BindSpaceWmsAdoptionRequest(
                fixture.LocationIds[0],
                discovered.RowVersion));

        var bound = Assert.Single(response.Items);
        Assert.Equal(wmsLogicalId, bound.WmsLogicalId);
        Assert.Equal("WMS-SQL-01", bound.SpaceLocationCode);
        Assert.Equal("Bound", bound.Status);
        Assert.Equal(0, response.OpenWarningCount);
        Assert.Equal(0, response.OpenBlockingCount);
    }

    [SqlServerFact]
    public async Task SqlServer_place_uses_wms_identity_and_level_dimensions()
    {
        await using var fixture = await Fixture.CreateSqlAsync(locationCount: 0);
        var wmsLogicalId = Guid.NewGuid();
        await fixture.SeedWmsAsync((wmsLogicalId, "WMS-SQL-P-01"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = Assert.Single(
            (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null)).Items);

        await fixture.Service.PlaceAsync(
            fixture.VersionId,
            discovered.Id,
            new PlaceSpaceWmsAdoptionRequest(
                fixture.FloorLogicalId,
                fixture.RackLogicalId,
                Column: 3,
                Level: 1,
                Depth: 1,
                discovered.RowVersion));

        var location = await fixture.Context.LocationRevisions.SingleAsync(
            value =>
                value.ModelVersionId == fixture.VersionId &&
                value.LogicalId == wmsLogicalId);
        Assert.Equal("WMS-SQL-P-01", location.LocationCode);
        Assert.Equal(1_000, location.Width);
        Assert.Equal(1_200, location.Height);
        Assert.Equal(1_100, location.Depth);
        Assert.Equal(900m, location.MaxLoad);
    }

    [SqlServerFact]
    public async Task SqlServer_batch_conflict_is_atomic()
    {
        await using var fixture = await Fixture.CreateSqlAsync(locationCount: 2);
        await fixture.SeedWmsAsync(
            (Guid.NewGuid(), "WMS-SQL-B-01"),
            (Guid.NewGuid(), "WMS-SQL-B-02"));
        await fixture.Service.RefreshAsync(fixture.VersionId);
        var discovered = (await fixture.Service.GetLocationsAsync(
                fixture.VersionId,
                null,
                null,
                50,
                null))
            .Items
            .OrderBy(value => value.WmsLocationCode)
            .ToArray();

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.BindBatchAsync(
                fixture.VersionId,
                new BatchBindSpaceWmsAdoptionRequest(
                [
                    new(
                        discovered[0].Id,
                        fixture.LocationIds[0],
                        discovered[0].RowVersion),
                    new(
                        discovered[1].Id,
                        fixture.LocationIds[0],
                        discovered[1].RowVersion),
                ])));

        Assert.Equal(SpaceErrorCodes.WmsAdoptionDuplicate, exception.Code);
        Assert.All(
            await fixture.Context.WmsAdoptions.AsNoTracking().ToListAsync(),
            value => Assert.Equal(
                SpaceWmsAdoptionStatus.Unbound,
                value.Status));
        Assert.All(
            await fixture.Context.LocationRevisions
                .AsNoTracking()
                .Where(value => value.ModelVersionId == fixture.VersionId)
                .ToListAsync(),
            value => Assert.Equal(
                SpaceExternalBindingState.Unbound,
                value.ExternalBindingState));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            StandardSpaceWmsSimulator simulator,
            SpaceWmsAdoptionService service,
            TestExecutionContext execution,
            Guid siteId,
            Guid versionId,
            Guid floorLogicalId,
            Guid rackLogicalId,
            Guid[] locationIds,
            bool deleteDatabase)
        {
            Context = context;
            Simulator = simulator;
            Service = service;
            Execution = execution;
            SiteId = siteId;
            VersionId = versionId;
            FloorLogicalId = floorLogicalId;
            RackLogicalId = rackLogicalId;
            LocationIds = locationIds;
            DeleteDatabase = deleteDatabase;
        }

        public SpaceContext Context { get; }
        public StandardSpaceWmsSimulator Simulator { get; }
        public SpaceWmsAdoptionService Service { get; }
        public TestExecutionContext Execution { get; }
        public Guid SiteId { get; }
        public Guid VersionId { get; }
        public Guid FloorLogicalId { get; }
        public Guid RackLogicalId { get; }
        public Guid[] LocationIds { get; }
        private bool DeleteDatabase { get; }

        public static async Task<Fixture> CreateAsync(int locationCount)
        {
            var execution = new TestExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid());
            var clock = new TestClock();
            var options = new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase($"space-wms-adoption-{Guid.NewGuid():N}")
                .Options;
            return await CreateCoreAsync(
                options,
                execution,
                clock,
                locationCount,
                deleteDatabase: false);
        }

        public static async Task<Fixture> CreateSqlAsync(int locationCount)
        {
            var baseConnection = Environment.GetEnvironmentVariable(
                SqlServerFactAttribute.EnvVar)!;
            var connectionString = new SqlConnectionStringBuilder(
                baseConnection)
            {
                InitialCatalog =
                    $"CP6SpaceWmsAdoption_{Guid.NewGuid():N}",
                TrustServerCertificate = true,
            }.ConnectionString;
            var execution = new TestExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid());
            var clock = new TestClock();
            var options = new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options;
            return await CreateCoreAsync(
                options,
                execution,
                clock,
                locationCount,
                deleteDatabase: true);
        }

        private static async Task<Fixture> CreateCoreAsync(
            DbContextOptions<SpaceContext> options,
            TestExecutionContext execution,
            TestClock clock,
            int locationCount,
            bool deleteDatabase)
        {
            var context = new SpaceContext(options, execution, clock);
            try
            {
                if (context.Database.IsRelational())
                    await context.Database.MigrateAsync();
                var seeded = await SeedDesignAsync(context, locationCount);
                var simulator = new StandardSpaceWmsSimulator();
                var service = new SpaceWmsAdoptionService(
                    context,
                    execution,
                    clock,
                    new TestAccessEvaluator(seeded.SiteId),
                    new TestCursorCodec(),
                    simulator,
                    new TestWarehouseResolver(seeded.SiteId));
                return new Fixture(
                    context,
                    simulator,
                    service,
                    execution,
                    seeded.SiteId,
                    seeded.VersionId,
                    seeded.FloorLogicalId,
                    seeded.RackLogicalId,
                    seeded.LocationIds,
                    deleteDatabase);
            }
            catch
            {
                if (deleteDatabase)
                    await context.Database.EnsureDeletedAsync();
                await context.DisposeAsync();
                throw;
            }
        }

        public async Task SeedWmsAsync(
            params (Guid LogicalId, string Code)[] locations)
        {
            var mutations = locations
                .Select((value, index) => SpaceWmsLocationMutation.Create(
                    index + 1,
                    value.LogicalId,
                    value.Code,
                    SpaceWmsLocationAction.Create,
                    new SpaceWmsLocationPath(
                        "SITE",
                        1,
                        "STORAGE",
                        "A01",
                        "RACK-01",
                        index + 1,
                        1,
                        1),
                    version: 1))
                .ToArray();
            await Simulator.ApplyBatchAsync(
                SpaceWmsBatch.Create(
                    WmsContext(),
                    Guid.NewGuid(),
                    1,
                    PlanHash,
                    mutations));
        }

        public Task UpdateWmsAsync(Guid logicalId, string code) =>
            Simulator.ApplyBatchAsync(
                SpaceWmsBatch.Create(
                    WmsContext(),
                    Guid.NewGuid(),
                    1,
                    PlanHash,
                    [
                        SpaceWmsLocationMutation.Create(
                            1,
                            logicalId,
                            code,
                            SpaceWmsLocationAction.Update,
                            new SpaceWmsLocationPath(
                                "SITE",
                                1,
                                "STORAGE",
                                "A01",
                                "RACK-01",
                                1,
                                1,
                                1),
                            version: 2),
                    ]));

        public Task DisableWmsAsync(Guid logicalId, string code) =>
            Simulator.ApplyBatchAsync(
                SpaceWmsBatch.Create(
                    WmsContext(),
                    Guid.NewGuid(),
                    1,
                    PlanHash,
                    [
                        SpaceWmsLocationMutation.Create(
                            1,
                            logicalId,
                            code,
                            SpaceWmsLocationAction.Disable,
                            new SpaceWmsLocationPath(
                                "SITE",
                                1,
                                "STORAGE",
                                "A01",
                                "RACK-01",
                                1,
                                1,
                                1),
                            version: 2),
                    ]));

        public void ResetWms() => Simulator.Reset(WmsContext());

        public void ConfigureWmsFault(SpaceWmsSimulatorFaultMode mode) =>
            Simulator.ConfigureFault(
                WmsContext(),
                new SpaceWmsSimulatorFaultProfile(mode));

        private SpaceWmsContext WmsContext() =>
            new(
                Execution.TenantId,
                SiteId,
                "WH1",
                Execution.CorrelationId);

        public async ValueTask DisposeAsync()
        {
            if (DeleteDatabase)
                await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }

    private static async Task<SeededDesign> SeedDesignAsync(
        SpaceContext context,
        int locationCount)
    {
        var tenantId = context.CurrentTenantId;
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published baseline");
        context.AddRange(model, published);
        await context.SaveChangesAsync();
        published.BeginValidation();
        published.MarkReady(ContentHash, "space-v1", WmsHash);
        published.BeginPublishing();
        published.MarkPublished(Guid.NewGuid(), Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();

        var draft = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "Brownfield adoption");
        model.ReserveDraft(draft);
        var floorLogicalId = Guid.NewGuid();
        var zoneLogicalId = Guid.NewGuid();
        var rackLogicalId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId,
            draft.Id,
            floorLogicalId,
            siteId,
            1,
            "F1",
            "Floor 1",
            height: 5_000);
        var zone = SpaceZoneRevision.Create(
            tenantId,
            draft.Id,
            zoneLogicalId,
            floorLogicalId,
            "STORAGE",
            zoneType: 1);
        var rack = SpaceRackRevision.Create(
            tenantId,
            draft.Id,
            rackLogicalId,
            floorLogicalId,
            zoneLogicalId,
            "RACK-01");
        rack.ConfigureGeometry(
            0,
            0,
            0,
            0,
            width: 4_000,
            depth: 1_100,
            height: 4_000);
        var level = SpaceRackLevelRevision.Create(
            tenantId,
            draft.Id,
            Guid.NewGuid(),
            rackLogicalId,
            levelNo: 1,
            bottomZ: 0,
            clearHeight: 1_200,
            binCount: 4,
            depthCount: 1,
            cellWidth: 1_000,
            cellDepth: 1_100,
            maxLoad: 900m);
        var locationIds = Enumerable.Range(1, locationCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var locations = locationIds
            .Select((logicalId, index) =>
                SpaceLocationRevision.Create(
                    tenantId,
                    draft.Id,
                    logicalId,
                    floorLogicalId,
                    rackLogicalId,
                    $"GENERATED-{index + 1:00}",
                    columnNo: index + 1,
                    levelNo: 1,
                    depthNo: 1,
                    width: 1_000,
                    height: 1_200,
                    depth: 1_100,
                    maxLoad: 900m))
            .ToArray();
        context.AddRange(draft, floor, zone, rack, level);
        context.AddRange(locations);
        await context.SaveChangesAsync();
        return new SeededDesign(
            siteId,
            draft.Id,
            floorLogicalId,
            rackLogicalId,
            locationIds);
    }

    private sealed record SeededDesign(
        Guid SiteId,
        Guid VersionId,
        Guid FloorLogicalId,
        Guid RackLogicalId,
        Guid[] LocationIds);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext
    {
        public Guid CorrelationId { get; } = Guid.NewGuid();
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class TestAccessEvaluator(Guid allowedSiteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            if (siteId != allowedSiteId)
                throw new InvalidOperationException("Site access denied.");
        }
    }

    private sealed class TestWarehouseResolver(Guid siteId) :
        ISpaceWarehouseResolver
    {
        public Task<SpaceWarehouseIdentity?> ResolveAsync(
            Guid requestedSiteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceWarehouseIdentity?>(
                requestedSiteId == siteId
                    ? new SpaceWarehouseIdentity(siteId, "SITE", "WH1")
                    : null);
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
            var state = JsonSerializer.Deserialize<SpaceCursorState>(
                Encoding.UTF8.GetString(Convert.FromBase64String(cursor)))
                ?? throw new FormatException();
            if (state.Resource != expectedResource ||
                state.FilterHash != expectedFilterHash)
            {
                throw new FormatException();
            }
            return state;
        }
    }
}
