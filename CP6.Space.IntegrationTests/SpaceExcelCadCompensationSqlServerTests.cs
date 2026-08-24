using System.Security.Cryptography;
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
public sealed class SpaceExcelCadCompensationSqlServerTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task Sealed_apply_history_undoes_and_redoes_atomically_in_sql_server()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid versionId;
            Guid floorId;
            Guid matchJobId;
            Guid applyJobId;
            Guid originalBatchId;
            Guid clientId;
            Guid leaseId;
            string historySha;

            await using (var context = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var model = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
                var published = SpaceModelVersion.CreateDraft(
                    execution.TenantId,
                    model.Id,
                    1,
                    "Published baseline");
                context.AddRange(model, published);
                await context.SaveChangesAsync();
                published.BeginValidation();
                published.MarkReady(
                    new string('a', 64),
                    "space-v1",
                    new string('b', 64));
                published.BeginPublishing();
                published.MarkPublished(execution.ActorId, clock.UtcNow);
                model.BeginCutover(Guid.NewGuid());
                model.MarkFrozen();
                model.MarkBootstrapping();
                model.MarkVerified(published);
                model.ActivateDesignV1();
                await context.SaveChangesAsync();

                var draft = SpaceModelVersion.CreateDraft(
                    execution.TenantId,
                    model.Id,
                    2,
                    "Excel CAD compensation",
                    published.Id);
                model.ReserveDraft(draft);
                versionId = draft.Id;
                floorId = Guid.NewGuid();
                var floor = SpaceFloorRevision.Create(
                    execution.TenantId,
                    versionId,
                    floorId,
                    model.SiteId,
                    1,
                    "F01",
                    "Floor 01");
                var zone = SpaceZoneRevision.Create(
                    execution.TenantId,
                    versionId,
                    Guid.NewGuid(),
                    floorId,
                    "Z01",
                    0);
                var file = CleanExcelFile(execution.TenantId);
                var source = SpaceModelSource.CreateFileSource(
                    execution.TenantId,
                    versionId,
                    SpaceSourceType.Excel,
                    file,
                    "warehouse.xlsx");
                source.BeginParsing();
                source.MarkPreviewReady();
                clientId = Guid.NewGuid();
                var editLease = SpaceEditLease.Create(
                    execution.TenantId,
                    versionId,
                    floorId,
                    execution.ActorId,
                    "SQL Excel/CAD owner",
                    clientId,
                    clock.UtcNow,
                    TimeSpan.FromSeconds(90));
                leaseId = editLease.LeaseId;
                originalBatchId = Guid.NewGuid();
                matchJobId = Guid.NewGuid();
                var payload = new SpaceExcelCadApplyJobPayload(
                    SpaceExcelCadApplyVersions.PayloadSchemaVersion,
                    versionId,
                    matchJobId,
                    Guid.NewGuid(),
                    new string('c', 64),
                    source.Id,
                    floorId,
                    clientId,
                    leaseId,
                    0,
                    0,
                    originalBatchId);
                var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                var applyJob = SpaceJob.CreateQueued(
                    execution.TenantId,
                    SpaceJobType.ExcelCadApply,
                    SpaceJobSubjectType.ModelSource,
                    source.Id,
                    new string('d', 64),
                    Hash(payloadJson),
                    50,
                    3,
                    execution.ActorId,
                    clock.UtcNow,
                    Guid.NewGuid(),
                    payloadJson);
                applyJobId = applyJob.Id;
                var batch = SpaceElementCommandBatch.Create(
                    execution.TenantId,
                    originalBatchId,
                    versionId,
                    floorId,
                    clientId,
                    leaseId,
                    0,
                    0,
                    null,
                    new string('e', 64),
                    new string('e', 64),
                    execution.ActorId,
                    clock.UtcNow);
                var rack = SpaceRackRevision.Create(
                    execution.TenantId,
                    versionId,
                    Guid.NewGuid(),
                    floorId,
                    zone.LogicalId,
                    "R-001");
                rack.ConfigureGeometry(1000, 2000, 0, 0, 2400, 1000, 4200);
                rack.AttachSource(source, "Racks!2");
                var afterJson = JsonSerializer.Serialize(new
                {
                    exists = true,
                    rack.Id,
                    rack.LogicalId,
                    rack.FloorLogicalId,
                    rack.ZoneLogicalId,
                    rack.AisleLogicalId,
                    rack.RackCode,
                    rack.Name,
                    rack.RackType,
                    rack.TemplateVersionId,
                    rack.X,
                    rack.Y,
                    rack.Z,
                    rack.RotationZ,
                    rack.Width,
                    rack.Depth,
                    rack.Height,
                    rack.SourceId,
                    rack.SourceRef,
                    lifecycleState = rack.LifecycleState.ToString(),
                }, JsonOptions);
                var record = SpaceElementCommandRecord.Create(
                    execution.TenantId,
                    Guid.NewGuid(),
                    batch,
                    0,
                    "ExcelCadApplyRackNew",
                    rack.LogicalId,
                    "{}",
                    "{\"exists\":false}",
                    afterJson);
                historySha = HistoryHash(record);
                floor.AdvanceRevision(0);
                draft.TouchContent();
                source.MarkImported(originalBatchId);
                var result = new SpaceExcelCadApplyResultV1(
                    SpaceExcelCadApplyVersions.SchemaVersion,
                    matchJobId,
                    applyJobId,
                    payload.ArtifactId,
                    payload.ArtifactPayloadSha256,
                    versionId,
                    source.Id,
                    floorId,
                    originalBatchId,
                    0,
                    1,
                    0,
                    1,
                    1,
                    0,
                    0,
                    execution.ActorId,
                    clock.UtcNow,
                    clock.UtcNow,
                    new string('e', 64),
                    historySha,
                    1);
                var resultJson = JsonSerializer.Serialize(result, JsonOptions);
                batch.Complete(1, 1, resultJson);
                var attempt = applyJob.Claim(
                    "sql-worker",
                    SpaceExcelCadApplyJobProcessor.Version,
                    clock.UtcNow,
                    TimeSpan.FromMinutes(5));
                attempt.Succeed(clock.UtcNow);
                applyJob.Complete(
                    attempt.Id,
                    attempt.WorkerId,
                    clock.UtcNow,
                    resultJson);
                context.AddRange(
                    draft,
                    floor,
                    zone,
                    file,
                    source,
                    editLease,
                    applyJob,
                    batch,
                    rack,
                    record);
                await context.SaveChangesAsync();
            }

            await using var verify = CreateContext(
                connectionString,
                execution,
                clock);
            var modelSiteId = await (
                    from version in verify.Versions.AsNoTracking()
                    join model in verify.Models.AsNoTracking()
                        on version.ModelId equals model.Id
                    where version.Id == versionId
                    select model.SiteId)
                .SingleAsync();
            var service = new SpaceExcelCadApplyService(
                verify,
                execution,
                new TestAccess(modelSiteId),
                new EmptyServices(),
                clock);
            var undo = new CompensateSpaceExcelCadApplyRequest(
                SpaceExcelCadApplyVersions.SchemaVersion,
                SpaceExcelCadCompensationDirections.Undo,
                Guid.NewGuid(),
                clientId,
                leaseId,
                1,
                1,
                historySha);
            await service.CompensateAsync(
                versionId,
                matchJobId,
                applyJobId,
                undo,
                "sql-history-undo");
            verify.ChangeTracker.Clear();
            await service.CompensateAsync(
                versionId,
                matchJobId,
                applyJobId,
                undo with
                {
                    Direction = SpaceExcelCadCompensationDirections.Redo,
                    CommandBatchId = Guid.NewGuid(),
                    ExpectedFloorRevision = 2,
                    ExpectedContentRevision = 2,
                },
                "sql-history-redo");

            Assert.Equal(3, await verify.ElementCommandBatches.CountAsync());
            Assert.Equal(3, await verify.ElementCommandRecords.CountAsync());
            Assert.Equal(
                SpaceLifecycleState.Active,
                await verify.RackRevisions.Select(item => item.LifecycleState)
                    .SingleAsync());
            Assert.Equal(
                SpaceSourceState.Imported,
                await verify.Sources.Where(item =>
                        item.ModelVersionId == versionId &&
                        item.SourceType == SpaceSourceType.Excel)
                    .Select(item => item.State)
                    .SingleAsync());
        });
    }

    private static SpaceFile CleanExcelFile(Guid tenantId)
    {
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenantId,
            $"{tenantId:N}/{Guid.NewGuid():N}/warehouse.xlsx",
            "warehouse.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsx",
            1,
            new string('f', 64));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        return file;
    }

    private static string HistoryHash(SpaceElementCommandRecord record) => Hash(
        $"{record.CommandBatchId:N}\u001f{record.Id:N}\u001f{record.SequenceNo}" +
        $"\u001f{record.CommandType}\u001f{record.TargetLogicalId:N}" +
        $"\u001f{record.BeforeJson}\u001f{record.AfterJson}\n");

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static async Task WithDatabaseAsync(
        Func<string, TestExecution, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6ExcelCadHistory_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(connectionString, execution, clock);
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
        TestExecution execution,
        TestClock clock) => new(
        new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options,
        execution,
        clock);

    private sealed record TestExecution(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => false;
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }

    private sealed class TestAccess(Guid siteId) : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid requestedSiteId, bool write)
        {
            if (requestedSiteId != siteId)
                throw new InvalidOperationException("Site denied.");
        }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
