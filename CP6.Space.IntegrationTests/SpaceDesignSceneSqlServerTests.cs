using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDesignSceneSqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Scene_uses_revision_authority_without_runtime_overlay()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid siteId;
            Guid versionId;
            Guid floorLogicalId;
            Guid modelAssetId;

            await using (var context = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var seeded = await SeedDesignModelAsync(
                    context,
                    execution.ActorId,
                    clock.UtcNow);
                siteId = seeded.Model.SiteId;
                var draft = SpaceModelVersion.CreateDraft(
                    execution.TenantId,
                    seeded.Model.Id,
                    2,
                    "Unified semantic scene",
                    seeded.Published.Id);
                seeded.Model.ReserveDraft(draft);

                var floor = SpaceFloorRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    siteId,
                    1,
                    "F1",
                    "Floor 1",
                    elevation: 0,
                    height: 6000);
                floor.ConfigureBoundary(
                    """
                    {"schemaVersion":1,"kind":"polygon","points":[[0,0],[10000,0],[10000,8000],[0,8000]]}
                    """,
                    "RH_Z_UP_MM");
                var zone = SpaceZoneRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    "Z1",
                    1);
                zone.ConfigureShape(
                    """
                    {"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}
                    """);
                var aisle = SpaceAisleRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    zone.LogicalId,
                    "A1",
                    1);
                aisle.ConfigureShape(
                    """
                    {"schemaVersion":1,"points":[[0,0],[1000,0],[1000,8000],[0,8000]]}
                    """,
                    """
                    {"schemaVersion":1,"points":[[500,0],[500,8000]]}
                    """);
                var rack = SpaceRackRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    zone.LogicalId,
                    "R1",
                    aisle.LogicalId);
                rack.ConfigureGeometry(
                    1000,
                    2000,
                    0,
                    90,
                    4800,
                    2200,
                    5000);
                var lower = SpaceRackLevelRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    rack.LogicalId,
                    levelNo: 1,
                    bottomZ: 0,
                    clearHeight: 1200,
                    binCount: 4,
                    depthCount: 1,
                    cellWidth: 1000,
                    cellDepth: 900,
                    maxLoad: 1500m,
                    beamHeight: 100);
                var upper = SpaceRackLevelRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    rack.LogicalId,
                    levelNo: 2,
                    bottomZ: 1300,
                    clearHeight: 800,
                    binCount: 3,
                    depthCount: 2,
                    cellWidth: 1200,
                    cellDepth: 1100,
                    maxLoad: 750m,
                    beamHeight: 80);
                var location = SpaceLocationRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    rack.LogicalId,
                    "F1-R1-01-01",
                    columnNo: 1,
                    levelNo: 1,
                    depthNo: 1,
                    width: 1000,
                    height: 1200,
                    depth: 900,
                    maxLoad: 1500m);
                var element = SpaceElementRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    SpaceElementTypes.Column,
                    """
                    {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                    """);
                modelAssetId = Guid.NewGuid();
                element.SetModelAsset(modelAssetId);
                element.ConfigurePlacement(
                    500,
                    500,
                    0,
                    0,
                    400,
                    5000,
                    400);
                var attribute = SpaceElementAttribute.Create(
                    execution.TenantId,
                    element,
                    SpaceElementAttributeNamespaces.Manufacturer,
                    "material",
                    "String",
                    "steel");

                context.AddRange(
                    draft,
                    floor,
                    zone,
                    aisle,
                    rack,
                    lower,
                    upper,
                    location,
                    element,
                    attribute);
                await context.SaveChangesAsync();

                versionId = draft.Id;
                floorLogicalId = floor.LogicalId;
                var service = NewService(
                    context,
                    execution,
                    clock,
                    siteId);
                var scene = await service.GetSceneAsync(
                    versionId,
                    floorLogicalId);

                Assert.Equal(
                    SpaceDesignSceneContract.SchemaVersion,
                    scene.SchemaVersion);
                Assert.Equal(
                    SpaceDesignSceneContract.Authority,
                    scene.Authority);
                Assert.False(scene.RuntimeOverlayIncluded);
                Assert.Equal(versionId, scene.ModelVersionId);
                Assert.Equal(siteId, scene.SiteId);
                Assert.Equal("Draft", scene.VersionStatus);
                Assert.Equal(floorLogicalId, scene.Floor.Revision.LogicalId);
                Assert.Equal("RH_Z_UP_MM", scene.Floor.CoordinateSystem);
                Assert.Single(scene.Zones);
                Assert.Single(scene.Aisles);
                Assert.Single(scene.Racks);
                Assert.Collection(
                    scene.RackLevels,
                    level =>
                    {
                        Assert.Equal(1, level.LevelNo);
                        Assert.Equal(1200, level.ClearHeight);
                        Assert.Equal(100, level.BeamHeight);
                        Assert.Equal(1500m, level.MaxLoad);
                    },
                    level =>
                    {
                        Assert.Equal(2, level.LevelNo);
                        Assert.Equal(800, level.ClearHeight);
                        Assert.Equal(2, level.DepthCount);
                        Assert.Equal(80, level.BeamHeight);
                        Assert.Equal(750m, level.MaxLoad);
                    });
                Assert.Single(scene.Locations);
                Assert.Single(scene.Elements);
                Assert.Equal(
                    SpaceElementTypes.Column,
                    scene.Elements[0].ElementType);
                Assert.Equal(
                    modelAssetId,
                    scene.Elements[0].ModelAssetId);
                Assert.Single(scene.ElementAttributes);
                Assert.Equal(
                    scene.Elements[0].Revision.RevisionId,
                    scene.ElementAttributes[0].ElementRevisionId);
                Assert.Equal(
                    SpaceElementAttributeNamespaces.Manufacturer,
                    scene.ElementAttributes[0].Namespace);

                var wrongVersionFloor =
                    await Assert.ThrowsAsync<SpaceProblemException>(
                        () => service.GetSceneAsync(
                            seeded.Published.Id,
                            floorLogicalId));
                Assert.Equal(
                    SpaceErrorCodes.LogicalIdNotFound,
                    wrongVersionFloor.Code);
                Assert.Equal(404, wrongVersionFloor.StatusCode);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using var otherContext = CreateContext(
                connectionString,
                otherExecution,
                clock);
            var otherService = NewService(
                otherContext,
                otherExecution,
                clock,
                siteId);
            var crossTenant = await Assert.ThrowsAsync<SpaceProblemException>(
                () => otherService.GetSceneAsync(
                    versionId,
                    floorLogicalId));
            Assert.Equal(
                SpaceErrorCodes.VersionNotFound,
                crossTenant.Code);
            Assert.Equal(404, crossTenant.StatusCode);
        });
    }

    private static SpaceDesignV1Service NewService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid allowedSiteId)
    {
        var cloneStore = new EfSpaceVersionCloneStore(
            context,
            execution,
            clock);
        return new SpaceDesignV1Service(
            context,
            execution,
            clock,
            new TestCursorCodec(),
            new TestAccessEvaluator(allowedSiteId),
            new SpaceVersionCloneCoordinator(execution, cloneStore),
            new SpaceSourceCoordinator(execution));
    }

    private static async Task<(SpaceModel Model, SpaceModelVersion Published)>
        SeedDesignModelAsync(
            SpaceContext context,
            Guid actorId,
            DateTime nowUtc)
    {
        var model = SpaceModel.Create(
            context.CurrentTenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            context.CurrentTenantId,
            model.Id,
            1,
            "Published baseline");
        context.AddRange(model, published);
        await context.SaveChangesAsync();

        published.BeginValidation();
        published.MarkReady(ContentHash, "space-v1", WmsHash);
        published.BeginPublishing();
        published.MarkPublished(actorId, nowUtc);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();
        return (model, published);
    }

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceScene_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
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

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
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
            throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }
}
