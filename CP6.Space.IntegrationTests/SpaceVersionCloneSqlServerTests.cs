using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceVersionCloneSqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task Queued_clone_cancellation_releases_the_reserved_draft()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var clock = new TestClock();
        await using var context = CreateInMemoryContext(tenantId, actorId, clock);
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var target = SpaceModelVersion.CreateInitializingClone(
            tenantId,
            model.Id,
            2,
            "Cancelled clone",
            Guid.NewGuid(),
            Guid.NewGuid());
        model.ReserveDraft(target);
        var job = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.CloneVersion,
            SpaceJobSubjectType.ModelVersion,
            target.Id,
            new string('c', 64),
            new string('d', 64),
            50,
            3,
            actorId,
            clock.UtcNow,
            Guid.NewGuid());
        context.AddRange(model, target, job);
        await context.SaveChangesAsync();

        job.RequestCancellation(actorId, clock.UtcNow);
        await new EfSpaceJobQueue(context).SaveChangesAsync();

        Assert.Equal(SpaceJobStatus.Cancelled, job.Status);
        Assert.Equal(SpaceVersionStatus.Abandoned, target.Status);
        Assert.Null(model.ActiveDraftVersionId);
    }

    [Fact]
    public async Task Published_snapshot_rows_are_immutable_in_the_context()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var clock = new TestClock();
        await using var context = CreateInMemoryContext(tenantId, actorId, clock);
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published");
        context.AddRange(model, published);
        await context.SaveChangesAsync();
        published.BeginValidation();
        published.MarkReady(ContentHash, "space-v1", WmsHash);
        published.BeginPublishing();
        published.MarkPublished(actorId, clock.UtcNow);
        await context.SaveChangesAsync();

        context.FloorRevisions.Add(
            SpaceFloorRevision.Create(
                tenantId,
                published.Id,
                Guid.NewGuid(),
                model.SiteId,
                1,
                "F1",
                "Floor 1"));

        await Assert.ThrowsAsync<SpaceVersionStateException>(
            () => context.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Migration_creates_clone_snapshot_tables_and_column()
    {
        await WithDatabaseAsync(async (context, _, _) =>
        {
            var tableNames = await ReadTableNamesAsync(context);
            Assert.Contains("Space_FloorRevision", tableNames);
            Assert.Contains("Space_ZoneRevision", tableNames);
            Assert.Contains("Space_AisleRevision", tableNames);
            Assert.Contains("Space_RackRevision", tableNames);
            Assert.Contains("Space_RackLevelRevision", tableNames);
            Assert.Contains("Space_LocationRevision", tableNames);
            Assert.Contains("Space_ElementRevision", tableNames);
            Assert.Contains("Space_ElementAttribute", tableNames);

            var cloneColumn = await context.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT [name] AS [Value]
                    FROM sys.columns
                    WHERE [object_id] = OBJECT_ID('Space_ModelVersion')
                      AND [name] = 'CloneOperationId'
                    """)
                .SingleAsync();
            Assert.Equal("CloneOperationId", cloneColumn);
        });
    }

    [SqlServerFact]
    public async Task Empty_published_warehouse_clones_to_an_empty_draft()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var (model, published) = await SeedPublishedAsync(
                context,
                execution.ActorId,
                includeSnapshot: false);
            var started = await StartAndProcessAsync(
                context,
                execution,
                clock,
                model.Id,
                "Empty clone");

            context.ChangeTracker.Clear();
            var target = await context.Versions.SingleAsync(
                version => version.Id == started.Result.ModelVersionId);
            var reloadedModel = await context.Models.SingleAsync(
                candidate => candidate.Id == model.Id);
            var source = await context.Versions.SingleAsync(
                version => version.Id == published.Id);
            var job = await context.Jobs.SingleAsync(
                candidate => candidate.Id == started.Result.JobId);

            Assert.Equal(0, started.Counts.Total);
            Assert.Equal(SpaceVersionStatus.Draft, target.Status);
            Assert.Equal(published.Id, target.BasedOnVersionId);
            Assert.Equal(target.Id, reloadedModel.ActiveDraftVersionId);
            Assert.Equal(published.Id, reloadedModel.CurrentPublishedVersionId);
            Assert.Equal(SpaceVersionStatus.Published, source.Status);
            Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
        });
    }

    [SqlServerFact]
    public async Task Clone_remaps_row_ids_and_preserves_logical_identity()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var (model, published) = await SeedPublishedAsync(
                context,
                execution.ActorId,
                includeSnapshot: true);
            var sourceFloor = await context.FloorRevisions
                .AsNoTracking()
                .SingleAsync(row => row.ModelVersionId == published.Id);
            var sourceElement = await context.ElementRevisions
                .AsNoTracking()
                .SingleAsync(row => row.ModelVersionId == published.Id);
            var sourceSource = await context.Sources
                .AsNoTracking()
                .SingleAsync(row => row.ModelVersionId == published.Id);

            var started = await StartAndProcessAsync(
                context,
                execution,
                clock,
                model.Id,
                "Full clone");
            context.ChangeTracker.Clear();

            var targetSource = await context.Sources.SingleAsync(
                row => row.ModelVersionId == started.Result.ModelVersionId);
            var targetFloor = await context.FloorRevisions.SingleAsync(
                row => row.ModelVersionId == started.Result.ModelVersionId);
            var targetElement = await context.ElementRevisions.SingleAsync(
                row => row.ModelVersionId == started.Result.ModelVersionId);
            var targetAttribute = await context.ElementAttributes.SingleAsync(
                row => row.ModelVersionId == started.Result.ModelVersionId);

            Assert.Equal(9, started.Counts.Total);
            Assert.NotEqual(sourceSource.Id, targetSource.Id);
            Assert.Equal(sourceSource.Sha256, targetSource.Sha256);
            Assert.NotEqual(sourceFloor.Id, targetFloor.Id);
            Assert.Equal(sourceFloor.LogicalId, targetFloor.LogicalId);
            Assert.Equal(targetSource.Id, targetFloor.SourceId);
            Assert.NotEqual(sourceElement.Id, targetElement.Id);
            Assert.Equal(sourceElement.LogicalId, targetElement.LogicalId);
            Assert.Equal(sourceElement.ModelAssetId, targetElement.ModelAssetId);
            Assert.Equal(targetElement.Id, targetAttribute.ElementRevisionId);
        });
    }

    [SqlServerFact]
    public async Task Duplicate_operation_returns_the_same_reservation_and_job()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var (model, _) = await SeedPublishedAsync(
                context,
                execution.ActorId,
                includeSnapshot: false);
            var operationId = Guid.NewGuid();
            var store = new EfSpaceVersionCloneStore(context, execution, clock);
            var request = new SpaceVersionCloneRequest(
                model.Id,
                "Idempotent clone",
                operationId);

            var first = await store.StartAsync(request);
            var duplicate = await store.StartAsync(request);

            Assert.False(first.Reused);
            Assert.True(duplicate.Reused);
            Assert.Equal(first.ModelVersionId, duplicate.ModelVersionId);
            Assert.Equal(first.JobId, duplicate.JobId);
            Assert.Single(await context.Versions.Where(
                version => version.CloneOperationId == operationId).ToListAsync());
            await Assert.ThrowsAsync<SpaceVersionConflictException>(() =>
                store.StartAsync(request with { Name = "Different input" }));
        });
    }

    private static async Task<(
        SpaceVersionCloneStartResult Result,
        SpaceVersionCloneCounts Counts)> StartAndProcessAsync(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid modelId,
        string name)
    {
        var result = await new EfSpaceVersionCloneStore(context, execution, clock)
            .StartAsync(
                new SpaceVersionCloneRequest(modelId, name, Guid.NewGuid()));
        var leaseStore = new EfSpaceJobLeaseStore(context, clock);
        var lease = await leaseStore.TryClaimNextAsync(
            "clone-worker",
            "space-clone-v1",
            TimeSpan.FromMinutes(2));
        var counts = await new EfSpaceVersionCloneProcessor(
                context,
                clock,
                leaseStore)
            .ProcessAsync(lease!);
        return (result, counts);
    }

    private static async Task<(SpaceModel Model, SpaceModelVersion Published)>
        SeedPublishedAsync(
            SpaceContext context,
            Guid actorId,
            bool includeSnapshot)
    {
        var tenantId = context.CurrentTenantId;
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        context.Models.Add(model);
        await context.SaveChangesAsync();
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published");
        context.Versions.Add(version);

        if (includeSnapshot)
        {
            var source = SpaceModelSource.CreateInlineSource(
                tenantId,
                version.Id,
                SpaceSourceType.Editor,
                "Editor",
                new string('c', 64));
            var floor = SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                model.SiteId,
                1,
                "F1",
                "Floor 1");
            floor.AttachSource(source, "editor:floor-1");
            var zone = SpaceZoneRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                "Z1",
                0);
            var aisle = SpaceAisleRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                zone.LogicalId,
                "A1",
                0);
            var rack = SpaceRackRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                zone.LogicalId,
                "R1",
                aisle.LogicalId);
            rack.ConfigureGeometry(100, 200, 0, 90, 1000, 800, 5000);
            var rackLevel = SpaceRackLevelRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                rack.LogicalId,
                1,
                0,
                1000,
                1,
                1,
                1000,
                800,
                1250.5m);
            var location = SpaceLocationRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                "F1-R1-01",
                1,
                1,
                1,
                1000,
                1000,
                800);
            var element = SpaceElementRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                "Column",
                """{"kind":"box","width":200,"height":3000,"depth":200}""");
            element.SetModelAsset(Guid.NewGuid());
            element.ConfigurePlacement(0, 0, 0, 0, 200, 3000, 200);
            var attribute = SpaceElementAttribute.Create(
                tenantId,
                element,
                "warehouse",
                "fireRating",
                "String",
                "2h");
            context.AddRange(
                source,
                floor,
                zone,
                aisle,
                rack,
                rackLevel,
                location,
                element,
                attribute);
        }

        await context.SaveChangesAsync();
        version.BeginValidation();
        version.MarkReady(ContentHash, "space-v1", WmsHash);
        version.BeginPublishing();
        version.MarkPublished(actorId, DateTime.UtcNow);
        await context.SaveChangesAsync();
        model.SetPublishedVersion(version, ContentHash);
        await context.SaveChangesAsync();
        return (model, version);
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        SpaceContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT [name] FROM sys.tables ORDER BY [name]";
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceClone_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var context = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await context.Database.MigrateAsync();
            await action(context, execution, clock);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
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
                sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private static SpaceContext CreateInMemoryContext(
        Guid tenantId,
        Guid actorId,
        TestClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString("N"),
                new InMemoryDatabaseRoot())
            .Options;
        return new SpaceContext(
            options,
            new TestExecutionContext(tenantId, actorId, Guid.NewGuid()),
            clock);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId)
        : ISpaceExecutionContext, ISpaceCorrelationContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }
}
