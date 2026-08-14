using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceHistoricalRepublishService :
    ISpaceHistoricalRepublishService
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;

    public SpaceHistoricalRepublishService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
    }

    public async Task<StartSpaceHistoricalRepublishResponse> StartAsync(
        Guid historicalVersionId,
        StartSpaceHistoricalRepublishRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        ValidateRequest(historicalVersionId, request);
        var normalizedKey = RequireIdempotencyKey(idempotencyKey);
        var requestHash = ComputeRequestHash(historicalVersionId, request);

        var replay = await _context.HistoricalRepublishes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.BusinessIdempotencyKey == normalizedKey,
                cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, requestHash);
            _access.EnsureSiteAccess(replay.SiteId, write: true);
            return new StartSpaceHistoricalRepublishResponse(
                await ToDtoAsync(replay.Id, cancellationToken),
                IdempotentReplay: true);
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var transactionCommitted = false;
        try
        {
            if (_context.Database.IsSqlServer())
            {
                var lockResource =
                    $"CP6:Space:Republish:{_execution.TenantId:D}:" +
                    Hash(normalizedKey);
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource = {lockResource},
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 15000;
                    IF @result < 0
                        THROW 51021, 'SPACE_REPUBLISH_LOCK_UNAVAILABLE', 1;
                    """,
                    cancellationToken);
            }

            var concurrentReplay = await _context.HistoricalRepublishes
                .SingleOrDefaultAsync(
                    value =>
                        value.BusinessIdempotencyKey == normalizedKey,
                    cancellationToken);
            if (concurrentReplay is not null)
            {
                EnsureReplay(concurrentReplay, requestHash);
                _access.EnsureSiteAccess(
                    concurrentReplay.SiteId,
                    write: true);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCommitted = true;
                }
                return new StartSpaceHistoricalRepublishResponse(
                    await ToDtoAsync(
                        concurrentReplay.Id,
                        cancellationToken),
                    IdempotentReplay: true);
            }

            var historical = await _context.Versions
                                 .SingleOrDefaultAsync(
                                     value =>
                                         value.Id == historicalVersionId,
                                     cancellationToken)
                             ?? throw NotFound(
                                 SpaceErrorCodes.VersionNotFound,
                                 "The historical model version was not found.");
            var model = await _context.Models
                            .SingleOrDefaultAsync(
                                value => value.Id == historical.ModelId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.ModelNotFound,
                            "The Space model was not found.");
            _access.EnsureSiteAccess(model.SiteId, write: true);
            EnsureHistoricalSource(historical);
            EnsurePublishedPrecondition(
                model,
                request.ExpectedPublishedVersionId);

            var current = await _context.Versions.SingleAsync(
                value => value.Id == request.ExpectedPublishedVersionId,
                cancellationToken);
            if (current.Status != SpaceVersionStatus.Published ||
                current.Purpose != SpaceModelVersionPurpose.Production)
            {
                throw Conflict(
                    SpaceErrorCodes.PublishedVersionChanged,
                    "The current Published pointer is not a complete production version.",
                    "refresh-version-history");
            }
            if (model.ActiveDraftVersionId.HasValue)
            {
                throw Conflict(
                    SpaceErrorCodes.VersionConflict,
                    "The model already has an active Draft reservation.",
                    "finish-or-abandon-active-draft");
            }
            var activePublish = await _context.PublishAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.SiteId == model.SiteId &&
                             value.OwnsPublishSlot,
                    cancellationToken);
            if (activePublish is not null)
            {
                throw Conflict(
                    SpaceErrorCodes.PublishSlotBusy,
                    $"Site publish attempt {activePublish.Id:D} is still active.",
                    "view-active-publish-attempt");
            }

            var nextVersionNo =
                (await _context.Versions
                    .Where(value => value.ModelId == model.Id)
                    .MaxAsync(
                        value => (long?)value.VersionNo,
                        cancellationToken) ?? 0) + 1;
            var now = RequireUtcNow();
            var correlationId = CorrelationId();
            var operation = SpaceHistoricalRepublish.Create(
                _execution.TenantId,
                model.SiteId,
                model.Id,
                historical.Id,
                current.Id,
                normalizedKey,
                requestHash,
                request.Reason,
                request.ApprovalReference,
                _execution.ActorId,
                now,
                correlationId);
            var targetName = NormalizeVersionName(
                request.NewVersionName,
                historical.VersionNo);
            var target = SpaceModelVersion.CreateInitializingClone(
                _execution.TenantId,
                model.Id,
                nextVersionNo,
                targetName,
                historical.Id,
                operation.Id);
            model.ReserveDraft(target);

            var enqueue = new SpaceJobEnqueueRequest(
                SpaceJobType.HistoricalRepublish,
                SpaceJobSubjectType.HistoricalRepublish,
                operation.Id,
                requestHash,
                SpaceHistoricalRepublishJobProcessor.Version,
                VariantKey: operation.Id.ToString("N"),
                Priority: 55,
                MaxAttempts: 5,
                PayloadJson: JsonSerializer.Serialize(
                    new
                    {
                        republishId = operation.Id,
                        historicalVersionId = historical.Id,
                        expectedPublishedVersionId = current.Id,
                        targetVersionId = target.Id,
                        requestHash,
                    },
                    Json));
            var job = SpaceJob.CreateQueued(
                _execution.TenantId,
                enqueue.JobType,
                enqueue.SubjectType,
                enqueue.SubjectId,
                SpaceJobBusinessKey.Create(enqueue),
                enqueue.InputHash,
                enqueue.Priority,
                enqueue.MaxAttempts,
                _execution.ActorId,
                now,
                correlationId,
                enqueue.PayloadJson);
            operation.BindReservation(target.Id, job.Id);
            _context.AddRange(operation, target, job);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            }
            return new StartSpaceHistoricalRepublishResponse(
                await ToDtoAsync(operation.Id, cancellationToken),
                IdempotentReplay: false);
        }
        catch
        {
            if (transaction is not null && !transactionCommitted)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SpaceHistoricalRepublishDto> GetAsync(
        Guid republishId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (republishId == Guid.Empty)
            throw Invalid("A non-empty republishId is required.");
        return await ToDtoAsync(republishId, cancellationToken);
    }

    private async Task<SpaceHistoricalRepublishDto> ToDtoAsync(
        Guid republishId,
        CancellationToken cancellationToken)
    {
        var operation = await _context.HistoricalRepublishes
                            .AsNoTracking()
                            .SingleOrDefaultAsync(
                                value => value.Id == republishId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.HistoricalRepublishNotFound,
                            "The historical republish operation was not found.");
        _access.EnsureSiteAccess(operation.SiteId, write: false);
        var target = await _context.Versions
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == operation.TargetVersionId,
                cancellationToken);
        var job = await _context.Jobs
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == operation.JobId,
                cancellationToken);
        var attemptStatus = operation.PublishAttemptId.HasValue
            ? await _context.PublishAttempts
                .AsNoTracking()
                .Where(value => value.Id == operation.PublishAttemptId.Value)
                .Select(value => (SpacePublishAttemptStatus?)value.Status)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return new SpaceHistoricalRepublishDto(
            operation.Id,
            operation.SiteId,
            operation.HistoricalVersionId,
            operation.ExpectedPublishedVersionId,
            operation.TargetVersionId,
            target.VersionNo.ToString(CultureInfo.InvariantCulture),
            target.Status.ToString(),
            operation.Status.ToString(),
            operation.Reason,
            operation.ApprovalReference,
            operation.RequestedBy,
            DateTime.SpecifyKind(
                operation.RequestedAtUtc,
                DateTimeKind.Utc),
            operation.CorrelationId,
            operation.JobId,
            job.Status.ToString(),
            operation.ValidationRunId,
            operation.PublishAttemptId,
            attemptStatus?.ToString());
    }

    private static void EnsureReplay(
        SpaceHistoricalRepublish operation,
        string requestHash)
    {
        if (!string.Equals(
                operation.RequestHash,
                requestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                SpaceErrorCodes.IdempotencyConflict,
                "The Idempotency-Key was already used for another republish request.",
                "use-new-idempotency-key");
        }
    }

    private static void EnsureHistoricalSource(SpaceModelVersion version)
    {
        if (version.Purpose != SpaceModelVersionPurpose.Production ||
            version.Status != SpaceVersionStatus.Superseded ||
            string.IsNullOrWhiteSpace(version.ContentHash))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.HistoricalVersionNotEligible,
                422,
                "The version cannot be republished as history.",
                "Only a complete Superseded production version can start a historical republish.",
                "select-superseded-version");
        }
    }

    private static void EnsurePublishedPrecondition(
        SpaceModel model,
        Guid expectedPublishedVersionId)
    {
        if (expectedPublishedVersionId == Guid.Empty ||
            model.CurrentPublishedVersionId != expectedPublishedVersionId)
        {
            throw Conflict(
                SpaceErrorCodes.PublishedVersionChanged,
                "The current Published version does not match the rollback precondition.",
                "refresh-version-history");
        }
    }

    private static void ValidateRequest(
        Guid historicalVersionId,
        StartSpaceHistoricalRepublishRequest request)
    {
        if (historicalVersionId == Guid.Empty)
            throw Invalid("A non-empty historicalVersionId is required.");
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedPublishedVersionId == Guid.Empty)
            throw Invalid("A non-empty expectedPublishedVersionId is required.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw Invalid("reason is required and cannot exceed 1000 characters.");
        if (request.ApprovalReference?.Trim().Length > 500)
            throw Invalid("approvalReference cannot exceed 500 characters.");
        if (request.NewVersionName?.Trim().Length > 200)
            throw Invalid("newVersionName cannot exceed 200 characters.");
    }

    private static string NormalizeVersionName(string? value, long versionNo)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? $"Republish historical version {versionNo}"
            : normalized;
    }

    private static string RequireIdempotencyKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "An Idempotency-Key header is required.",
                recoveryAction: "supply-idempotency-key");
        }
        if (normalized.Length > 128)
            throw Invalid("Idempotency-Key cannot exceed 128 characters.");
        return normalized;
    }

    private static string ComputeRequestHash(
        Guid historicalVersionId,
        StartSpaceHistoricalRepublishRequest request) =>
        Hash(string.Join(
            "\n",
            historicalVersionId.ToString("D"),
            request.ExpectedPublishedVersionId.ToString("D"),
            request.Reason.Trim(),
            request.ApprovalReference?.Trim() ?? "-",
            request.NewVersionName?.Trim() ?? "-"));

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private Guid CorrelationId() =>
        _execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : Guid.NewGuid();

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Space clock must return UTC.");
        return now;
    }

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

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The historical republish request is invalid.",
            detail,
            "correct-request");

    private static SpaceProblemException NotFound(
        string code,
        string detail) =>
        new(
            code,
            404,
            "The requested Space resource was not found.",
            detail,
            "refresh-resource");

    private static SpaceProblemException Conflict(
        string code,
        string detail,
        string recoveryAction) =>
        new(
            code,
            409,
            "The historical republish conflicts with current state.",
            detail,
            recoveryAction);
}

public sealed class SpaceHistoricalRepublishJobExecutor :
    ISpaceHistoricalRepublishJobExecutor
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;
    private readonly ISpaceVersionSnapshotCloner _cloner;
    private readonly ISpaceValidationProfileProvider _profiles;
    private readonly SpaceValidationEngine _validationEngine;
    private readonly ISpacePublishPreviewService _preview;
    private readonly ISpaceHistoricalRepublishPublishStarter _publish;
    private readonly EfSpaceValidationSnapshotReader _validationSnapshots;

    public SpaceHistoricalRepublishJobExecutor(
        SpaceContext context,
        ISpaceClock clock,
        ISpaceVersionSnapshotCloner cloner,
        ISpaceValidationProfileProvider profiles,
        SpaceValidationEngine validationEngine,
        ISpacePublishPreviewService preview,
        ISpaceHistoricalRepublishPublishStarter publish)
    {
        _context = context;
        _clock = clock;
        _cloner = cloner;
        _profiles = profiles;
        _validationEngine = validationEngine;
        _preview = preview;
        _publish = publish;
        _validationSnapshots = new EfSpaceValidationSnapshotReader(context);
    }

    public Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureLease(execution.Lease);
        return execution.StepCode switch
        {
            SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot =>
                CloneHistoricalSnapshotAsync(execution, cancellationToken),
            SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot =>
                ValidateHistoricalSnapshotAsync(execution, cancellationToken),
            SpaceHistoricalRepublishJobSteps.QueuePublish =>
                QueuePublishAsync(execution, cancellationToken),
            _ => throw Processing(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                "The historical republish Job contains an unknown step."),
        };
    }

    private async Task<SpaceJobStepOutput> CloneHistoricalSnapshotAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var state = await LoadStateAsync(execution.Lease, cancellationToken);
        if (state.Operation.Status != SpaceHistoricalRepublishStatus.Requested)
        {
            var existingCounts = await _cloner.CloneSnapshotAsync(
                CloneRequest(state),
                cancellationToken);
            return Output(
                new
                {
                    state.Operation.Id,
                    state.Operation.HistoricalVersionId,
                    state.Operation.TargetVersionId,
                    counts = existingCounts,
                    reused = true,
                });
        }

        if (state.Model.CurrentPublishedVersionId !=
            state.Operation.ExpectedPublishedVersionId)
        {
            state.Target.FailInitialization();
            state.Model.ReleaseFailedClone(state.Target);
            await _context.SaveChangesAsync(CancellationToken.None);
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishedVersionChanged,
                "The Published version changed before the historical snapshot was cloned.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var transactionCommitted = false;
        try
        {
            var counts = await _cloner.CloneSnapshotAsync(
                CloneRequest(state),
                cancellationToken);
            state.Target.CompleteInitialization(
                state.Historical.ContentRevision);
            state.Operation.MarkSnapshotCloned();
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            }
            return Output(
                new
                {
                    state.Operation.Id,
                    state.Operation.HistoricalVersionId,
                    state.Operation.TargetVersionId,
                    state.Historical.ContentHash,
                    counts,
                    reused = false,
                });
        }
        catch
        {
            if (transaction is not null && !transactionCommitted)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SpaceJobStepOutput> ValidateHistoricalSnapshotAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var state = await LoadStateAsync(execution.Lease, cancellationToken);
        if (state.Operation.ValidationRunId.HasValue)
        {
            var existing = await _context.ValidationRuns
                .AsNoTracking()
                .SingleAsync(
                    value =>
                        value.Id == state.Operation.ValidationRunId.Value,
                    cancellationToken);
            if (existing.Status == SpaceValidationStatus.Blocked)
            {
                throw Processing(
                    SpaceJobFailureKind.Input,
                    SpaceErrorCodes.ValidationBlocked,
                    "The historical snapshot is blocked by current validation rules.");
            }
            if (existing.Status != SpaceValidationStatus.Passed)
            {
                throw Processing(
                    SpaceJobFailureKind.Bug,
                    SpaceErrorCodes.JobProcessorFailed,
                    "The historical republish references a non-terminal validation.");
            }
            return ValidationOutput(state.Operation.Id, existing, reused: true);
        }
        if (state.Operation.Status !=
            SpaceHistoricalRepublishStatus.SnapshotCloned)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.VersionStateInvalid,
                "The historical snapshot has not completed cloning.");
        }
        if (state.Model.CurrentPublishedVersionId !=
            state.Operation.ExpectedPublishedVersionId)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishedVersionChanged,
                "The Published version changed before rollback validation.");
        }
        if (state.Target.Status == SpaceVersionStatus.Draft)
        {
            state.Target.BeginValidation();
            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (state.Target.Status != SpaceVersionStatus.Validating)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.VersionStateInvalid,
                "The historical clone cannot enter validation from its current state.");
        }

        var contentRevision = state.Target.ContentRevision;
        var profile = await _profiles.GetProfileAsync(
            state.Operation.TenantId,
            state.Operation.SiteId,
            state.Operation.CorrelationId,
            cancellationToken);
        var snapshot = await _validationSnapshots.ReadAsync(
            state.Model,
            state.Target,
            cancellationToken);
        var result = _validationEngine.Validate(snapshot, profile);

        _context.ChangeTracker.Clear();
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var blocked = false;
        var transactionCommitted = false;
        SpaceValidationRun? run = null;
        try
        {
            var operation = await _context.HistoricalRepublishes
                .SingleAsync(
                    value => value.Id == execution.Lease.SubjectId,
                    cancellationToken);
            var target = await _context.Versions.SingleAsync(
                value => value.Id == operation.TargetVersionId,
                cancellationToken);
            var model = await _context.Models.SingleAsync(
                value => value.Id == operation.ModelId,
                cancellationToken);
            if (operation.ValidationRunId.HasValue)
            {
                run = await _context.ValidationRuns.SingleAsync(
                    value => value.Id == operation.ValidationRunId.Value,
                    cancellationToken);
            }
            else
            {
                if (model.CurrentPublishedVersionId !=
                        operation.ExpectedPublishedVersionId ||
                    target.Status != SpaceVersionStatus.Validating ||
                    target.ContentRevision != contentRevision ||
                    !string.Equals(
                        result.ContentHash,
                        _validationEngine.ComputeContentHash(snapshot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (target.Status == SpaceVersionStatus.Validating)
                        target.CompleteValidationWithErrors();
                    await _context.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        transactionCommitted = true;
                    }
                    throw Processing(
                        SpaceJobFailureKind.Input,
                        SpaceErrorCodes.ValidationStale,
                        "The rollback target or Published pointer changed during validation.");
                }

                var now = RequireUtcNow();
                run = SpaceValidationRun.CreateQueued(
                    operation.TenantId,
                    target.Id,
                    target.ContentRevision,
                    result.ContentHash,
                    SpaceValidationRuleSet.Version,
                    profile.AdapterId,
                    profile.CapabilityHash,
                    operation.RequestedBy,
                    now,
                    operation.JobId,
                    operation.CorrelationId);
                run.Start(now);
                foreach (var candidate in result.Issues)
                {
                    _context.Issues.Add(
                        SpaceModelIssue.Create(
                            run.TenantId,
                            run.ModelVersionId,
                            candidate.SourceId,
                            run.JobId,
                            candidate.Severity,
                            candidate.Code,
                            candidate.SourceRef,
                            candidate.TargetLogicalId,
                            candidate.MessageArgsJson,
                            candidate.SuggestedActionCode,
                            candidate.GenerationRunId,
                            candidate.GenerationProposalId,
                            run.Id,
                            candidate.Category,
                            candidate.FieldPath,
                            candidate.EvidenceJson));
                }
                if (result.BlockingCount == 0)
                {
                    run.Pass(
                        result.BlockingCount,
                        result.WarningCount,
                        result.InfoCount,
                        now);
                    target.MarkReady(
                        result.ContentHash,
                        SpaceValidationRuleSet.Version,
                        profile.CapabilityHash);
                    operation.MarkValidationPassed(run.Id);
                }
                else
                {
                    run.Block(
                        result.BlockingCount,
                        result.WarningCount,
                        result.InfoCount,
                        now);
                    target.CompleteValidationWithErrors();
                    operation.MarkValidationBlocked(run.Id);
                    blocked = true;
                }
                _context.ValidationRuns.Add(run);
                await _context.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            }
        }
        catch
        {
            if (transaction is not null && !transactionCommitted)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (blocked || run!.Status == SpaceValidationStatus.Blocked)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ValidationBlocked,
                "The historical snapshot is blocked by current validation rules.");
        }
        return ValidationOutput(
            execution.Lease.SubjectId,
            run,
            reused: false);
    }

    private async Task<SpaceJobStepOutput> QueuePublishAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var state = await LoadStateAsync(execution.Lease, cancellationToken);
        if (state.Operation.PublishAttemptId.HasValue)
        {
            var existing = await _context.PublishAttempts
                .AsNoTracking()
                .SingleAsync(
                    value =>
                        value.Id == state.Operation.PublishAttemptId.Value,
                    cancellationToken);
            return Output(
                new
                {
                    state.Operation.Id,
                    publishAttemptId = existing.Id,
                    status = existing.Status.ToString(),
                    reused = true,
                });
        }
        if (state.Operation.Status !=
                SpaceHistoricalRepublishStatus.ValidationPassed ||
            !state.Operation.ValidationRunId.HasValue ||
            state.Target.Status != SpaceVersionStatus.Ready)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ValidationBlocked,
                "The historical clone does not have a passed current validation.");
        }
        if (state.Model.CurrentPublishedVersionId !=
            state.Operation.ExpectedPublishedVersionId)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishedVersionChanged,
                "The Published version changed before the rollback publish was queued.");
        }
        var validation = await _context.ValidationRuns
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == state.Operation.ValidationRunId.Value,
                cancellationToken);
        if (validation.Status != SpaceValidationStatus.Passed)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ValidationBlocked,
                "The rollback validation did not pass.");
        }
        var preview = await _preview.GetPreviewAsync(
            state.Target.Id,
            floorLogicalId: null,
            objectType: null,
            action: null,
            impactCode: null,
            includeNoOp: false,
            limit: 1,
            cursor: null,
            cancellationToken);
        if (!preview.Publishable)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ValidationBlocked,
                "The rollback publish plan contains blocking impact.");
        }
        if (preview.ValidationWarningCount > 0)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishWarningAcknowledgementRequired,
                "The rollback validation contains Warnings. Open the " +
                "generated Ready version in publish preview and confirm " +
                "the bound Warning set before publishing.");
        }

        var publish = await _publish.StartHistoricalRepublishAsync(
            state.Target.Id,
            new CreateSpacePublishAttemptRequest(
                state.Operation.ExpectedPublishedVersionId,
                validation.Id,
                preview.PlanHash,
                state.Operation.ApprovalReference),
            $"historical-republish:{state.Operation.Id:N}",
            new SpaceHistoricalRepublishPublishContext(
                state.Operation.Id,
                state.Operation.HistoricalVersionId,
                state.Operation.Reason,
                state.Operation.RequestedBy),
            cancellationToken);

        _context.ChangeTracker.Clear();
        var operation = await _context.HistoricalRepublishes.SingleAsync(
            value => value.Id == state.Operation.Id,
            cancellationToken);
        operation.MarkPublishQueued(publish.Attempt.Id);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Output(
            new
            {
                operation.Id,
                publishAttemptId = publish.Attempt.Id,
                publishJobId = publish.Attempt.JobId,
                publish.Attempt.PlanHash,
                publish.IdempotentReplay,
            });
    }

    private async Task<RepublishState> LoadStateAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var operation = await _context.HistoricalRepublishes
                            .SingleOrDefaultAsync(
                                value => value.Id == lease.SubjectId,
                                cancellationToken)
                        ?? throw Processing(
                            SpaceJobFailureKind.Input,
                            SpaceErrorCodes.HistoricalRepublishNotFound,
                            "The historical republish operation was not found.");
        if (operation.JobId != lease.JobId)
        {
            throw Processing(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.JobProcessorFailed,
                "The historical republish Job does not match its operation.");
        }
        var model = await _context.Models.SingleAsync(
            value => value.Id == operation.ModelId,
            cancellationToken);
        var historical = await _context.Versions.SingleAsync(
            value => value.Id == operation.HistoricalVersionId,
            cancellationToken);
        var target = await _context.Versions.SingleAsync(
            value => value.Id == operation.TargetVersionId,
            cancellationToken);
        if (historical.ModelId != model.Id ||
            target.ModelId != model.Id ||
            target.BasedOnVersionId != historical.Id ||
            target.CloneOperationId != operation.Id ||
            historical.Purpose != SpaceModelVersionPurpose.Production ||
            historical.Status != SpaceVersionStatus.Superseded ||
            string.IsNullOrWhiteSpace(historical.ContentHash))
        {
            throw Processing(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.HistoricalVersionNotEligible,
                "The historical republish lineage is invalid.");
        }
        return new RepublishState(operation, model, historical, target);
    }

    private static SpaceVersionSnapshotCloneRequest CloneRequest(
        RepublishState state) =>
        new(
            state.Historical.Id,
            state.Target.Id,
            state.Historical.ContentHash!,
            state.Operation.RequestedBy,
            DateTime.SpecifyKind(
                state.Operation.RequestedAtUtc,
                DateTimeKind.Utc));

    private static SpaceJobStepOutput ValidationOutput(
        Guid republishId,
        SpaceValidationRun run,
        bool reused) =>
        Output(
            new
            {
                republishId,
                validationRunId = run.Id,
                status = run.Status.ToString(),
                run.ContentHash,
                run.BlockingCount,
                run.WarningCount,
                run.InfoCount,
                reused,
            });

    private static SpaceJobStepOutput Output<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        return new SpaceJobStepOutput(json, Hash(json));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void EnsureLease(SpaceJobLease lease)
    {
        if (lease.JobType != SpaceJobType.HistoricalRepublish ||
            lease.SubjectType != SpaceJobSubjectType.HistoricalRepublish)
        {
            throw Processing(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.JobProcessorFailed,
                "The lease is not a historical republish Job.");
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Space clock must return UTC.");
        return now;
    }

    private static SpaceJobProcessingException Processing(
        SpaceJobFailureKind kind,
        string code,
        string summary) =>
        new(kind, code, summary);

    private sealed record RepublishState(
        SpaceHistoricalRepublish Operation,
        SpaceModel Model,
        SpaceModelVersion Historical,
        SpaceModelVersion Target);
}
