using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceExcelPreflightSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 19, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Sql_start_atomically_pins_source_job_and_idempotency()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantId = Guid.NewGuid();
            Guid versionId;
            Guid sourceId;
            await using (var fixture = CreateContext(connectionString, tenantId))
            {
                (versionId, sourceId) = await SeedAsync(fixture);
                var mapping = new SpaceExcelMappingService(
                    fixture.Context,
                    fixture.Execution,
                    fixture.Clock);
                var service = new SpaceExcelPreflightService(
                    fixture.Context,
                    fixture.Execution,
                    new AllowAccess(),
                    null!,
                    null!,
                    mapping,
                    fixture.Clock);
                var request = new StartSpaceExcelPreflightRequest(
                    SpaceExcelMappingService.SystemStandardProfileId,
                    1);

                var started = await service.StartAsync(
                    versionId,
                    sourceId,
                    request,
                    "sql-preflight-1");
                var replay = await service.StartAsync(
                    versionId,
                    sourceId,
                    request,
                    "sql-preflight-1");

                Assert.Equal(started.JobId, replay.JobId);
                Assert.True(replay.IdempotentReplay);
                Assert.False(string.IsNullOrWhiteSpace(started.Source.RowVersion));
            }

            await using var verify = CreateContext(connectionString, tenantId);
            var source = await verify.Context.Sources.SingleAsync(
                item => item.Id == sourceId);
            var job = await verify.Context.Jobs.SingleAsync(
                item => item.SubjectId == sourceId &&
                        item.JobType == SpaceJobType.ExcelPreview);
            var version = await verify.Context.Versions.SingleAsync(
                item => item.Id == versionId);
            Assert.Equal(SpaceSourceState.Parsing, source.State);
            Assert.Equal(
                SpaceExcelMappingService.SystemStandardProfileId,
                source.MappingProfileId);
            Assert.Equal(1, source.MappingProfileVersion);
            Assert.Equal(SpaceJobStatus.Queued, job.Status);
            Assert.Equal(0, version.ContentRevision);
            Assert.Single(await verify.Context.IdempotencyRecords.ToListAsync());
        });
    }

    private static async Task<(Guid VersionId, Guid SourceId)> SeedAsync(
        ContextFixture fixture)
    {
        var model = SpaceModel.Create(
            fixture.Execution.TenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            fixture.Execution.TenantId,
            model.Id,
            1,
            "Published");
        published.BeginValidation();
        published.MarkReady(
            new string('a', 64),
            "space-v1",
            new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(fixture.Execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var draft = SpaceModelVersion.CreateDraft(
            fixture.Execution.TenantId,
            model.Id,
            2,
            "Draft",
            published.Id);
        model.ReserveDraft(draft);
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            fixture.Execution.TenantId,
            $"{fixture.Execution.TenantId:N}/{Guid.NewGuid():N}/source.content",
            "warehouse.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsx",
            1024,
            new string('c', 64));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        var source = SpaceModelSource.CreateFileSource(
            fixture.Execution.TenantId,
            draft.Id,
            SpaceSourceType.Excel,
            file,
            file.OriginalName);
        fixture.Context.AddRange(model, published, draft, file, source);
        await fixture.Context.SaveChangesAsync();
        return (draft.Id, source.Id);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E03S03_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var migration = CreateContext(
            connectionString,
            Guid.NewGuid());
        try
        {
            await migration.Context.Database.MigrateAsync();
            await action(connectionString);
        }
        finally
        {
            await migration.Context.Database.EnsureDeletedAsync();
        }
    }

    private static ContextFixture CreateContext(
        string connectionString,
        Guid tenantId)
    {
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);
        return new ContextFixture(context, execution, clock);
    }

    private sealed record ContextFixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        FixedClock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

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
