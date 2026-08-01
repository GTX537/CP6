using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceFileSafetySqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 7, 26, 16, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Clean_scan_commits_file_job_and_attempt_atomically()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var design = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var (file, _) = await SeedFileScanAsync(
                connectionString,
                execution,
                clock);
            var source = SpaceModelSource.CreatePendingFileSource(
                execution.TenantId,
                design.DraftVersionId,
                SpaceSourceType.Pdf,
                file,
                file.OriginalName);
            await using (var sourceContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                sourceContext.Sources.Add(source);
                await sourceContext.SaveChangesAsync();
            }
            await using var worker = CreateContext(
                connectionString,
                execution,
                clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await new EfSpaceJobLeaseStore(worker, clock)
                    .TryClaimNextAsync(
                        "safety-worker",
                        SpaceFileScanProcessor.ProcessorVersion,
                        TimeSpan.FromMinutes(1)));
            var processor = new SpaceFileScanProcessor(
                new EfSpaceFileScanStateStore(worker, clock),
                new FixedSafetyScanner(
                    FileSafetyResult.Clean(
                        "clamav",
                        "daily-20260726")));

            await processor.ProcessAsync(lease);

            await using var verify = CreateContext(
                connectionString,
                execution,
                clock);
            var persistedFile = await verify.Files.SingleAsync();
            var job = await verify.Jobs.SingleAsync();
            var attempt = await verify.JobAttempts.SingleAsync();
            Assert.Equal(file.Id, persistedFile.Id);
            Assert.Equal(SpaceFileState.Clean, persistedFile.State);
            Assert.Equal("clamav", persistedFile.ScanEngine);
            Assert.Equal(
                SpaceSourceState.Ready,
                (await verify.Sources.SingleAsync()).State);
            Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
            Assert.Equal(SpaceJobAttemptOutcome.Succeeded, attempt.Outcome);
        });
    }

    [SqlServerFact]
    public async Task Scanner_outage_returns_file_to_quarantine_and_schedules_retry()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await SeedFileScanAsync(
                connectionString,
                execution,
                clock);
            await using var worker = CreateContext(
                connectionString,
                execution,
                clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await new EfSpaceJobLeaseStore(worker, clock)
                    .TryClaimNextAsync(
                        "safety-worker",
                        SpaceFileScanProcessor.ProcessorVersion,
                        TimeSpan.FromMinutes(1)));
            var processor = new SpaceFileScanProcessor(
                new EfSpaceFileScanStateStore(worker, clock),
                new QuarantiningFileSafetyScanner());

            await processor.ProcessAsync(lease);

            await using var verify = CreateContext(
                connectionString,
                execution,
                clock);
            Assert.Equal(
                SpaceFileState.Quarantined,
                (await verify.Files.SingleAsync()).State);
            var job = await verify.Jobs.SingleAsync();
            Assert.Equal(SpaceJobStatus.Queued, job.Status);
            Assert.Equal(SpaceErrorCodes.FileQuarantined, job.LastErrorCode);
            Assert.Equal(
                SpaceJobAttemptOutcome.Failed,
                (await verify.JobAttempts.SingleAsync()).Outcome);
            var deletion = await new EfSpaceFileRetentionStore(verify)
                .TryTombstoneAsync(
                    execution.TenantId,
                    (await verify.Files.SingleAsync()).Id,
                    Now,
                    requireExpired: false);
            Assert.Equal(
                SpaceFileTombstoneStatus.Referenced,
                deletion.Status);
        });
    }

    [SqlServerFact]
    public async Task Ready_underlay_attachment_is_revisioned_and_idempotent()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var design = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var file = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            var source = SpaceModelSource.CreateFileSource(
                execution.TenantId,
                design.DraftVersionId,
                SpaceSourceType.Pdf,
                file,
                file.OriginalName);
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seed.AddRange(file, source);
                await seed.SaveChangesAsync();
            }

            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var service = NewUnderlayService(
                context,
                execution,
                clock,
                design.SiteId);
            var request = new AttachSpaceUnderlayRequest(
                source.Id,
                ExpectedFloorRevision: 0);

            var first = await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                request,
                "attach-underlay-once");
            var replay = await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                request,
                "attach-underlay-once");

            Assert.False(first.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(source.Id, first.Floor.UnderlaySourceId);
            Assert.Equal(1, first.Floor.RevisionNumber);
            context.ChangeTracker.Clear();
            var floor = await context.FloorRevisions.SingleAsync(
                candidate =>
                    candidate.ModelVersionId == design.DraftVersionId &&
                    candidate.LogicalId == design.FloorLogicalId);
            var version = await context.Versions.SingleAsync(
                candidate => candidate.Id == design.DraftVersionId);
            Assert.Equal(source.Id, floor.UnderlaySourceId);
            Assert.Equal(1, floor.Revision);
            Assert.Equal(1, version.ContentRevision);
            Assert.Single(context.IdempotencyRecords);
        });
    }

    [SqlServerFact]
    public async Task Underlay_calibration_is_audited_revisioned_and_idempotent()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var design = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var file = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            var source = SpaceModelSource.CreateFileSource(
                execution.TenantId,
                design.DraftVersionId,
                SpaceSourceType.Pdf,
                file,
                file.OriginalName);
            var replacementFile = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            var replacementSource = SpaceModelSource.CreateFileSource(
                execution.TenantId,
                design.DraftVersionId,
                SpaceSourceType.Pdf,
                replacementFile,
                replacementFile.OriginalName);
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seed.AddRange(
                    file,
                    source,
                    replacementFile,
                    replacementSource);
                await seed.SaveChangesAsync();
            }

            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var service = NewUnderlayService(
                context,
                execution,
                clock,
                design.SiteId);
            await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new AttachSpaceUnderlayRequest(
                    source.Id,
                    ExpectedFloorRevision: 0),
                "attach-before-calibration");
            var request = new SaveSpaceUnderlayCalibrationRequest(
                design.FloorLogicalId,
                PageNumber: 1,
                PixelWidth: 1_000,
                PixelHeight: 500,
                new SpaceUnderlayCalibrationPointDto(
                    0,
                    500,
                    1_000,
                    2_000),
                new SpaceUnderlayCalibrationPointDto(
                    100,
                    500,
                    2_000,
                    2_000),
                new SpaceUnderlayCalibrationPointDto(
                    0,
                    400,
                    1_000,
                    3_000),
                ExpectedFloorRevision: 1);

            var first = await service.CalibrateAsync(
                design.DraftVersionId,
                source.Id,
                request,
                "calibrate-underlay-once");
            var replay = await service.CalibrateAsync(
                design.DraftVersionId,
                source.Id,
                request,
                "calibrate-underlay-once");
            var current = await service.GetCalibrationAsync(
                design.DraftVersionId,
                source.Id,
                design.FloorLogicalId);

            Assert.False(first.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(first.Calibration.Id, replay.Calibration.Id);
            Assert.Equal(first.Calibration.Id, current.Id);
            Assert.Equal(10m, current.MillimetersPerPixel);
            Assert.Equal(0m, current.ValidationErrorMillimeters);
            Assert.Equal(50m, current.ErrorThresholdMillimeters);
            Assert.Equal(2, first.Floor.RevisionNumber);
            Assert.Equal(
                first.Calibration.Id,
                first.Floor.UnderlayCalibrationId);

            context.ChangeTracker.Clear();
            var floor = await context.FloorRevisions.SingleAsync(
                candidate =>
                    candidate.ModelVersionId == design.DraftVersionId &&
                    candidate.LogicalId == design.FloorLogicalId);
            var version = await context.Versions.SingleAsync(
                candidate => candidate.Id == design.DraftVersionId);
            Assert.Equal(first.Calibration.Id, floor.UnderlayCalibrationId);
            Assert.Equal(10m, floor.UnderlayScale);
            Assert.Equal(2, floor.Revision);
            Assert.Equal(2, version.ContentRevision);
            Assert.Single(context.UnderlayCalibrations);
            Assert.Equal(2, context.IdempotencyRecords.Count());

            await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new AttachSpaceUnderlayRequest(
                    replacementSource.Id,
                    ExpectedFloorRevision: 2),
                "attach-replacement-underlay");
            var keyConflict =
                await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.CalibrateAsync(
                        design.DraftVersionId,
                        replacementSource.Id,
                        request,
                        "calibrate-underlay-once"));
            Assert.Equal(
                SpaceErrorCodes.IdempotencyConflict,
                keyConflict.Code);
            Assert.Equal(409, keyConflict.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task Scan_and_retention_stores_do_not_cross_tenants()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var (file, _) = await SeedFileScanAsync(
                connectionString,
                execution,
                clock);
            await using var owner = CreateContext(
                connectionString,
                execution,
                clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await new EfSpaceJobLeaseStore(owner, clock)
                    .TryClaimNextAsync(
                        "owner-worker",
                        SpaceFileScanProcessor.ProcessorVersion,
                        TimeSpan.FromMinutes(1)));

            var other = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using var otherContext = CreateContext(
                connectionString,
                other,
                clock);
            await Assert.ThrowsAsync<SpaceJobLeaseLostException>(
                () => new EfSpaceFileScanStateStore(otherContext, clock)
                    .BeginScanAsync(lease));
            var tombstone = await new EfSpaceFileRetentionStore(otherContext)
                .TryTombstoneAsync(
                    other.TenantId,
                    file.Id,
                    Now,
                    requireExpired: false);
            Assert.Equal(
                SpaceFileTombstoneStatus.NotFound,
                tombstone.Status);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_source_reference_and_expiration_never_delete_a_referenced_file()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var seeded = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var file = NewFile(
                    execution.TenantId,
                    clean: true,
                    retainUntilUtc: Now.AddMinutes(-1));
                await using (var seed = CreateContext(
                                 connectionString,
                                 execution,
                                 clock))
                {
                    seed.Files.Add(file);
                    await seed.SaveChangesAsync();
                }

                await using var sourceContext = CreateContext(
                    connectionString,
                    execution,
                    clock);
                await using var cleanupContext = CreateContext(
                    connectionString,
                    execution,
                    clock);
                var sourceService = NewDesignService(
                    sourceContext,
                    execution,
                    clock,
                    seeded.SiteId);
                var cleanup = new EfSpaceFileRetentionStore(cleanupContext);

                Exception? sourceFailure = null;
                SpaceFileTombstoneResult? cleanupResult = null;
                var sourceTask = Task.Run(async () =>
                {
                    try
                    {
                        await sourceService.CreateSourceAsync(
                            seeded.DraftVersionId,
                            new CreateSpaceSourceRequest(
                                file.Id,
                                "Pdf",
                                $"Concurrent {iteration}"),
                            $"concurrent-{iteration}");
                    }
                    catch (Exception exception)
                    {
                        sourceFailure = exception;
                    }
                });
                var cleanupTask = Task.Run(async () =>
                {
                    cleanupResult = await cleanup.TryTombstoneAsync(
                        execution.TenantId,
                        file.Id,
                        Now,
                        requireExpired: true);
                });
                await Task.WhenAll(sourceTask, cleanupTask);

                await using var verify = CreateContext(
                    connectionString,
                    execution,
                    clock);
                var hasReference = await verify.Sources.AnyAsync(
                    source => source.FileId == file.Id);
                var persisted = await verify.Files
                    .IgnoreQueryFilters()
                    .SingleAsync(candidate => candidate.Id == file.Id);
                Assert.False(hasReference && persisted.IsDeleted);
                if (hasReference)
                {
                    Assert.Null(sourceFailure);
                    Assert.Equal(
                        SpaceFileTombstoneStatus.Referenced,
                        cleanupResult!.Status);
                    Assert.False(persisted.IsDeleted);
                }
                else
                {
                    Assert.NotNull(sourceFailure);
                    Assert.True(persisted.IsDeleted);
                }
            }
        });
    }

    [SqlServerFact]
    public async Task Tombstone_survives_object_failure_and_can_be_completed_later()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var file = NewFile(
                execution.TenantId,
                clean: false,
                retainUntilUtc: Now.AddMinutes(-1));
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                seed.Files.Add(file);
                await seed.SaveChangesAsync();
            }

            await using var cleanup = CreateContext(
                connectionString,
                execution,
                clock);
            var store = new EfSpaceFileRetentionStore(cleanup);
            var result = await store.TryTombstoneAsync(
                execution.TenantId,
                file.Id,
                Now,
                requireExpired: true);
            var candidate = Assert.IsType<SpaceFileDeletionCandidate>(
                result.Candidate);
            Assert.Equal(
                SpaceFileTombstoneStatus.Tombstoned,
                result.Status);
            Assert.Single(
                await store.FindPendingContentDeletionAsync(
                    execution.TenantId,
                    10));

            await store.MarkContentDeletedAsync(
                candidate,
                Now.AddSeconds(1));

            Assert.Empty(
                await store.FindPendingContentDeletionAsync(
                    execution.TenantId,
                    10));
            cleanup.ChangeTracker.Clear();
            var tombstone = await cleanup.Files
                .IgnoreQueryFilters()
                .SingleAsync();
            Assert.True(tombstone.IsDeleted);
            Assert.Equal(
                Now.AddSeconds(1),
                tombstone.ContentDeletedAtUtc);
        });
    }

    private static async Task<(SpaceFile File, SpaceJob Job)> SeedFileScanAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        var file = NewFile(
            execution.TenantId,
            clean: false,
            retainUntilUtc: Now.AddDays(30));
        var request = new SpaceJobEnqueueRequest(
            SpaceJobType.FileScan,
            SpaceJobSubjectType.File,
            file.Id,
            file.Sha256!,
            SpaceFileScanProcessor.ProcessorVersion);
        var job = SpaceJob.CreateQueued(
            execution.TenantId,
            request.JobType,
            request.SubjectType,
            request.SubjectId,
            SpaceJobBusinessKey.Create(request),
            request.InputHash,
            request.Priority,
            request.MaxAttempts,
            execution.ActorId,
            Now,
            execution.CorrelationId);
        await using var context = CreateContext(
            connectionString,
            execution,
            clock);
        await new EfSpaceFileCatalog(context)
            .AddQuarantinedWithScanJobAsync(file, job);
        return (file, job);
    }

    private static SpaceFile NewFile(
        Guid tenantId,
        bool clean,
        DateTime retainUntilUtc)
    {
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenantId,
            $"quarantine/{Guid.NewGuid():N}",
            "floor.pdf",
            "application/pdf",
            SpaceFileRetentionClass.Source,
            retainUntilUtc);
        file.CompleteQuarantine(
            "application/pdf",
            ".pdf",
            128,
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        if (clean)
        {
            file.BeginScanning();
            file.MarkClean("test-av", "v1");
        }
        return file;
    }

    private static async Task<(
        Guid SiteId,
        Guid DraftVersionId,
        Guid FloorLogicalId)>
        SeedWritableDesignAsync(
            string connectionString,
            TestExecutionContext execution,
            TestClock clock)
    {
        await using var context = CreateContext(
            connectionString,
            execution,
            clock);
        var model = SpaceModel.Create(
            execution.TenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            1,
            "Published");
        context.AddRange(model, published);
        await context.SaveChangesAsync();
        published.BeginValidation();
        published.MarkReady(
            new string('a', 64),
            "space-v1",
            new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var draft = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            2,
            "Draft",
            published.Id);
        model.ReserveDraft(draft);
        var floor = SpaceFloorRevision.Create(
            execution.TenantId,
            draft.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F1",
            "Floor 1");
        context.AddRange(draft, floor);
        await context.SaveChangesAsync();
        return (model.SiteId, draft.Id, floor.LogicalId);
    }

    private static SpaceDesignV1Service NewDesignService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid siteId) =>
        new(
            context,
            execution,
            clock,
            new TestCursorCodec(),
            new TestAccessEvaluator(siteId),
            new SpaceVersionCloneCoordinator(
                execution,
                new EfSpaceVersionCloneStore(
                    context,
                    execution,
                    clock)),
            new SpaceSourceCoordinator(execution));

    private static SpaceUnderlayV1Service NewUnderlayService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid siteId)
    {
        var files = new UnusedFileStore();
        return new SpaceUnderlayV1Service(
            context,
            execution,
            new TestAccessEvaluator(siteId),
            new SpaceFileUploadService(
                execution,
                files,
                new EfSpaceFileCatalog(context),
                clock: clock),
            new SpaceSourceCoordinator(execution),
            files,
            clock);
    }

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceFileSafety_{Guid.NewGuid():N}",
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

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FixedSafetyScanner : IFileSafetyScanner
    {
        private readonly FileSafetyResult _result;

        public FixedSafetyScanner(FileSafetyResult result)
        {
            _result = result;
        }

        public Task<FileSafetyResult> ScanAsync(
            FileScanRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class TestAccessEvaluator(Guid siteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid requestedSiteId, bool write)
        {
            if (requestedSiteId != siteId)
                throw new UnauthorizedAccessException();
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

    private sealed class UnusedFileStore :
        ISpaceQuarantineStore,
        ISpaceFileStore
    {
        public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ISpaceQuarantineWriteSession>(
                new NotSupportedException());

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromException<Stream>(new NotSupportedException());

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new NotSupportedException());
    }
}
