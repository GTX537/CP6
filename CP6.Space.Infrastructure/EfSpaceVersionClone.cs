using System.Data;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceVersionCloneStore : ISpaceVersionCloneStore
{
    private const int StartRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;

    public EfSpaceVersionCloneStore(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
    }

    public async Task<SpaceVersionCloneStartResult> StartAsync(
        SpaceVersionCloneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        var normalizedName = NormalizeName(request.Name);

        for (var attempt = 0; attempt < StartRetries; attempt++)
        {
            _context.ChangeTracker.Clear();
            try
            {
                return await StartOnceAsync(
                    request with { Name = normalizedName },
                    cancellationToken);
            }
            catch (Exception exception)
                when (ContainsSqlError(exception, 1205))
            {
                _context.ChangeTracker.Clear();
                if (attempt + 1 >= StartRetries)
                {
                    throw new SpaceVersionConflictException(
                        "Another request won the model Draft reservation.");
                }
            }
            catch (DbUpdateException exception)
                when (IsUniqueViolation(exception))
            {
                _context.ChangeTracker.Clear();
                var existing = await FindExistingAsync(
                    request with { Name = normalizedName },
                    cancellationToken);
                if (existing is not null)
                    return existing;
                throw new SpaceVersionConflictException(
                    "Another active Draft won the clone reservation.");
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                var existing = await FindExistingAsync(
                    request with { Name = normalizedName },
                    cancellationToken);
                if (existing is not null)
                    return existing;
                throw new SpaceVersionConflictException(
                    "Another request changed the model Draft reservation.");
            }
        }

        throw new SpaceVersionConflictException(
            "The clone reservation could not be serialized.");
    }

    private async Task<SpaceVersionCloneStartResult> StartOnceAsync(
        SpaceVersionCloneRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var existing = await FindExistingAsync(request, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var model = await _context.Models.SingleOrDefaultAsync(
                            candidate => candidate.Id == request.ModelId,
                            cancellationToken)
                        ?? throw new KeyNotFoundException(
                            "The Space model was not found.");
            if (model.ActiveDraftVersionId.HasValue)
            {
                throw new SpaceVersionConflictException(
                    "The model already has an active Draft reservation.");
            }
            if (!model.CurrentPublishedVersionId.HasValue)
            {
                throw new SpaceVersionStateException(
                    "A current Published version is required before cloning.");
            }

            var source = await _context.Versions.SingleAsync(
                version => version.Id == model.CurrentPublishedVersionId.Value,
                cancellationToken);
            if (source.Status != SpaceVersionStatus.Published ||
                string.IsNullOrWhiteSpace(source.ContentHash))
            {
                throw new SpaceVersionStateException(
                    "The current version is not a complete Published snapshot.");
            }

            var nextVersionNo =
                (await _context.Versions
                    .Where(version => version.ModelId == model.Id)
                    .MaxAsync(
                        version => (long?)version.VersionNo,
                        cancellationToken) ?? 0) + 1;
            var target = SpaceModelVersion.CreateInitializingClone(
                _execution.TenantId,
                model.Id,
                nextVersionNo,
                request.Name,
                source.Id,
                request.OperationId);
            model.ReserveDraft(target);

            var payload = new SpaceVersionClonePayload(
                model.Id,
                source.Id,
                target.Id,
                request.OperationId);
            var inputHash = source.ContentHash;
            var businessKey = SpaceJobBusinessKey.Create(
                new SpaceJobEnqueueRequest(
                    SpaceJobType.CloneVersion,
                    SpaceJobSubjectType.ModelVersion,
                    target.Id,
                    inputHash,
                    SpaceVersionCloneContract.ProcessorVersion,
                    request.OperationId.ToString("N")));
            var job = SpaceJob.CreateQueued(
                _execution.TenantId,
                SpaceJobType.CloneVersion,
                SpaceJobSubjectType.ModelVersion,
                target.Id,
                businessKey,
                inputHash,
                priority: 50,
                maxAttempts: 3,
                _execution.ActorId,
                RequireUtcNow(),
                CorrelationId(request.OperationId),
                JsonSerializer.Serialize(payload, JsonOptions));

            _context.Versions.Add(target);
            _context.Jobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(target, job, reused: false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SpaceVersionCloneStartResult?> FindExistingAsync(
        SpaceVersionCloneRequest request,
        CancellationToken cancellationToken)
    {
        var version = await _context.Versions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.ModelId == request.ModelId &&
                    candidate.CloneOperationId == request.OperationId,
                cancellationToken);
        if (version is null)
            return null;
        if (!string.Equals(version.Name, request.Name, StringComparison.Ordinal) ||
            !version.BasedOnVersionId.HasValue)
        {
            throw new SpaceVersionConflictException(
                "The clone operation ID was already used with different input.");
        }

        var job = await _context.Jobs
                      .AsNoTracking()
                      .Where(candidate =>
                          candidate.JobType == SpaceJobType.CloneVersion &&
                          candidate.SubjectType == SpaceJobSubjectType.ModelVersion &&
                          candidate.SubjectId == version.Id)
                      .OrderBy(candidate => candidate.RequestedAtUtc)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new SpaceVersionStateException(
                      "The clone reservation is missing its Job ledger entry.");
        return Result(version, job, reused: true);
    }

    private static SpaceVersionCloneStartResult Result(
        SpaceModelVersion version,
        SpaceJob job,
        bool reused) =>
        new(
            version.Id,
            version.VersionNo,
            version.Status,
            job.Id,
            job.Status,
            reused);

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty ||
            _execution.TenantId != _context.CurrentTenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified current Space tenant and actor are required.");
        }
    }

    private Guid CorrelationId(Guid fallback) =>
        _execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : fallback;

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string NormalizeName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
        {
            throw new ArgumentException(
                "Version name is required and cannot exceed 200 characters.",
                nameof(value));
        }
        return normalized;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        ContainsSqlError(exception, 2601) ||
        ContainsSqlError(exception, 2627);

    private static bool ContainsSqlError(Exception? exception, int number)
    {
        while (exception is not null)
        {
            if (exception is SqlException sqlException &&
                sqlException.Number == number)
            {
                return true;
            }
            exception = exception.InnerException;
        }
        return false;
    }
}

public sealed class EfSpaceVersionCloneProcessor :
    ISpaceVersionCloneProcessor,
    ISpaceVersionSnapshotCloner
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;
    private readonly ISpaceJobLeaseStore _leases;

    public EfSpaceVersionCloneProcessor(
        SpaceContext context,
        ISpaceClock clock,
        ISpaceJobLeaseStore leases)
    {
        _context = context;
        _clock = clock;
        _leases = leases;
    }

    public async Task<SpaceVersionCloneCounts> CloneSnapshotAsync(
        SpaceVersionSnapshotCloneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_context.CurrentTenantId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified tenant is required to clone a version snapshot.");
        }
        if (request.HistoricalVersionId == Guid.Empty ||
            request.TargetVersionId == Guid.Empty ||
            request.RequestedBy == Guid.Empty)
        {
            throw new ArgumentException(
                "Historical, target, and requester identities are required.",
                nameof(request));
        }
        if (request.RequestedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The snapshot clone timestamp must be UTC.",
                nameof(request));
        }

        var source = await _context.Versions
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == request.HistoricalVersionId,
                cancellationToken);
        var target = await _context.Versions
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == request.TargetVersionId,
                cancellationToken);
        if (source.Purpose != SpaceModelVersionPurpose.Production ||
            source.Status is not (
                SpaceVersionStatus.Published or
                SpaceVersionStatus.Superseded) ||
            !string.Equals(
                source.ContentHash,
                request.HistoricalContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceVersionStateException(
                "The historical source is no longer an immutable published snapshot.");
        }
        if (target.ModelId != source.ModelId ||
            target.BasedOnVersionId != source.Id)
        {
            throw new SpaceVersionStateException(
                "The target is not bound to the requested historical snapshot.");
        }

        if (await TargetContainsSnapshotAsync(target.Id, cancellationToken))
        {
            if (target.Status != SpaceVersionStatus.Initializing)
                return await CountSnapshotAsync(target.Id, cancellationToken);
            throw new SpaceVersionStateException(
                "An initializing historical clone contains partial snapshot rows.");
        }
        if (target.Status != SpaceVersionStatus.Initializing)
        {
            throw new SpaceVersionStateException(
                "The historical clone target is not initializing.");
        }

        await CloneSnapshotSqlAsync(
            source.Id,
            target.Id,
            _context.CurrentTenantId,
            request.RequestedBy,
            request.RequestedAtUtc,
            cancellationToken);
        return await CountSnapshotAsync(target.Id, cancellationToken);
    }

    public async Task<SpaceVersionCloneCounts> ProcessAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ProcessOnceAsync(lease, cancellationToken);
        }
        catch (SpaceJobLeaseLostException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SpaceVersionStateException)
        {
            await _leases.FailJobAsync(
                lease,
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.VersionStateInvalid,
                "The clone source or reservation is no longer valid.",
                cancellationToken: CancellationToken.None);
            throw;
        }
        catch (SpaceVersionConflictException)
        {
            await _leases.FailJobAsync(
                lease,
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.VersionConflict,
                "The clone reservation conflicted with current model state.",
                cancellationToken: CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SpaceJobLeaseLostException(
                "The clone worker lost its database fencing token.",
                exception);
        }
        catch
        {
            await _leases.FailJobAsync(
                lease,
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.VersionStateInvalid,
                "The clone snapshot could not be completed.",
                cancellationToken: CancellationToken.None);
            throw new SpaceVersionStateException(
                "The clone snapshot could not be completed.");
        }
    }

    private async Task<SpaceVersionCloneCounts> ProcessOnceAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.TenantId != _context.CurrentTenantId ||
            lease.JobType != SpaceJobType.CloneVersion ||
            lease.SubjectType != SpaceJobSubjectType.ModelVersion)
        {
            throw new SpaceJobLeaseLostException(
                "The lease is not a compatible clone Job for the current tenant.");
        }

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            var now = RequireUtcNow();
            var job = await _context.Jobs.SingleOrDefaultAsync(
                          candidate => candidate.Id == lease.JobId,
                          cancellationToken)
                      ?? throw new SpaceJobLeaseLostException(
                          "The clone Job no longer exists.");
            job.EnsureLease(lease.AttemptId, lease.WorkerId, now);
            var attempt = await _context.JobAttempts.SingleAsync(
                candidate =>
                    candidate.Id == lease.AttemptId &&
                    candidate.JobId == lease.JobId &&
                    candidate.Outcome == SpaceJobAttemptOutcome.Running,
                cancellationToken);
            var payload = ReadPayload(job);
            if (payload.TargetVersionId != job.SubjectId)
            {
                throw new SpaceVersionStateException(
                    "Clone Job subject and payload differ.");
            }

            var target = await _context.Versions.SingleAsync(
                version => version.Id == payload.TargetVersionId,
                cancellationToken);
            var model = await _context.Models.SingleAsync(
                candidate => candidate.Id == payload.ModelId,
                cancellationToken);
            var planningBranch =
                payload.PlanningScenarioBranchId.HasValue
                    ? await _context.PlanningScenarioBranches.SingleAsync(
                        candidate =>
                            candidate.Id ==
                            payload.PlanningScenarioBranchId.Value,
                        cancellationToken)
                    : null;

            if (job.CancellationRequestedAtUtc.HasValue)
            {
                target.AbandonInitialization();
                if (model.ActiveDraftVersionId == target.Id)
                    model.ReleaseFailedClone(target);
                job.AcknowledgeCancellation(lease.AttemptId, lease.WorkerId, now);
                attempt.Cancel(now);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return EmptyCounts();
            }

            ValidateReservation(
                payload,
                model,
                target,
                planningBranch,
                job.Id);
            var source = await _context.Versions
                .AsNoTracking()
                .SingleAsync(
                    version => version.Id == payload.SourceVersionId,
                    cancellationToken);
            var sourceStatusValid = planningBranch is not null
                ? source.Status is
                    SpaceVersionStatus.Published or
                    SpaceVersionStatus.Superseded
                : source.Status == SpaceVersionStatus.Published;
            if (source.Purpose != SpaceModelVersionPurpose.Production ||
                !sourceStatusValid ||
                string.IsNullOrWhiteSpace(source.ContentHash))
            {
                throw new SpaceVersionStateException(
                    "Clone source is not a complete Published version.");
            }
            if (await TargetContainsSnapshotAsync(target.Id, cancellationToken))
            {
                throw new SpaceVersionStateException(
                    "Initializing clone target already contains snapshot rows.");
            }

            await CloneSnapshotSqlAsync(
                source.Id,
                target.Id,
                lease.TenantId,
                job.RequestedBy,
                now,
                cancellationToken);
            var counts = await CountSnapshotAsync(target.Id, cancellationToken);
            var completedAt = RequireUtcNow();
            var checkpointJson = JsonSerializer.Serialize(counts, JsonOptions);
            var step = SpaceJobStep.Start(
                lease.TenantId,
                lease.AttemptId,
                1,
                "CloneSnapshot",
                now);
            step.Complete(checkpointJson, source.ContentHash, completedAt);
            _context.JobSteps.Add(step);

            target.CompleteInitialization(source.ContentRevision);
            job.ReportProgress(
                lease.AttemptId,
                lease.WorkerId,
                counts.Total,
                counts.Total,
                "CloneSnapshot",
                completedAt);
            job.Complete(
                lease.AttemptId,
                lease.WorkerId,
                completedAt,
                checkpointJson);
            attempt.Succeed(completedAt);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return counts;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateReservation(
        SpaceVersionClonePayload payload,
        SpaceModel model,
        SpaceModelVersion target,
        SpacePlanningScenarioBranch? planningBranch,
        Guid cloneJobId)
    {
        if (target.ModelId != model.Id ||
            target.Status != SpaceVersionStatus.Initializing ||
            target.BasedOnVersionId != payload.SourceVersionId ||
            target.CloneOperationId != payload.OperationId)
        {
            throw new SpaceVersionStateException(
                "The clone reservation no longer matches the model pointers.");
        }

        if (planningBranch is not null)
        {
            if (target.Purpose != SpaceModelVersionPurpose.PlanningScenario ||
                model.ActiveDraftVersionId == target.Id ||
                planningBranch.TenantId != target.TenantId ||
                planningBranch.ModelId != model.Id ||
                planningBranch.SiteId != model.SiteId ||
                planningBranch.BasePublishedVersionId !=
                    payload.SourceVersionId ||
                planningBranch.ScenarioVersionId != target.Id ||
                planningBranch.CloneJobId != cloneJobId ||
                planningBranch.Id != payload.PlanningScenarioBranchId)
            {
                throw new SpaceVersionStateException(
                    "The planning scenario clone binding is invalid.");
            }
            return;
        }

        if (target.Purpose != SpaceModelVersionPurpose.Production ||
            model.ActiveDraftVersionId != target.Id ||
            model.CurrentPublishedVersionId != payload.SourceVersionId)
        {
            throw new SpaceVersionStateException(
                "The production clone no longer matches the model pointers.");
        }
    }

    private static SpaceVersionClonePayload ReadPayload(SpaceJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceVersionClonePayload>(
                       job.PayloadJson,
                       JsonOptions)
                   ?? throw new SpaceVersionStateException(
                       "Clone Job payload is empty.");
        }
        catch (JsonException)
        {
            throw new SpaceVersionStateException("Clone Job payload is invalid.");
        }
    }

    private async Task<bool> TargetContainsSnapshotAsync(
        Guid targetVersionId,
        CancellationToken cancellationToken) =>
        await _context.Sources.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.FloorRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.ZoneRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.AisleRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.RackRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.RackLevelRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.LocationRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.ElementRevisions.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.ElementAttributes.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.LocationExternalBindings.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken) ||
        await _context.DesignAttributes.AnyAsync(
            row => row.ModelVersionId == targetVersionId,
            cancellationToken);

    private async Task<SpaceVersionCloneCounts> CountSnapshotAsync(
        Guid targetVersionId,
        CancellationToken cancellationToken) =>
        new(
            await _context.Sources.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.FloorRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.ZoneRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.AisleRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.RackRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.RackLevelRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.LocationRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.ElementRevisions.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.ElementAttributes.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.LocationExternalBindings.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken),
            await _context.DesignAttributes.CountAsync(
                row => row.ModelVersionId == targetVersionId,
                cancellationToken));

    private Task<int> CloneSnapshotSqlAsync(
        Guid sourceVersionId,
        Guid targetVersionId,
        Guid tenantId,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             SET NOCOUNT ON;

             DECLARE @SourceMap TABLE
             (
                 [OldId] uniqueidentifier NOT NULL PRIMARY KEY,
                 [NewId] uniqueidentifier NOT NULL UNIQUE
             );
             INSERT INTO @SourceMap ([OldId], [NewId])
             SELECT [Id], NEWID()
             FROM [Space_ModelSource]
             WHERE [TenantId] = {tenantId}
               AND [ModelVersionId] = {sourceVersionId};

             INSERT INTO [Space_ModelSource]
                 ([Id], [ModelVersionId], [SourceType], [FileId], [DisplayName],
                  [Sha256], [ParserVersion], [MappingProfileId],
                  [MappingProfileVersion], [Unit], [ScaleToMillimeters],
                  [TransformJson], [State], [ImportedCommandBatchId],
                  [TenantId], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc],
                  [ModifiedBy], [IsDeleted])
             SELECT m.[NewId], {targetVersionId}, s.[SourceType], s.[FileId],
                    s.[DisplayName], s.[Sha256], s.[ParserVersion],
                    s.[MappingProfileId], s.[MappingProfileVersion], s.[Unit],
                    s.[ScaleToMillimeters], s.[TransformJson], s.[State],
                    s.[ImportedCommandBatchId], {tenantId}, {nowUtc}, {actorId},
                    NULL, NULL, s.[IsDeleted]
             FROM [Space_ModelSource] s
             INNER JOIN @SourceMap m ON m.[OldId] = s.[Id]
             WHERE s.[TenantId] = {tenantId}
               AND s.[ModelVersionId] = {sourceVersionId};

             DECLARE @CalibrationMap TABLE
             (
                 [OldId] uniqueidentifier NOT NULL PRIMARY KEY,
                 [NewId] uniqueidentifier NOT NULL UNIQUE
             );
             INSERT INTO @CalibrationMap ([OldId], [NewId])
             SELECT [Id], NEWID()
             FROM [Space_UnderlayCalibration]
             WHERE [TenantId] = {tenantId}
               AND [ModelVersionId] = {sourceVersionId}
               AND [IsDeleted] = 0;

             INSERT INTO [Space_UnderlayCalibration]
                 ([Id], [ModelVersionId], [FloorLogicalId], [SourceId],
                  [PageNumber], [PixelWidth], [PixelHeight],
                  [Point1PixelX], [Point1PixelY], [Point1WorldX],
                  [Point1WorldY], [Point2PixelX], [Point2PixelY],
                  [Point2WorldX], [Point2WorldY], [ValidationPixelX],
                  [ValidationPixelY], [ValidationWorldX], [ValidationWorldY],
                  [MillimetersPerPixel], [OffsetX], [OffsetY], [RotationZ],
                  [ValidationErrorMillimeters], [ErrorThresholdMillimeters],
                  [TenantId], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc],
                  [ModifiedBy], [IsDeleted])
             SELECT cm.[NewId], {targetVersionId}, c.[FloorLogicalId],
                    sm.[NewId], c.[PageNumber], c.[PixelWidth],
                    c.[PixelHeight], c.[Point1PixelX], c.[Point1PixelY],
                    c.[Point1WorldX], c.[Point1WorldY], c.[Point2PixelX],
                    c.[Point2PixelY], c.[Point2WorldX], c.[Point2WorldY],
                    c.[ValidationPixelX], c.[ValidationPixelY],
                    c.[ValidationWorldX], c.[ValidationWorldY],
                    c.[MillimetersPerPixel], c.[OffsetX], c.[OffsetY],
                    c.[RotationZ], c.[ValidationErrorMillimeters],
                    c.[ErrorThresholdMillimeters], {tenantId}, {nowUtc},
                    {actorId}, NULL, NULL, 0
             FROM [Space_UnderlayCalibration] c
             INNER JOIN @CalibrationMap cm ON cm.[OldId] = c.[Id]
             INNER JOIN @SourceMap sm ON sm.[OldId] = c.[SourceId]
             WHERE c.[TenantId] = {tenantId}
               AND c.[ModelVersionId] = {sourceVersionId}
               AND c.[IsDeleted] = 0;

             INSERT INTO [Space_FloorRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [SiteLogicalId], [Level], [FloorCode], [Name],
                  [Elevation], [Height], [BoundaryJson], [CoordinateSystem],
                  [UnderlaySourceId], [UnderlayCalibrationId],
                  [UnderlayScale], [UnderlayOffsetX],
                  [UnderlayOffsetY], [UnderlayRotationZ], [Revision],
                  [TenantId], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc],
                  [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[SiteLogicalId], r.[Level],
                    r.[FloorCode], r.[Name], r.[Elevation], r.[Height],
                    r.[BoundaryJson], r.[CoordinateSystem], um.[NewId],
                    cm.[NewId],
                    r.[UnderlayScale], r.[UnderlayOffsetX], r.[UnderlayOffsetY],
                    r.[UnderlayRotationZ], r.[Revision], {tenantId}, {nowUtc},
                    {actorId}, NULL, NULL, 0
             FROM [Space_FloorRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             LEFT JOIN @SourceMap um ON um.[OldId] = r.[UnderlaySourceId]
             LEFT JOIN @CalibrationMap cm
                ON cm.[OldId] = r.[UnderlayCalibrationId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_ZoneRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [FloorLogicalId], [ZoneCode], [Name],
                  [ZoneType], [PolygonJson], [Color], [CapabilityFlags], [TenantId],
                  [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [ModifiedBy],
                  [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[FloorLogicalId],
                    r.[ZoneCode], r.[Name], r.[ZoneType], r.[PolygonJson], r.[Color],
                    r.[CapabilityFlags], {tenantId}, {nowUtc}, {actorId},
                    NULL, NULL, 0
             FROM [Space_ZoneRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_AisleRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [ZoneLogicalId], [AisleCode], [Name],
                  [PolygonJson], [CenterlineJson], [Direction], [TenantId], [CreatedAtUtc],
                  [CreatedBy], [ModifiedAtUtc], [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[ZoneLogicalId],
                    r.[AisleCode], r.[Name], r.[PolygonJson], r.[CenterlineJson],
                    r.[Direction], {tenantId}, {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_AisleRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_RackRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [FloorLogicalId], [ZoneLogicalId],
                  [AisleLogicalId], [RackCode], [Name], [RackType],
                  [TemplateVersionId], [X], [Y], [Z], [RotationZ], [Width],
                  [Depth], [Height], [TenantId],
                  [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [ModifiedBy],
                  [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[FloorLogicalId],
                    r.[ZoneLogicalId], r.[AisleLogicalId], r.[RackCode],
                    r.[Name], r.[RackType], r.[TemplateVersionId], r.[X], r.[Y],
                    r.[Z], r.[RotationZ],
                    r.[Width], r.[Depth], r.[Height], {tenantId}, {nowUtc},
                    {actorId}, NULL, NULL, 0
             FROM [Space_RackRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_RackLevelRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [RackLogicalId], [LevelNo], [BottomZ],
                  [ClearHeight], [BinCount], [DepthCount], [CellWidth],
                  [CellDepth], [BeamHeight], [MaxLoad], [TenantId], [CreatedAtUtc],
                  [CreatedBy], [ModifiedAtUtc], [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[RackLogicalId],
                    r.[LevelNo], r.[BottomZ], r.[ClearHeight], r.[BinCount],
                    r.[DepthCount], r.[CellWidth], r.[CellDepth], r.[BeamHeight],
                    r.[MaxLoad],
                    {tenantId}, {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_RackLevelRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_LocationRevision]
                 ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                  [LifecycleState], [FloorLogicalId], [RackLogicalId],
                  [LocationCode], [ColumnNo], [LevelNo], [DepthNo], [Width],
                  [Height], [Depth], [MaxLoad], [LocationType], [CodeOrigin],
                  [ExternalBindingState], [TenantId], [CreatedAtUtc],
                  [CreatedBy], [ModifiedAtUtc], [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, r.[LogicalId], sm.[NewId],
                    r.[SourceRef], r.[LifecycleState], r.[FloorLogicalId],
                    r.[RackLogicalId], r.[LocationCode], r.[ColumnNo],
                    r.[LevelNo], r.[DepthNo], r.[Width], r.[Height], r.[Depth],
                    r.[MaxLoad], r.[LocationType], r.[CodeOrigin],
                    r.[ExternalBindingState],
                    {tenantId}, {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_LocationRevision] r
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_LocationExternalBinding]
                 ([Id], [ModelVersionId], [LocationLogicalId], [AdapterId],
                  [WarehouseCode], [ExternalLocationId], [BindingMode],
                  [SourceId], [SourceRef], [TenantId], [CreatedAtUtc],
                  [CreatedBy], [ModifiedAtUtc], [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, b.[LocationLogicalId],
                    b.[AdapterId], b.[WarehouseCode], b.[ExternalLocationId],
                    b.[BindingMode], sm.[NewId], b.[SourceRef], {tenantId},
                    {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_LocationExternalBinding] b
             INNER JOIN @SourceMap sm ON sm.[OldId] = b.[SourceId]
             WHERE b.[TenantId] = {tenantId}
               AND b.[ModelVersionId] = {sourceVersionId}
               AND b.[IsDeleted] = 0;

             INSERT INTO [Space_DesignAttribute]
                 ([Id], [ModelVersionId], [ObjectType], [ObjectLogicalId],
                  [Namespace], [Key], [Value], [Unit], [SourceId], [SourceRef],
                  [TenantId], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc],
                  [ModifiedBy], [IsDeleted])
             SELECT NEWID(), {targetVersionId}, a.[ObjectType],
                    a.[ObjectLogicalId], a.[Namespace], a.[Key], a.[Value],
                    a.[Unit], sm.[NewId], a.[SourceRef], {tenantId}, {nowUtc},
                    {actorId}, NULL, NULL, 0
             FROM [Space_DesignAttribute] a
             INNER JOIN @SourceMap sm ON sm.[OldId] = a.[SourceId]
             WHERE a.[TenantId] = {tenantId}
               AND a.[ModelVersionId] = {sourceVersionId}
               AND a.[IsDeleted] = 0;

             DECLARE @ElementMap TABLE
             (
                 [OldId] uniqueidentifier NOT NULL PRIMARY KEY,
                 [NewId] uniqueidentifier NOT NULL UNIQUE
             );
             INSERT INTO @ElementMap ([OldId], [NewId])
             SELECT [Id], NEWID()
             FROM [Space_ElementRevision]
             WHERE [TenantId] = {tenantId}
               AND [ModelVersionId] = {sourceVersionId}
               AND [IsDeleted] = 0;

               INSERT INTO [Space_ElementRevision]
                   ([Id], [ModelVersionId], [LogicalId], [SourceId], [SourceRef],
                    [LifecycleState], [FloorLogicalId], [ParentLogicalId],
                    [ElementType], [GeometryJson], [ModelAssetId], [ModelAssetScope],
                    [ModelAssetOwnerTenantId], [X], [Y], [Z],
                    [RotationZ], [Width], [Height], [Depth], [BusinessCode],
                  [LinkedEntityType], [LinkedLogicalId],
                  [IsManualCorrectionLocked], [UserCorrectionVersion],
                  [ManualCorrectionUpdatedBy], [ManualCorrectionUpdatedAtUtc],
                  [TenantId],
                  [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [ModifiedBy],
                  [IsDeleted])
             SELECT em.[NewId], {targetVersionId}, r.[LogicalId], sm.[NewId],
                      r.[SourceRef], r.[LifecycleState], r.[FloorLogicalId],
                      r.[ParentLogicalId], r.[ElementType], r.[GeometryJson],
                      r.[ModelAssetId], r.[ModelAssetScope],
                      r.[ModelAssetOwnerTenantId], r.[X], r.[Y], r.[Z], r.[RotationZ],
                    r.[Width], r.[Height], r.[Depth], r.[BusinessCode],
                    r.[LinkedEntityType], r.[LinkedLogicalId],
                    r.[IsManualCorrectionLocked], r.[UserCorrectionVersion],
                    r.[ManualCorrectionUpdatedBy],
                    r.[ManualCorrectionUpdatedAtUtc], {tenantId},
                    {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_ElementRevision] r
             INNER JOIN @ElementMap em ON em.[OldId] = r.[Id]
             LEFT JOIN @SourceMap sm ON sm.[OldId] = r.[SourceId]
             WHERE r.[TenantId] = {tenantId}
               AND r.[ModelVersionId] = {sourceVersionId}
               AND r.[IsDeleted] = 0;

             INSERT INTO [Space_ElementAttribute]
                 ([Id], [ModelVersionId], [ElementRevisionId], [Namespace],
                  [Key], [ValueType], [Value], [Unit], [TenantId],
                  [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [ModifiedBy],
                  [IsDeleted])
             SELECT NEWID(), {targetVersionId}, em.[NewId], a.[Namespace],
                    a.[Key], a.[ValueType], a.[Value], a.[Unit], {tenantId},
                    {nowUtc}, {actorId}, NULL, NULL, 0
             FROM [Space_ElementAttribute] a
             INNER JOIN @ElementMap em ON em.[OldId] = a.[ElementRevisionId]
             WHERE a.[TenantId] = {tenantId}
               AND a.[ModelVersionId] = {sourceVersionId}
               AND a.[IsDeleted] = 0;
             """,
            cancellationToken);

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceVersionCloneCounts EmptyCounts() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
