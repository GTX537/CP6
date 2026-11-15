using System.Data.Common;
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
    private const string RollbackPermission = "space:model:rollback";
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceDesignWriteAuthority? _writeAuthority;

    public SpaceHistoricalRepublishService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceDesignWriteAuthority? writeAuthority = null)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _writeAuthority = writeAuthority;
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
            ? await SpaceDesignWriteTransaction.BeginForPermissionAsync(
                _context, _writeAuthority, RollbackPermission, cancellationToken)
            : null;
        var completingTransaction = false;
        try
        {
            var lockResource =
                $"CP6:Space:Republish:{_execution.TenantId:D}:" +
                Hash(normalizedKey);
            if (!await SpaceResourceLocks.TryAcquireTransactionAsync(
                    _context, lockResource, 15000, cancellationToken))
                throw new InvalidOperationException("SPACE_REPUBLISH_LOCK_UNAVAILABLE");

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
                    completingTransaction = true;
                    await SpaceDesignWriteTransaction.CommitAsync(_context, _writeAuthority, transaction, cancellationToken);
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
                completingTransaction = true;
                await SpaceDesignWriteTransaction.CommitAsync(_context, _writeAuthority, transaction, cancellationToken);
            }
            return new StartSpaceHistoricalRepublishResponse(
                await ToDtoAsync(operation.Id, cancellationToken),
                IdempotentReplay: false);
        }
        catch
        {
            if (transaction is not null && !completingTransaction)
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
    private readonly ISpaceArtifactWriteAuthority? _writeAuthority;

    public SpaceHistoricalRepublishJobExecutor(
        SpaceContext context,
        ISpaceClock clock,
        ISpaceVersionSnapshotCloner cloner,
        ISpaceValidationProfileProvider profiles,
        SpaceValidationEngine validationEngine,
        ISpacePublishPreviewService preview,
        ISpaceHistoricalRepublishPublishStarter publish,
        ISpaceArtifactWriteAuthority? writeAuthority = null)
    {
        _context = context;
        _clock = clock;
        _cloner = cloner;
        _profiles = profiles;
        _validationEngine = validationEngine;
        _preview = preview;
        _publish = publish;
        _writeAuthority = writeAuthority;
        _validationSnapshots = new EfSpaceValidationSnapshotReader(context);
    }

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        EnsureLease(execution.Lease);
        try
        {
            return await (execution.StepCode switch
            {
                SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot =>
                    CloneHistoricalSnapshotAsync(execution.Lease, cancellationToken),
                SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot =>
                    ValidateHistoricalSnapshotAsync(execution.Lease, cancellationToken),
                SpaceHistoricalRepublishJobSteps.QueuePublish =>
                    QueuePublishAsync(execution.Lease, cancellationToken),
                _ => throw Processing(SpaceJobFailureKind.Bug, SpaceErrorCodes.JobProcessorFailed,
                    "The historical republish Job contains an unknown step."),
            });
        }
        catch (SpaceJobProcessingException exception) when (exception.ErrorCode == "SPACE_ARTIFACT_COMMIT_UNKNOWN")
        {
            try
            {
                var state = await LoadStateAsync(execution.Lease, CancellationToken.None);
                var recovered = execution.StepCode switch
                {
                    SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot
                        when state.Operation.Status != SpaceHistoricalRepublishStatus.Requested =>
                        await ReadClonedSnapshotAsync(state, CancellationToken.None),
                    SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot
                        when state.Operation.ValidationRunId.HasValue =>
                        await ReadValidationAsync(state.Operation, CancellationToken.None),
                    SpaceHistoricalRepublishJobSteps.QueuePublish
                        when state.Operation.PublishAttemptId.HasValue =>
                        QueueOutput(state.Operation, (await FindChildAsync(state, CancellationToken.None))!),
                    _ => null,
                };
                if (recovered is not null)
                    return recovered;
            }
            catch (DbException readError) when (readError.IsTransient)
            {
                // Leave the durable outcome to the Job retry when readback is unavailable.
            }
            throw;
        }
    }

    private async Task<SpaceJobStepOutput> CloneHistoricalSnapshotAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(lease, cancellationToken);
        if (state.Operation.Status != SpaceHistoricalRepublishStatus.Requested)
            return await ReadClonedSnapshotAsync(state, cancellationToken);
        if (state.Target.Status == SpaceVersionStatus.Failed)
            throw CloneBaseChanged();

        await using var write = await SpaceArtifactWriteTransaction.BeginMetadataAsync(
            _context, lease, _writeAuthority, cancellationToken);
        state = await LoadStateAsync(lease, cancellationToken);
        if (state.Operation.Status != SpaceHistoricalRepublishStatus.Requested)
            return await ReadClonedSnapshotAsync(state, cancellationToken);
        if (state.Target.Status == SpaceVersionStatus.Failed)
            throw CloneBaseChanged();
        EnsureActiveDraft(state);
        if (state.Model.CurrentPublishedVersionId != state.Operation.ExpectedPublishedVersionId)
        {
            state.Target.FailInitialization();
            state.Model.ReleaseFailedClone(state.Target);
            await _context.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            throw CloneBaseChanged();
        }

        var counts = await _cloner.CloneSnapshotAsync(CloneRequest(state), cancellationToken);
        state.Target.CompleteInitialization(state.Historical.ContentRevision);
        state.Operation.MarkSnapshotCloned();
        await _context.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);
        return CloneOutput(state, counts, reused: false);
    }

    private async Task<SpaceJobStepOutput> ReadClonedSnapshotAsync(RepublishState state, CancellationToken cancellationToken)
    {
        if (state.Target.Status == SpaceVersionStatus.Initializing)
            throw Processing(SpaceJobFailureKind.Bug, SpaceErrorCodes.VersionStateInvalid,
                "The historical clone checkpoint does not match the initialized target.");
        var counts = await EfSpaceVersionCloneProcessor.CountSnapshotAsync(_context, state.Target.Id, cancellationToken);
        return CloneOutput(state, counts, reused: true);
    }

    private static SpaceJobStepOutput CloneOutput(RepublishState state, SpaceVersionCloneCounts counts, bool reused) =>
        Output(new { state.Operation.Id, state.Operation.HistoricalVersionId, state.Operation.TargetVersionId,
            state.Historical.ContentHash, counts, reused });

    private static SpaceJobProcessingException CloneBaseChanged() =>
        Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.PublishedVersionChanged,
            "The Published version changed before the historical snapshot was cloned.");

    private async Task<SpaceJobStepOutput> ValidateHistoricalSnapshotAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(lease, cancellationToken);
        if (state.Operation.ValidationRunId.HasValue)
            return await ReadValidationAsync(state.Operation, cancellationToken);
        EnsureValidationPhase(state);

        if (state.Target.Status == SpaceVersionStatus.Draft)
        {
            await using var start = await SpaceArtifactWriteTransaction.BeginMetadataAsync(
                _context, lease, _writeAuthority, cancellationToken);
            state = await LoadStateAsync(lease, cancellationToken);
            if (state.Operation.ValidationRunId.HasValue)
                return await ReadValidationAsync(state.Operation, cancellationToken);
            EnsureValidationPhase(state);
            if (state.Target.Status == SpaceVersionStatus.Draft)
            {
                state.Target.BeginValidation();
                await _context.SaveChangesAsync(cancellationToken);
                await start.CommitAsync(cancellationToken);
            }
        }

        var profile = await _profiles.GetProfileAsync(
            state.Operation.TenantId, state.Operation.SiteId, state.Operation.CorrelationId, cancellationToken);
        var snapshot = await _validationSnapshots.ReadAsync(state.Model, state.Target, cancellationToken);
        var result = _validationEngine.Validate(snapshot, profile);

        await using var finish = await SpaceArtifactWriteTransaction.BeginMetadataAsync(
            _context, lease, _writeAuthority, cancellationToken);
        state = await LoadStateAsync(lease, cancellationToken);
        if (state.Operation.ValidationRunId.HasValue)
            return await ReadValidationAsync(state.Operation, cancellationToken);

        var (operation, model, _, target) = state;
        if (operation.Status != SpaceHistoricalRepublishStatus.SnapshotCloned ||
            model.ActiveDraftVersionId != target.Id ||
            model.CurrentPublishedVersionId != operation.ExpectedPublishedVersionId ||
            target.Status != SpaceVersionStatus.Validating ||
            target.Id != snapshot.ModelVersionId ||
            target.ContentRevision != snapshot.ContentRevision)
        {
            if (operation.Status == SpaceHistoricalRepublishStatus.SnapshotCloned &&
                target.Status == SpaceVersionStatus.Validating && target.Id == snapshot.ModelVersionId)
            {
                target.CompleteValidationWithErrors();
                await _context.SaveChangesAsync(cancellationToken);
                await finish.CommitAsync(cancellationToken);
            }
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.ValidationStale,
                "The rollback target or Published pointer changed during validation.");
        }

        var now = RequireUtcNow();
        var run = SpaceValidationRun.CreateQueued(
            operation.TenantId, target.Id, target.ContentRevision, result.ContentHash, SpaceValidationRuleSet.Version,
            profile.AdapterId, profile.CapabilityHash, operation.RequestedBy, now, operation.JobId, operation.CorrelationId);
        run.Start(now);
        foreach (var candidate in result.Issues)
        {
            _context.Issues.Add(SpaceModelIssue.Create(
                run.TenantId, run.ModelVersionId, candidate.SourceId, run.JobId, candidate.Severity, candidate.Code,
                candidate.SourceRef, candidate.TargetLogicalId, candidate.MessageArgsJson, candidate.SuggestedActionCode,
                candidate.GenerationRunId, candidate.GenerationProposalId, run.Id, candidate.Category, candidate.FieldPath,
                candidate.EvidenceJson));
        }
        if (result.BlockingCount == 0)
        {
            run.Pass(result.BlockingCount, result.WarningCount, result.InfoCount, now);
            target.MarkReady(result.ContentHash, SpaceValidationRuleSet.Version, profile.CapabilityHash);
            operation.MarkValidationPassed(run.Id);
        }
        else
        {
            run.Block(result.BlockingCount, result.WarningCount, result.InfoCount, now);
            target.CompleteValidationWithErrors();
            operation.MarkValidationBlocked(run.Id);
        }
        _context.ValidationRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);
        await finish.CommitAsync(cancellationToken);
        return ValidationOutput(operation.Id, run, reused: false);
    }

    private async Task<SpaceJobStepOutput> ReadValidationAsync(SpaceHistoricalRepublish operation, CancellationToken cancellationToken)
    {
        var run = await _context.ValidationRuns.AsNoTracking()
            .SingleAsync(value => value.Id == operation.ValidationRunId, cancellationToken);
        return ValidationOutput(operation.Id, run, reused: true);
    }

    private static void EnsureActiveDraft(RepublishState state)
    {
        if (state.Model.ActiveDraftVersionId != state.Target.Id)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.VersionStateInvalid,
                "The historical clone no longer owns its draft reservation.");
    }

    private static void EnsureValidationPhase(RepublishState state)
    {
        EnsureActiveDraft(state);
        if (state.Operation.Status != SpaceHistoricalRepublishStatus.SnapshotCloned ||
            state.Target.Status is not (SpaceVersionStatus.Draft or SpaceVersionStatus.Validating))
        {
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.VersionStateInvalid,
                "The historical clone cannot enter validation from its current state.");
        }
        if (state.Model.CurrentPublishedVersionId != state.Operation.ExpectedPublishedVersionId)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.PublishedVersionChanged,
                "The Published version changed before rollback validation.");
    }

    private async Task<SpaceJobStepOutput> QueuePublishAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(lease, cancellationToken);
        var child = await FindChildAsync(state, cancellationToken);
        if (child is not null)
        {
            if (!state.Operation.PublishAttemptId.HasValue)
            {
                // Recover an older child commit where only the parent's link is missing.
                await using var write = await SpaceArtifactWriteTransaction.BeginMetadataAsync(
                    _context, lease, _writeAuthority, cancellationToken);
                state = await LoadStateAsync(lease, cancellationToken);
                child = await FindChildAsync(state, cancellationToken) ?? throw HistoricalChildConflict();
                if (!state.Operation.PublishAttemptId.HasValue)
                {
                    state.Operation.MarkPublishQueued(child.AttemptId);
                    await _context.SaveChangesAsync(cancellationToken);
                    await write.CommitAsync(cancellationToken);
                }
            }
            return QueueOutput(state.Operation, child);
        }

        EnsureActiveDraft(state);
        if (state.Operation.Status != SpaceHistoricalRepublishStatus.ValidationPassed ||
            !state.Operation.ValidationRunId.HasValue ||
            state.Target.Status != SpaceVersionStatus.Ready)
        {
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.ValidationBlocked,
                "The historical clone does not have a passed current validation.");
        }
        if (state.Model.CurrentPublishedVersionId != state.Operation.ExpectedPublishedVersionId)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.PublishedVersionChanged,
                "The Published version changed before the rollback publish was queued.");

        var validation = await _context.ValidationRuns.AsNoTracking()
            .SingleAsync(value => value.Id == state.Operation.ValidationRunId.Value, cancellationToken);
        if (validation.Status != SpaceValidationStatus.Passed)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.ValidationBlocked, "The rollback validation did not pass.");

        var preview = await _preview.GetPreviewAsync(
            state.Target.Id, floorLogicalId: null, objectType: null, action: null, impactCode: null,
            includeNoOp: false, limit: 1, cursor: null, cancellationToken);
        if (!preview.Publishable)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.ValidationBlocked,
                "The rollback publish plan contains blocking impact.");
        if (preview.ValidationWarningCount > 0)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.PublishWarningAcknowledgementRequired,
                "The rollback validation contains Warnings. Open the generated Ready version in publish preview and confirm the bound Warning set before publishing.");

        var operation = state.Operation;
        var publish = await _publish.StartHistoricalRepublishAsync(
            state.Target.Id,
            new CreateSpacePublishAttemptRequest(
                operation.ExpectedPublishedVersionId, validation.Id, preview.PlanHash, operation.ApprovalReference),
            ChildKey(operation.Id),
            new SpaceHistoricalRepublishPublishContext(
                operation.Id, operation.HistoricalVersionId, operation.Reason, operation.RequestedBy)
            { ParentLease = lease },
            cancellationToken);
        return Output(new { operation.Id, publishAttemptId = publish.Attempt.Id,
            publishJobId = publish.Attempt.JobId, publish.Attempt.PlanHash, publish.IdempotentReplay });
    }

    private async Task<HistoricalChild?> FindChildAsync(RepublishState state, CancellationToken cancellationToken)
    {
        var operation = state.Operation;
        var key = ChildKey(operation.Id);
        var attempt = await _context.PublishAttempts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.BusinessIdempotencyKey == key, cancellationToken);
        if (attempt is null)
            return operation.PublishAttemptId.HasValue ? throw HistoricalChildConflict() : null;

        if (operation.Status is not (SpaceHistoricalRepublishStatus.ValidationPassed or SpaceHistoricalRepublishStatus.PublishQueued) ||
            operation.PublishAttemptId is { } linked && linked != attempt.Id ||
            attempt.SiteId != operation.SiteId || attempt.TargetVersionId != operation.TargetVersionId ||
            attempt.BaseVersionId != operation.ExpectedPublishedVersionId || attempt.RequestedBy != operation.RequestedBy)
            throw HistoricalChildConflict();

        var plan = await _context.PublishPlans.AsNoTracking().SingleAsync(value => value.Id == attempt.PublishPlanId, cancellationToken);
        var audit = await _context.PublishAuditEvents.AsNoTracking()
            .SingleOrDefaultAsync(value => value.AttemptId == attempt.Id &&
                value.EventType == SpacePublishAuditEventType.HistoricalRepublishQueued, cancellationToken);
        if (audit is null || audit.ActorId != operation.RequestedBy ||
            plan.TargetVersionId != operation.TargetVersionId || plan.SiteId != operation.SiteId ||
            plan.BaseVersionId != operation.ExpectedPublishedVersionId || plan.ValidationRunId != operation.ValidationRunId)
            throw HistoricalChildConflict();

        var evidence = JsonSerializer.Deserialize<HistoricalChildEvidence>(audit.EvidenceJson, Json);
        var request = new CreateSpacePublishAttemptRequest(
            operation.ExpectedPublishedVersionId, plan.ValidationRunId, plan.PlanHash, operation.ApprovalReference);
        if (evidence != new HistoricalChildEvidence(operation.Id, operation.HistoricalVersionId,
                operation.ExpectedPublishedVersionId, operation.TargetVersionId, plan.ValidationRunId, operation.Reason) ||
            attempt.RequestHash != SpacePublishOrchestrator.RequestHash(operation.TargetVersionId, request))
            throw HistoricalChildConflict();

        var originalJob = await _context.Jobs.AsNoTracking().SingleAsync(value => value.Id == audit.JobId, cancellationToken);
        if (originalJob.JobType != SpaceJobType.Publish || originalJob.SubjectType != SpaceJobSubjectType.PublishAttempt ||
            originalJob.SubjectId != attempt.Id || originalJob.InputHash != plan.PlanHash ||
            originalJob.RequestedBy != operation.RequestedBy)
            throw HistoricalChildConflict();

        return new HistoricalChild(attempt.Id, originalJob.Id, plan.PlanHash);
    }

    private static string ChildKey(Guid id) => $"historical-republish:{id:N}";

    private static SpaceJobProcessingException HistoricalChildConflict() =>
        Processing(SpaceJobFailureKind.Security, SpaceErrorCodes.IdempotencyConflict,
            "The stored historical publish child does not match its original request.");

    private static SpaceJobStepOutput QueueOutput(SpaceHistoricalRepublish operation, HistoricalChild child) =>
        Output(new { operation.Id, publishAttemptId = child.AttemptId, publishJobId = child.JobId,
            child.PlanHash, idempotentReplay = true });

    private sealed record HistoricalChild(Guid AttemptId, Guid JobId, string PlanHash);

    private sealed record HistoricalChildEvidence(Guid RepublishId, Guid HistoricalVersionId, Guid ExpectedPublishedVersionId,
        Guid TargetVersionId, Guid ValidationRunId, string Reason);

    private async Task<RepublishState> LoadStateAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
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
            target.Purpose != SpaceModelVersionPurpose.Production ||
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
        bool reused)
    {
        if (run.Status == SpaceValidationStatus.Blocked)
            throw Processing(SpaceJobFailureKind.Input, SpaceErrorCodes.ValidationBlocked,
                "The historical snapshot is blocked by current validation rules.");
        if (run.Status != SpaceValidationStatus.Passed)
            throw Processing(SpaceJobFailureKind.Bug, SpaceErrorCodes.JobProcessorFailed,
                "The historical republish references a non-terminal validation.");
        return Output(
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
    }

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
