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
                ExpectedFloorRevision: 0,
                ExpectedContentRevision: 0,
                design.ClientInstanceId,
                design.LeaseId,
                CommandBatchId: Guid.NewGuid());

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
            Assert.Equal("UnderlaySet", first.History.OperationType);
            Assert.Equal(request.CommandBatchId, first.History.OriginalCommandBatchId);
            Assert.Matches("^[0-9a-f]{64}$", first.History.HistorySha256);

            var undoRequest = new CompensateSpaceUnderlayRequest(
                SchemaVersion: SpaceUnderlayHistoryVersions.SchemaVersion,
                Direction: SpaceUnderlayCompensationDirections.Undo,
                OriginalCommandBatchId: first.History.OriginalCommandBatchId,
                HistorySha256: first.History.HistorySha256,
                CommandBatchId: Guid.NewGuid(),
                ClientInstanceId: design.ClientInstanceId,
                LeaseId: design.LeaseId,
                ExpectedFloorRevision: 1,
                ExpectedContentRevision: 1);
            var invalidHistory = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.CompensateAsync(
                    design.DraftVersionId,
                    design.FloorLogicalId,
                    undoRequest with
                    {
                        CommandBatchId = Guid.NewGuid(),
                        HistorySha256 = new string('0', 64),
                    },
                    "undo-invalid-underlay-history"));
            Assert.Equal(SpaceErrorCodes.UnderlayHistoryInvalid, invalidHistory.Code);
            var wrongSession = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.CompensateAsync(
                    design.DraftVersionId,
                    design.FloorLogicalId,
                    undoRequest with
                    {
                        CommandBatchId = Guid.NewGuid(),
                        ClientInstanceId = Guid.NewGuid(),
                    },
                    "undo-underlay-wrong-session"));
            Assert.Equal(SpaceErrorCodes.EditLeaseLost, wrongSession.Code);
            var undone = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                undoRequest,
                "undo-attach-underlay");
            var undoReplay = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                undoRequest,
                "undo-attach-underlay");
            Assert.Null(undone.Floor.UnderlaySourceId);
            Assert.Equal(2, undone.Floor.RevisionNumber);
            Assert.True(undoReplay.IdempotentReplay);

            var redoRequest = undoRequest with
            {
                Direction = SpaceUnderlayCompensationDirections.Redo,
                CommandBatchId = Guid.NewGuid(),
                ExpectedFloorRevision = 2,
                ExpectedContentRevision = 2,
            };
            var redone = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                redoRequest,
                "redo-attach-underlay");
            Assert.Equal(source.Id, redone.Floor.UnderlaySourceId);
            Assert.Equal(3, redone.Floor.RevisionNumber);

            var detachRequest = request with
            {
                SourceId = null,
                ExpectedFloorRevision = 3,
                ExpectedContentRevision = 3,
                CommandBatchId = Guid.NewGuid(),
            };
            var detached = await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                detachRequest,
                "detach-underlay-once");
            Assert.Null(detached.Floor.UnderlaySourceId);
            Assert.Equal(4, detached.Floor.RevisionNumber);

            var undoDetach = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new CompensateSpaceUnderlayRequest(
                    SchemaVersion: SpaceUnderlayHistoryVersions.SchemaVersion,
                    Direction: SpaceUnderlayCompensationDirections.Undo,
                    OriginalCommandBatchId: detached.History.OriginalCommandBatchId,
                    HistorySha256: detached.History.HistorySha256,
                    CommandBatchId: Guid.NewGuid(),
                    ClientInstanceId: design.ClientInstanceId,
                    LeaseId: design.LeaseId,
                    ExpectedFloorRevision: 4,
                    ExpectedContentRevision: 4),
                "undo-detach-underlay");
            Assert.Equal(source.Id, undoDetach.Floor.UnderlaySourceId);
            Assert.Equal(5, undoDetach.Floor.RevisionNumber);
            context.ChangeTracker.Clear();
            var floor = await context.FloorRevisions.SingleAsync(
                candidate =>
                    candidate.ModelVersionId == design.DraftVersionId &&
                    candidate.LogicalId == design.FloorLogicalId);
            var version = await context.Versions.SingleAsync(
                candidate => candidate.Id == design.DraftVersionId);
            Assert.Equal(source.Id, floor.UnderlaySourceId);
            Assert.Equal(5, floor.Revision);
            Assert.Equal(5, version.ContentRevision);
            Assert.Equal(5, context.IdempotencyRecords.Count());
            Assert.Equal(5, context.ElementCommandBatches.Count());
            Assert.Equal(5, context.ElementCommandRecords.Count());
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
                    ExpectedFloorRevision: 0,
                    ExpectedContentRevision: 0,
                    design.ClientInstanceId,
                    design.LeaseId,
                    CommandBatchId: Guid.NewGuid()),
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
                ExpectedFloorRevision: 1,
                ExpectedContentRevision: 1,
                design.ClientInstanceId,
                design.LeaseId,
                CommandBatchId: Guid.NewGuid());

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

            var undoCalibration = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new CompensateSpaceUnderlayRequest(
                    SchemaVersion: SpaceUnderlayHistoryVersions.SchemaVersion,
                    Direction: SpaceUnderlayCompensationDirections.Undo,
                    OriginalCommandBatchId: first.History.OriginalCommandBatchId,
                    HistorySha256: first.History.HistorySha256,
                    CommandBatchId: Guid.NewGuid(),
                    ClientInstanceId: design.ClientInstanceId,
                    LeaseId: design.LeaseId,
                    ExpectedFloorRevision: 2,
                    ExpectedContentRevision: 2),
                "undo-underlay-calibration");
            Assert.Null(undoCalibration.Floor.UnderlayCalibrationId);
            Assert.Equal(source.Id, undoCalibration.Floor.UnderlaySourceId);

            var redoCalibration = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new CompensateSpaceUnderlayRequest(
                    SchemaVersion: SpaceUnderlayHistoryVersions.SchemaVersion,
                    Direction: SpaceUnderlayCompensationDirections.Redo,
                    OriginalCommandBatchId: first.History.OriginalCommandBatchId,
                    HistorySha256: first.History.HistorySha256,
                    CommandBatchId: Guid.NewGuid(),
                    ClientInstanceId: design.ClientInstanceId,
                    LeaseId: design.LeaseId,
                    ExpectedFloorRevision: 3,
                    ExpectedContentRevision: 3),
                "redo-underlay-calibration");
            Assert.Equal(first.Calibration.Id, redoCalibration.Floor.UnderlayCalibrationId);

            var replacement = await service.AttachAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new AttachSpaceUnderlayRequest(
                    replacementSource.Id,
                    ExpectedFloorRevision: 4,
                    ExpectedContentRevision: 4,
                    design.ClientInstanceId,
                    design.LeaseId,
                    CommandBatchId: Guid.NewGuid()),
                "attach-replacement-underlay");
            Assert.Equal(replacementSource.Id, replacement.Floor.UnderlaySourceId);
            Assert.Null(replacement.Floor.UnderlayCalibrationId);

            var undoReplacement = await service.CompensateAsync(
                design.DraftVersionId,
                design.FloorLogicalId,
                new CompensateSpaceUnderlayRequest(
                    SchemaVersion: SpaceUnderlayHistoryVersions.SchemaVersion,
                    Direction: SpaceUnderlayCompensationDirections.Undo,
                    OriginalCommandBatchId: replacement.History.OriginalCommandBatchId,
                    HistorySha256: replacement.History.HistorySha256,
                    CommandBatchId: Guid.NewGuid(),
                    ClientInstanceId: design.ClientInstanceId,
                    LeaseId: design.LeaseId,
                    ExpectedFloorRevision: 5,
                    ExpectedContentRevision: 5),
                "undo-underlay-replacement");
            Assert.Equal(source.Id, undoReplacement.Floor.UnderlaySourceId);
            Assert.Equal(first.Calibration.Id, undoReplacement.Floor.UnderlayCalibrationId);
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
    public async Task Unused_source_is_logically_removed_idempotently_and_file_is_retained()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var seeded = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var file = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            context.Files.Add(file);
            await context.SaveChangesAsync();
            var service = NewDesignService(
                context,
                execution,
                clock,
                seeded.SiteId);
            var created = await service.CreateSourceAsync(
                seeded.DraftVersionId,
                new CreateSpaceSourceRequest(
                    file.Id,
                    "Pdf",
                    "Unused source"),
                "create-unused-source");

            var preview = await service.GetSourceRemovalPreviewAsync(
                seeded.DraftVersionId,
                created.Source.Id);
            Assert.True(preview.CanRemove);
            Assert.Empty(preview.References);
            Assert.True(preview.PhysicalFileRetained);

            var request = new RemoveSpaceSourceRequest(
                preview.VersionContentRevision,
                preview.SourceRowVersion);
            var removed = await service.RemoveSourceAsync(
                seeded.DraftVersionId,
                created.Source.Id,
                request,
                "remove-unused-source");
            var replay = await service.RemoveSourceAsync(
                seeded.DraftVersionId,
                created.Source.Id,
                request,
                "remove-unused-source");

            Assert.False(removed.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(
                preview.VersionContentRevision + 1,
                removed.VersionContentRevision);
            context.ChangeTracker.Clear();
            var tombstone = await context.Sources
                .IgnoreQueryFilters()
                .SingleAsync(source => source.Id == created.Source.Id);
            var retainedFile = await context.Files
                .IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == file.Id);
            Assert.True(tombstone.IsDeleted);
            Assert.False(retainedFile.IsDeleted);
            Assert.Equal(SpaceFileState.Clean, retainedFile.State);
        });
    }

    [SqlServerFact]
    public async Task Active_job_blocks_source_removal_but_terminal_job_is_retained_as_audit()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var seeded = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var file = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            context.Files.Add(file);
            await context.SaveChangesAsync();
            var service = NewDesignService(
                context,
                execution,
                clock,
                seeded.SiteId);
            var created = await service.CreateSourceAsync(
                seeded.DraftVersionId,
                new CreateSpaceSourceRequest(
                    file.Id,
                    "Pdf",
                    "Job source"),
                "create-job-source");
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.CadParse,
                SpaceJobSubjectType.ModelSource,
                created.Source.Id,
                new string('a', 64),
                new string('b', 64),
                priority: 10,
                maxAttempts: 2,
                execution.ActorId,
                Now,
                execution.CorrelationId);
            context.Jobs.Add(job);
            await context.SaveChangesAsync();

            var blocked = await service.GetSourceRemovalPreviewAsync(
                seeded.DraftVersionId,
                created.Source.Id);
            var activeReference = Assert.Single(
                blocked.References,
                reference =>
                    reference.Code ==
                    SpaceSourceRemovalReferenceCodes.ActiveJobs);
            Assert.True(activeReference.BlocksRemoval);
            Assert.False(blocked.CanRemove);
            var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.RemoveSourceAsync(
                    seeded.DraftVersionId,
                    created.Source.Id,
                    new RemoveSpaceSourceRequest(
                        blocked.VersionContentRevision,
                        blocked.SourceRowVersion),
                    "blocked-remove"));
            Assert.Equal(SpaceErrorCodes.SourceReferenced, error.Code);

            job.RequestCancellation(execution.ActorId, Now);
            await context.SaveChangesAsync();
            var retained = await service.GetSourceRemovalPreviewAsync(
                seeded.DraftVersionId,
                created.Source.Id);
            var auditReference = Assert.Single(
                retained.References,
                reference =>
                    reference.Code ==
                    SpaceSourceRemovalReferenceCodes.JobAudit);
            Assert.False(auditReference.BlocksRemoval);
            Assert.True(retained.CanRemove);
            await service.RemoveSourceAsync(
                seeded.DraftVersionId,
                created.Source.Id,
                new RemoveSpaceSourceRequest(
                    retained.VersionContentRevision,
                    retained.SourceRowVersion),
                "remove-after-cancel");

            context.ChangeTracker.Clear();
            Assert.True(await context.Jobs.IgnoreQueryFilters().AnyAsync(
                candidate => candidate.Id == job.Id));
        });
    }

    [SqlServerFact]
    public async Task Active_design_revision_blocks_source_removal()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            var seeded = await SeedWritableDesignAsync(
                connectionString,
                execution,
                clock);
            var file = NewFile(
                execution.TenantId,
                clean: true,
                retainUntilUtc: Now.AddDays(30));
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            context.Files.Add(file);
            await context.SaveChangesAsync();
            var service = NewDesignService(
                context,
                execution,
                clock,
                seeded.SiteId);
            var created = await service.CreateSourceAsync(
                seeded.DraftVersionId,
                new CreateSpaceSourceRequest(
                    file.Id,
                    "Pdf",
                    "Design source"),
                "create-design-source");
            var source = await context.Sources.SingleAsync(
                candidate => candidate.Id == created.Source.Id);
            var floor = await context.FloorRevisions.SingleAsync(
                candidate =>
                    candidate.ModelVersionId == seeded.DraftVersionId &&
                    candidate.LogicalId == seeded.FloorLogicalId);
            floor.AttachSource(source, "manual-floor-reference");
            await context.SaveChangesAsync();

            var preview = await service.GetSourceRemovalPreviewAsync(
                seeded.DraftVersionId,
                source.Id);
            var designReference = Assert.Single(
                preview.References,
                reference =>
                    reference.Code ==
                    SpaceSourceRemovalReferenceCodes.DesignRevisions);
            Assert.True(designReference.BlocksRemoval);
            Assert.False(preview.CanRemove);

            var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.RemoveSourceAsync(
                    seeded.DraftVersionId,
                    source.Id,
                    new RemoveSpaceSourceRequest(
                        preview.VersionContentRevision,
                        preview.SourceRowVersion),
                    "blocked-design-remove"));
            Assert.Equal(SpaceErrorCodes.SourceReferenced, error.Code);
            Assert.False(source.IsDeleted);
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
        Guid FloorLogicalId,
        Guid ClientInstanceId,
        Guid LeaseId)>
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
        var clientInstanceId = Guid.NewGuid();
        var leaseNow = await context.Database
            .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
            .SingleAsync();
        leaseNow = DateTime.SpecifyKind(leaseNow, DateTimeKind.Utc);
        var lease = SpaceEditLease.Create(
            execution.TenantId,
            draft.Id,
            floor.LogicalId,
            execution.ActorId,
            "Space file safety test",
            clientInstanceId,
            leaseNow,
            TimeSpan.FromMinutes(5));
        context.AddRange(draft, floor, lease);
        await context.SaveChangesAsync();
        return (
            model.SiteId,
            draft.Id,
            floor.LogicalId,
            clientInstanceId,
            lease.LeaseId);
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
