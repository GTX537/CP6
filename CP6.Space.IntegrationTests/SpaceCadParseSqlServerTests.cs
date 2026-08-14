using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceCadParseSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 6, 20, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Sql_start_cancel_and_retry_preserve_one_idempotent_lineage()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE02S08_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var clock = new FixedClock();
        await using var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);

        try
        {
            await context.Database.MigrateAsync();
            var fixture = await SeedAsync(context, execution);
            var service = new SpaceCadParseService(
                context,
                execution,
                new AllowAccess(),
                null!,
                null!,
                clock);
            var request = Request(fixture.Source.Sha256);
            var preparation = SpaceCadParsePreparation.Create(
                execution.TenantId,
                fixture.Version.Id,
                fixture.Source.Id,
                fixture.Source.Sha256,
                request.FloorLogicalId,
                request.ConfirmedUnit.ToString(),
                request.ConfirmedScaleToMillimeters,
                request.CoordinateMetadataJson,
                request.CoordinateTransformSha256,
                request.MappingProfileId,
                request.MappingProfileVersion,
                request.MappingDefinitionSha256,
                request.MappingPreviewSha256,
                new string('9', 64),
                true,
                fixture.Version.ContentRevision,
                fixture.Version.ContentHash,
                Now.AddHours(2));
            context.CadParsePreparations.Add(preparation);
            await context.SaveChangesAsync();
            request = request with { PreparationId = preparation.Id };

            var started = await service.StartAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                request,
                "sql-start");
            var replay = await service.StartAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                request,
                "sql-start");
            await service.CancelAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId);
            await using var firstContext = CreateContext(
                connectionString,
                execution,
                clock);
            await using var secondContext = CreateContext(
                connectionString,
                execution,
                clock);
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<SpaceCadParseActionResponse> RetryAsync(
                SpaceContext serviceContext)
            {
                await gate.Task;
                return await new SpaceCadParseService(
                    serviceContext,
                    execution,
                    new AllowAccess(),
                    null!,
                    null!,
                    clock).RetryAsync(
                        fixture.Version.Id,
                        fixture.Source.Id,
                        started.JobId,
                        "sql-retry");
            }

            var first = RetryAsync(firstContext);
            var second = RetryAsync(secondContext);
            gate.SetResult();
            var retries = await Task.WhenAll(first, second);

            context.ChangeTracker.Clear();
            var jobs = await context.Jobs.OrderBy(job => job.RequestedAtUtc)
                .ThenBy(job => job.Id)
                .ToListAsync();
            Assert.Equal(started.JobId, replay.JobId);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(retries[0].JobId, retries[1].JobId);
            Assert.Single(retries, retry => !retry.IdempotentReplay);
            Assert.Single(retries, retry => retry.IdempotentReplay);
            Assert.Equal(2, jobs.Count);
            Assert.Equal(
                SpaceJobStatus.Cancelled,
                jobs.Single(job => job.Id == started.JobId).Status);
            Assert.Equal(
                started.JobId,
                jobs.Single(job => job.Id == retries[0].JobId).RetryOfJobId);
            Assert.Equal(2, await context.IdempotencyRecords.CountAsync());
            Assert.Empty(await context.Artifacts.ToListAsync());
            Assert.Equal(
                SpaceSourceState.Ready,
                (await context.Sources.SingleAsync(
                    source => source.Id == fixture.Source.Id)).State);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        FixedClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);

    private static async Task<SeedResult> SeedAsync(
        SpaceContext context,
        TestExecutionContext execution)
    {
        var model = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            1,
            "Published");
        context.AddRange(model, published);
        await context.SaveChangesAsync();
        published.BeginValidation();
        published.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();
        var version = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            2,
            "Draft",
            published.Id);
        context.Add(version);
        await context.SaveChangesAsync();
        model.ReserveDraft(version);
        await context.SaveChangesAsync();
        var fileId = Guid.NewGuid();
        var file = SpaceFile.CreateUploading(
            fileId,
            execution.TenantId,
            $"{execution.TenantId:N}/{fileId:N}/source.content",
            "warehouse.dxf",
            "application/vnd.autocad.dxf",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine(
            "application/vnd.autocad.dxf",
            ".dxf",
            128,
            new string('c', 64));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        var source = SpaceModelSource.CreateFileSource(
            execution.TenantId,
            version.Id,
            SpaceSourceType.Dxf,
            file,
            "warehouse.dxf");
        context.AddRange(file, source);
        await context.SaveChangesAsync();
        return new SeedResult(version, source);
    }

    private static StartSpaceCadParseRequest Request(string sourceSha256)
    {
        var floorId = Guid.NewGuid();
        var transformHash = new string('d', 64);
        var metadata = new SpaceCadCoordinateMetadataV1(
            1,
            sourceSha256,
            true,
            SpaceCadUnit.Millimeter,
            1m,
            SpaceCadUnit.Millimeter,
            1m,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            0m,
            new SpaceCadFloorAssignmentV1(
                floorId,
                "F1",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(0, 0, 100_000, 100_000)),
            SpaceCadAffineTransformV1.Identity,
            new SpaceCadBoundsV1(0, 0, 100_000, 100_000),
            new SpaceCadBoundsV1(0, 0, 100_000, 100_000),
            transformHash);
        return new StartSpaceCadParseRequest(
            Guid.Empty,
            floorId,
            SpaceCadUnit.Millimeter,
            1m,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            transformHash,
            Guid.NewGuid(),
            1,
            new string('e', 64),
            new string('f', 64));
    }

    private sealed record SeedResult(
        SpaceModelVersion Version,
        SpaceModelSource Source);

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }
}
