using System.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Retries failed integration events and dead-letters exhausted attempts.
/// Space retries additionally restore their persisted execution identity and
/// append a fail-closed audit trail.
/// </summary>
public class IntegrationEventRetryWorker : BackgroundService
{
    private const string SpaceSourceModule = "SPACE";
    private const string SpaceWorkerActor =
        "space-worker:integration-event-retry";
    private const string SpaceRetryAction =
        "space.integration-event.retry";
    private const string SpaceResourceType = "IntegrationEvent";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationEventOptions _opts;
    private readonly SpaceObservabilityOptions _spaceObservability;
    private readonly ILogger<IntegrationEventRetryWorker> _logger;

    public IntegrationEventRetryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegrationEventOptions> options,
        ILogger<IntegrationEventRetryWorker> logger,
        IOptions<SpaceObservabilityOptions>? spaceObservability = null)
    {
        _scopeFactory = scopeFactory;
        _opts = options.Value;
        _opts.ValidateSpaceRetryLease();
        _spaceObservability =
            spaceObservability?.Value ??
            new SpaceObservabilityOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IntegrationEvent retry worker starting");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_opts.Enabled)
                    await ProcessOnceAsync(stoppingToken);

                await Task.Delay(
                    TimeSpan.FromSeconds(
                        _opts.PollIntervalSeconds),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation(
                "IntegrationEvent retry worker stopped");
        }
    }

    /// <summary>
    /// Processes one retry scan in an independent scope for each tenant.
    /// </summary>
    public async Task ProcessOnceAsync(
        CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(
            _scopeFactory,
            (sp, tenantId, currentToken) =>
                ProcessTenantOnceAsync(
                    sp,
                    tenantId,
                    currentToken),
            _logger,
            ct);
    }

    private async Task ProcessTenantOnceAsync(
        IServiceProvider sp,
        Guid tenantId,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<CP6Context>();
        var notifier =
            sp.GetRequiredService<IDeadLetterNotifier>();

        // Drain notifications that were committed by an earlier scan before
        // touching the retry queue. A poison due event (including an
        // uncaught cancellation-shaped adapter failure) must not starve an
        // already-durable Space dead letter.
        await ProcessPendingSpaceDeadLetterNotificationsAsync(
            tenantId,
            ct);

        var now = DateTime.UtcNow;
        var due = await db.IntegrationEvents
            .Where(e =>
                e.Status == IntegrationEventStatus.Failed &&
                (((e.SourceModule == SpaceSourceModule &&
                   e.Attempts >= _opts.MaxAttempts) &&
                  (e.NextRetryAt == null ||
                   e.NextRetryAt <= now)) ||
                 (e.NextRetryAt != null &&
                  e.NextRetryAt <= now &&
                  e.Attempts < _opts.MaxAttempts)))
            .OrderBy(e => e.NextRetryAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var evt in due)
        {
            if (string.Equals(
                    evt.SourceModule,
                    SpaceSourceModule,
                    StringComparison.Ordinal))
            {
                var eventId = evt.Id;
                db.Entry(evt).State = EntityState.Detached;
                await ProcessSpaceEventAsync(
                    tenantId,
                    eventId,
                    now,
                    ct);
                continue;
            }

            evt.Attempts++;
            var dispatcher =
                sp.GetRequiredService<
                    IIntegrationEventDispatcher>();
            await ProcessNonSpaceEventAsync(
                dispatcher,
                notifier,
                evt,
                now,
                ct);
        }

        await ProcessPendingSpaceDeadLetterNotificationsAsync(
            tenantId,
            ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessNonSpaceEventAsync(
        IIntegrationEventDispatcher dispatcher,
        IDeadLetterNotifier notifier,
        IntegrationEvent evt,
        DateTime now,
        CancellationToken ct)
    {
        try
        {
            var ok = await dispatcher.DispatchAsync(evt, ct);
            evt.Status = ok
                ? IntegrationEventStatus.Success
                : IntegrationEventStatus.Failed;
            evt.NextRetryAt = ok
                ? null
                : now.AddSeconds(
                    _opts.GetBackoffSeconds(evt.Attempts));
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            // Preserve the established non-Space state machine and storage
            // contract. Space is handled by the sanitized branch below.
            evt.LastError = ex.ToString();
            evt.NextRetryAt = now.AddSeconds(
                _opts.GetBackoffSeconds(evt.Attempts));
            evt.Status = IntegrationEventStatus.Failed;
        }

        if (evt.Attempts >= _opts.MaxAttempts &&
            evt.Status == IntegrationEventStatus.Failed)
        {
            evt.Status = IntegrationEventStatus.DeadLetter;
            evt.NextRetryAt = null;
            await notifier.NotifyAsync(evt, ct);
        }
    }

    private async Task<bool> TryBackfillSpaceIdentityAsync(
        CP6Context db,
        IntegrationEvent evt,
        CancellationToken ct)
    {
        var needsJobId =
            !evt.JobId.HasValue ||
            evt.JobId.Value == Guid.Empty;
        var needsPublishAttemptId =
            !evt.PublishAttemptId.HasValue ||
            evt.PublishAttemptId.Value == Guid.Empty;
        var needsCorrelationId =
            evt.CorrelationId == Guid.Empty;
        var needsOccurredAtUtc =
            !evt.OccurredAtUtc.HasValue;
        if (!needsJobId &&
            !needsPublishAttemptId &&
            !needsCorrelationId &&
            !needsOccurredAtUtc)
        {
            return true;
        }

        var originalJobId = evt.JobId;
        var originalPublishAttemptId =
            evt.PublishAttemptId;
        var originalCorrelationId = evt.CorrelationId;
        var originalOccurredAtUtc = evt.OccurredAtUtc;
        var originalAttempts = evt.Attempts;
        var originalNextRetryAt = evt.NextRetryAt;
        var originalRowVersion = evt.RowVersion;
        var jobId = needsJobId
            ? evt.Id == Guid.Empty
                ? Guid.NewGuid()
                : evt.Id
            : evt.JobId!.Value;
        var publishAttemptId = needsPublishAttemptId
            ? Guid.NewGuid()
            : evt.PublishAttemptId!.Value;
        var correlationId = needsCorrelationId
            ? Guid.NewGuid()
            : evt.CorrelationId;
        var occurredAtUtc = needsOccurredAtUtc
            ? SpaceIntegrationEventUtcNormalizer.Normalize(
                    evt.CreateDate,
                    evt.Id,
                    jobId,
                    jobId != Guid.Empty &&
                    jobId != evt.Id
                        ? null
                        : SpaceIntegrationEventUtcNormalizer
                            .ResolveRequiredTimeZone(
                                _spaceObservability
                                    .LegacyIntegrationEventTimeZoneId))
                .Utc
            : evt.OccurredAtUtc!.Value;

        try
        {
            if (db.Database.IsRelational())
            {
                var affected = await db.IntegrationEvents
                    .Where(e =>
                        e.Id == evt.Id &&
                        e.Status ==
                            IntegrationEventStatus.Failed &&
                        e.Attempts == originalAttempts &&
                        e.NextRetryAt ==
                            originalNextRetryAt &&
                        e.JobId == originalJobId &&
                        e.PublishAttemptId ==
                            originalPublishAttemptId &&
                        e.CorrelationId ==
                            originalCorrelationId &&
                        e.OccurredAtUtc ==
                            originalOccurredAtUtc &&
                        ((originalRowVersion == null &&
                          e.RowVersion == null) ||
                         (originalRowVersion != null &&
                          e.RowVersion ==
                              originalRowVersion)))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                e => e.JobId,
                                jobId)
                            .SetProperty(
                                e => e.PublishAttemptId,
                                publishAttemptId)
                            .SetProperty(
                                e => e.CorrelationId,
                                correlationId)
                            .SetProperty(
                                e => e.OccurredAtUtc,
                                occurredAtUtc),
                        ct);
                if (affected != 1)
                {
                    db.Entry(evt).State =
                        EntityState.Detached;
                    return false;
                }

                await db.Entry(evt).ReloadAsync(ct);
                return true;
            }

            evt.JobId = jobId;
            evt.PublishAttemptId = publishAttemptId;
            evt.CorrelationId = correlationId;
            evt.OccurredAtUtc = occurredAtUtc;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(evt).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<Guid?> TryClaimSpaceEventAsync(
        CP6Context db,
        Guid tenantId,
        IntegrationEvent evt,
        DateTime claimNow,
        bool stuckAtMax,
        CancellationToken ct)
    {
        var originalAttempts = evt.Attempts;
        var originalNextRetryAt = evt.NextRetryAt;
        var originalLeaseId = evt.RetryLeaseId;
        var originalRowVersion = evt.RowVersion;
        var retryLeaseId = Guid.NewGuid();
        var leaseUntil = DateTime.UtcNow.AddSeconds(
            _opts.SpaceRetryLeaseSeconds);

        try
        {
            if (db.Database.IsRelational())
            {
                var claim = db.IntegrationEvents
                    .Where(e =>
                        e.Id == evt.Id &&
                        e.TenantId == tenantId &&
                        e.Status ==
                            IntegrationEventStatus.Failed &&
                        e.Attempts == originalAttempts &&
                        e.NextRetryAt ==
                            originalNextRetryAt &&
                        e.RetryLeaseId ==
                            originalLeaseId &&
                        ((originalRowVersion == null &&
                          e.RowVersion == null) ||
                         (originalRowVersion != null &&
                          e.RowVersion ==
                              originalRowVersion)));
                claim = stuckAtMax
                    ? claim.Where(e =>
                        e.Attempts >= _opts.MaxAttempts &&
                        (e.NextRetryAt == null ||
                         e.NextRetryAt <= claimNow))
                    : claim.Where(e =>
                        e.Attempts < _opts.MaxAttempts &&
                        e.NextRetryAt != null &&
                        e.NextRetryAt <= claimNow);
                var affected = await claim
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                e => e.RetryLeaseId,
                                retryLeaseId)
                            .SetProperty(
                                e => e.NextRetryAt,
                                leaseUntil),
                        ct);
                if (affected != 1)
                {
                    db.Entry(evt).State =
                        EntityState.Detached;
                    return null;
                }

                await db.Entry(evt).ReloadAsync(ct);
                return retryLeaseId;
            }

            var eligible = stuckAtMax
                ? evt.Attempts >= _opts.MaxAttempts &&
                  (evt.NextRetryAt == null ||
                   evt.NextRetryAt <= claimNow)
                : evt.Attempts < _opts.MaxAttempts &&
                  evt.NextRetryAt != null &&
                  evt.NextRetryAt <= claimNow;
            if (!eligible)
            {
                db.Entry(evt).State = EntityState.Detached;
                return null;
            }

            evt.RetryLeaseId = retryLeaseId;
            evt.NextRetryAt = leaseUntil;
            await db.SaveChangesAsync(ct);
            return retryLeaseId;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(evt).State = EntityState.Detached;
            return null;
        }
    }

    private async Task ProcessSpaceEventAsync(
        Guid tenantId,
        Guid eventId,
        DateTime now,
        CancellationToken ct)
    {
        using var ledgerScope = _scopeFactory.CreateScope();
        var ledgerSp = ledgerScope.ServiceProvider;
        ledgerSp.GetRequiredService<ITenantContext>()
            .CurrentTenantId = tenantId;
        var db = ledgerSp.GetRequiredService<CP6Context>();
        var evt = await db.IntegrationEvents
            .SingleOrDefaultAsync(
                e => e.Id == eventId &&
                     e.TenantId == tenantId,
                ct);
        if (evt is null)
            return;

        var claimNow = DateTime.UtcNow;
        if (!await TryBackfillSpaceIdentityAsync(db, evt, ct))
            return;

        var stuckAtMax = evt.Attempts >= _opts.MaxAttempts;
        var retryLeaseId = await TryClaimSpaceEventAsync(
            db,
            tenantId,
            evt,
            claimNow,
            stuckAtMax,
            ct);
        if (!retryLeaseId.HasValue)
            return;

        var leaseId = retryLeaseId.Value;
        var heartbeat = StartSpaceLeaseHeartbeat(
            tenantId,
            evt.Id,
            leaseId);
        try
        {
            if (ct.IsCancellationRequested)
            {
                await TryReleaseOwnedSpaceLeaseAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    evt.Attempts,
                    "SPACE_RETRY_CANCELLED",
                    NextSpaceRecoveryAt(now, evt.Attempts),
                    CancellationToken.None);
                ct.ThrowIfCancellationRequested();
            }

            using var operationScope =
                _scopeFactory.CreateScope();
            var operationSp = operationScope.ServiceProvider;
            operationSp.GetRequiredService<ITenantContext>()
                .CurrentTenantId = tenantId;

            var parentTraceId = ActivityTraceId.CreateRandom();
            var parentSpanId = ActivitySpanId.CreateRandom();
            using var activity = new Activity(
                    "Space.IntegrationEventRetry")
                .SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(
                    parentTraceId,
                    parentSpanId,
                    ActivityTraceFlags.None)
                .Start();
            var context = SpaceExecutionContext.ForSystem(
                tenantId,
                SpaceWorkerActor,
                evt.CorrelationId,
                activity.TraceId.ToHexString(),
                evt.JobId,
                Guid.NewGuid(),
                evt.PublishAttemptId);
            var executionManager =
                operationSp.GetRequiredService<
                    ISpaceExecutionContextManager>();
            var auditWriter =
                operationSp.GetRequiredService<ISpaceAuditWriter>();
            var finalizer =
                operationSp.GetRequiredService<ISpaceRetryFinalizer>();
            using var execution = executionManager.Push(context);
            using var logScope = _logger.BeginScope(
                new Dictionary<string, object?>
                {
                    ["TenantId"] = context.TenantId,
                    ["ActorType"] = context.ActorType,
                    ["ActorId"] = context.ActorId,
                    ["CorrelationId"] = context.CorrelationId,
                    ["TraceId"] = context.TraceId,
                    ["JobId"] = context.JobId,
                    ["RunId"] = context.RunId,
                    ["PublishAttemptId"] =
                        context.PublishAttemptId,
                    ["AttemptNo"] = stuckAtMax
                        ? evt.Attempts
                        : evt.Attempts + 1,
                    ["RetryLeaseId"] = leaseId,
                });

            await db.Entry(evt).ReloadAsync(
                CancellationToken.None);
            if (evt.RetryLeaseId != leaseId)
                return;
            if (evt.RetryCompletionSucceeded.HasValue)
            {
                await TryFinalizeSpaceCompletionAsync(
                    finalizer,
                    tenantId,
                    evt,
                    leaseId);
                return;
            }

            if (stuckAtMax)
            {
                if (heartbeat.LostLease)
                    return;

                var deadResult =
                    await finalizer.TryFinalizeAsync(
                        new SpaceRetryFinalizationInput(
                            evt.Id,
                            tenantId,
                            leaseId,
                            evt.Attempts,
                            IntegrationEventStatus.DeadLetter,
                            "SPACE_RETRY_DEAD_LETTER",
                            null,
                            CreateSpaceRetryAudit(
                                evt,
                                evt.Attempts,
                                SpaceAuditOutcome.Failed,
                                "SPACE_RETRY_DEAD_LETTER",
                                IntegrationEventStatus.DeadLetter),
                            AuditId: leaseId),
                        CancellationToken.None);
                if (deadResult ==
                    SpaceRetryFinalizationResult.Committed)
                {
                    ApplySpaceTerminalState(
                        evt,
                        IntegrationEventStatus.DeadLetter,
                        "SPACE_RETRY_DEAD_LETTER",
                        null);
                    LogSpaceFailure(
                        evt,
                        "SPACE_RETRY_DEAD_LETTER");
                }
                else if (deadResult ==
                         SpaceRetryFinalizationResult.AuditUnavailable)
                {
                    await TryReleaseOwnedSpaceLeaseAsync(
                        db,
                        tenantId,
                        evt,
                        leaseId,
                        evt.Attempts,
                        "SPACE_AUDIT_UNAVAILABLE",
                        NextSpaceRecoveryAt(
                            DateTime.UtcNow,
                            evt.Attempts),
                        CancellationToken.None);
                    LogSpaceFailure(
                        evt,
                        "SPACE_AUDIT_UNAVAILABLE");
                }
                return;
            }

            var expectedAttempts = evt.Attempts;
            var attemptNo = expectedAttempts + 1;
            bool started;
            try
            {
                started = await TryAppendSpaceAuditAsync(
                    auditWriter,
                    CreateSpaceRetryAudit(
                        evt,
                        attemptNo,
                        SpaceAuditOutcome.Started,
                        status: "RETRYING"),
                    ct);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                await TryReleaseOwnedSpaceLeaseAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    expectedAttempts,
                    "SPACE_RETRY_CANCELLED",
                    NextSpaceRecoveryAt(
                        DateTime.UtcNow,
                        expectedAttempts),
                    CancellationToken.None);
                throw;
            }

            if (!started)
            {
                await TryReleaseOwnedSpaceLeaseAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    expectedAttempts,
                    "SPACE_AUDIT_UNAVAILABLE",
                    NextSpaceRecoveryAt(
                        DateTime.UtcNow,
                        expectedAttempts),
                    CancellationToken.None);
                LogSpaceFailure(
                    evt,
                    "SPACE_AUDIT_UNAVAILABLE");
                return;
            }

            if (ct.IsCancellationRequested)
            {
                await TryReleaseOwnedSpaceLeaseAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    expectedAttempts,
                    "SPACE_RETRY_CANCELLED",
                    NextSpaceRecoveryAt(
                        DateTime.UtcNow,
                        expectedAttempts),
                    CancellationToken.None);
                ct.ThrowIfCancellationRequested();
            }

            if (heartbeat.LostLease ||
                !await TryStartOwnedSpaceAttemptAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    expectedAttempts,
                    CancellationToken.None))
            {
                return;
            }
            evt.Attempts = attemptNo;

            var dispatcher =
                operationSp.GetRequiredService<
                    IIntegrationEventDispatcher>();
            var succeeded = false;
            var failureReason = "SPACE_ADAPTER_REJECTED";
            SpaceSafeError? safeError = null;
            string? safeStorageCode = null;
            OperationCanceledException? hostCancellation = null;
            try
            {
                succeeded = await dispatcher.DispatchAsync(evt, ct);
            }
            catch (OperationCanceledException ex)
                when (ct.IsCancellationRequested)
            {
                hostCancellation = ex;
            }
            catch (Exception ex)
            {
                safeError = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_ADAPTER_FAILURE");
                failureReason = safeError.ReasonCode;
                safeStorageCode =
                    SpaceErrorSanitizer.ToStorageCode(
                        ex,
                    "SPACE_ADAPTER_FAILURE");
            }

            await db.Entry(evt).ReloadAsync(
                CancellationToken.None);
            if (evt.RetryLeaseId != leaseId)
                return;
            if (evt.RetryCompletionSucceeded.HasValue)
            {
                await TryFinalizeSpaceCompletionAsync(
                    finalizer,
                    tenantId,
                    evt,
                    leaseId);
                return;
            }
            if (hostCancellation is not null)
                throw hostCancellation;
            if (heartbeat.LostLease)
                return;

            var completedAt = DateTime.UtcNow;
            var status = succeeded
                ? IntegrationEventStatus.Success
                : evt.Attempts >= _opts.MaxAttempts
                    ? IntegrationEventStatus.DeadLetter
                    : IntegrationEventStatus.Failed;
            var storageCode = succeeded
                ? null
                : safeError is null
                    ? failureReason
                    : safeStorageCode!;
            var nextRetryAt =
                status == IntegrationEventStatus.Failed
                    ? completedAt.AddSeconds(
                        _opts.GetBackoffSeconds(
                            evt.Attempts))
                    : (DateTime?)null;
            var auditReason = succeeded
                ? null
                : status ==
                  IntegrationEventStatus.DeadLetter
                    ? "SPACE_RETRY_DEAD_LETTER"
                    : failureReason;
            var finalResult =
                await finalizer.TryFinalizeAsync(
                    new SpaceRetryFinalizationInput(
                        evt.Id,
                        tenantId,
                        leaseId,
                        evt.Attempts,
                        status,
                        storageCode,
                        nextRetryAt,
                        CreateSpaceRetryAudit(
                            evt,
                            evt.Attempts,
                            succeeded
                                ? SpaceAuditOutcome.Succeeded
                                : SpaceAuditOutcome.Failed,
                            auditReason,
                            status,
                            safeError),
                        AuditId: leaseId),
                    CancellationToken.None);
            if (finalResult ==
                SpaceRetryFinalizationResult.LostLease)
            {
                return;
            }
            if (finalResult ==
                SpaceRetryFinalizationResult.AuditUnavailable)
            {
                await TryReleaseOwnedSpaceLeaseAsync(
                    db,
                    tenantId,
                    evt,
                    leaseId,
                    evt.Attempts,
                    "SPACE_OPERATION_OUTCOME_UNKNOWN",
                    NextSpaceRecoveryAt(
                        completedAt,
                        evt.Attempts),
                    CancellationToken.None);
                LogSpaceFailure(
                    evt,
                    "SPACE_OPERATION_OUTCOME_UNKNOWN");
                return;
            }

            ApplySpaceTerminalState(
                evt,
                status,
                storageCode,
                nextRetryAt);
            if (!succeeded)
                LogSpaceFailure(evt, failureReason);
        }
        finally
        {
            await heartbeat.StopAsync();
        }
    }

    private async Task ProcessPendingSpaceDeadLetterNotificationsAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        using var scanScope = _scopeFactory.CreateScope();
        var scanSp = scanScope.ServiceProvider;
        scanSp.GetRequiredService<ITenantContext>()
            .CurrentTenantId = tenantId;
        var scanDb = scanSp.GetRequiredService<CP6Context>();
        var claimNow = DateTime.UtcNow;
        var eventIds = await scanDb.IntegrationEvents
            .AsNoTracking()
            .Where(e =>
                e.TenantId == tenantId &&
                e.SourceModule == SpaceSourceModule &&
                e.Status == IntegrationEventStatus.DeadLetter &&
                e.DeadLetterNotifiedAtUtc == null &&
                (e.DeadLetterNotificationLeaseUntilUtc == null ||
                 e.DeadLetterNotificationLeaseUntilUtc <=
                    claimNow))
            .OrderBy(e => e.CreateDate)
            .Select(e => e.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var eventId in eventIds)
        {
            var notificationLeaseId = Guid.NewGuid();
            if (!await TryClaimSpaceDeadLetterNotificationAsync(
                    tenantId,
                    eventId,
                    notificationLeaseId,
                    claimNow,
                    ct))
            {
                continue;
            }

            var durable = false;
            try
            {
                using var notifyScope =
                    _scopeFactory.CreateScope();
                var notifySp = notifyScope.ServiceProvider;
                notifySp.GetRequiredService<ITenantContext>()
                    .CurrentTenantId = tenantId;
                var notifyDb =
                    notifySp.GetRequiredService<CP6Context>();
                var evt = await notifyDb.IntegrationEvents
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        e =>
                            e.Id == eventId &&
                            e.TenantId == tenantId &&
                            e.Status ==
                                IntegrationEventStatus.DeadLetter &&
                            e.DeadLetterNotifiedAtUtc == null &&
                            e.DeadLetterNotificationLeaseId ==
                                notificationLeaseId,
                        CancellationToken.None);
                if (evt is not null)
                {
                    durable = await notifySp
                        .GetRequiredService<
                            ISpaceDeadLetterNotifier>()
                        .TryNotifyDurablyAsync(
                            evt,
                            notificationLeaseId,
                            CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_DEAD_LETTER_NOTIFY_FAILED");
                _logger.LogWarning(
                    "Space dead-letter notification failed {ReasonCode} {ErrorType} {Fingerprint} {EventId}",
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint,
                    eventId);
            }

            if (durable)
            {
                await TryAcknowledgeSpaceDeadLetterNotificationAsync(
                    tenantId,
                    eventId,
                    notificationLeaseId,
                    CancellationToken.None);
            }
            else
            {
                await TryReleaseSpaceDeadLetterNotificationAsync(
                    tenantId,
                    eventId,
                    notificationLeaseId,
                    CancellationToken.None);
            }
        }
    }

    private async Task<bool> TryClaimSpaceDeadLetterNotificationAsync(
        Guid tenantId,
        Guid eventId,
        Guid notificationLeaseId,
        DateTime claimNow,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>()
            .CurrentTenantId = tenantId;
        var db = sp.GetRequiredService<CP6Context>();
        var leaseUntil = DateTime.UtcNow.AddSeconds(
            _opts.SpaceDeadLetterNotificationLeaseSeconds);
        var pending = db.IntegrationEvents.Where(e =>
            e.Id == eventId &&
            e.TenantId == tenantId &&
            e.SourceModule == SpaceSourceModule &&
            e.Status == IntegrationEventStatus.DeadLetter &&
            e.DeadLetterNotifiedAtUtc == null &&
            (e.DeadLetterNotificationLeaseUntilUtc == null ||
             e.DeadLetterNotificationLeaseUntilUtc <= claimNow));
        if (db.Database.IsRelational())
        {
            return await pending.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseId,
                        notificationLeaseId)
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseUntilUtc,
                        leaseUntil),
                ct) == 1;
        }

        var evt = await pending.SingleOrDefaultAsync(ct);
        if (evt is null)
            return false;
        evt.DeadLetterNotificationLeaseId =
            notificationLeaseId;
        evt.DeadLetterNotificationLeaseUntilUtc =
            leaseUntil;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool>
        TryAcknowledgeSpaceDeadLetterNotificationAsync(
            Guid tenantId,
            Guid eventId,
            Guid notificationLeaseId,
            CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>()
            .CurrentTenantId = tenantId;
        var db = sp.GetRequiredService<CP6Context>();
        var owned = db.IntegrationEvents.Where(e =>
            e.Id == eventId &&
            e.TenantId == tenantId &&
            e.SourceModule == SpaceSourceModule &&
            e.Status == IntegrationEventStatus.DeadLetter &&
            e.DeadLetterNotifiedAtUtc == null &&
            e.DeadLetterNotificationLeaseId ==
                notificationLeaseId);
        var notifiedAt = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            return await owned.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        e => e.DeadLetterNotifiedAtUtc,
                        notifiedAt)
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseId,
                        (Guid?)null)
                    .SetProperty(
                        e =>
                            e.DeadLetterNotificationLeaseUntilUtc,
                        (DateTime?)null),
                ct) == 1;
        }

        var evt = await owned.SingleOrDefaultAsync(ct);
        if (evt is null)
            return false;
        evt.DeadLetterNotifiedAtUtc = notifiedAt;
        evt.DeadLetterNotificationLeaseId = null;
        evt.DeadLetterNotificationLeaseUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool>
        TryReleaseSpaceDeadLetterNotificationAsync(
            Guid tenantId,
            Guid eventId,
            Guid notificationLeaseId,
            CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>()
            .CurrentTenantId = tenantId;
        var db = sp.GetRequiredService<CP6Context>();
        var owned = db.IntegrationEvents.Where(e =>
            e.Id == eventId &&
            e.TenantId == tenantId &&
            e.SourceModule == SpaceSourceModule &&
            e.Status == IntegrationEventStatus.DeadLetter &&
            e.DeadLetterNotifiedAtUtc == null &&
            e.DeadLetterNotificationLeaseId ==
                notificationLeaseId);
        if (db.Database.IsRelational())
        {
            return await owned.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseId,
                        (Guid?)null)
                    .SetProperty(
                        e =>
                            e.DeadLetterNotificationLeaseUntilUtc,
                        (DateTime?)null),
                ct) == 1;
        }

        var evt = await owned.SingleOrDefaultAsync(ct);
        if (evt is null)
            return false;
        evt.DeadLetterNotificationLeaseId = null;
        evt.DeadLetterNotificationLeaseUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<SpaceRetryFinalizationResult>
        TryFinalizeSpaceCompletionAsync(
            ISpaceRetryFinalizer finalizer,
            Guid tenantId,
            IntegrationEvent evt,
            Guid currentLeaseId)
    {
        if (!evt.RetryCompletionSucceeded.HasValue ||
            !evt.RetryCompletionLeaseId.HasValue)
        {
            return SpaceRetryFinalizationResult.LostLease;
        }

        var completedAt = DateTime.UtcNow;
        var succeeded = evt.RetryCompletionSucceeded.Value;
        var status = succeeded
            ? IntegrationEventStatus.Success
            : evt.Attempts >= _opts.MaxAttempts
                ? IntegrationEventStatus.DeadLetter
                : IntegrationEventStatus.Failed;
        var storageCode = succeeded
            ? null
            : "SPACE_ADAPTER_REJECTED";
        var nextRetryAt =
            status == IntegrationEventStatus.Failed
                ? completedAt.AddSeconds(
                    _opts.GetBackoffSeconds(evt.Attempts))
                : (DateTime?)null;
        var reasonCode = succeeded
            ? null
            : status == IntegrationEventStatus.DeadLetter
                ? "SPACE_RETRY_DEAD_LETTER"
                : "SPACE_ADAPTER_REJECTED";
        var completionLeaseId =
            evt.RetryCompletionLeaseId.Value;
        var result = await finalizer.TryFinalizeAsync(
            new SpaceRetryFinalizationInput(
                evt.Id,
                tenantId,
                currentLeaseId,
                evt.Attempts,
                status,
                storageCode,
                nextRetryAt,
                CreateSpaceRetryAudit(
                    evt,
                    evt.Attempts,
                    succeeded
                        ? SpaceAuditOutcome.Succeeded
                        : SpaceAuditOutcome.Failed,
                    reasonCode,
                    status),
                AuditId: completionLeaseId,
                ExpectedCompletionLeaseId:
                    completionLeaseId,
                ExpectedCompletionSucceeded:
                    succeeded),
            CancellationToken.None);
        if (result == SpaceRetryFinalizationResult.Committed)
        {
            ApplySpaceTerminalState(
                evt,
                status,
                storageCode,
                nextRetryAt);
            if (!succeeded)
                LogSpaceFailure(evt, reasonCode!);
        }
        return result;
    }

    private async Task<bool> TryStartOwnedSpaceAttemptAsync(
        CP6Context db,
        Guid tenantId,
        IntegrationEvent evt,
        Guid leaseId,
        int expectedAttempts,
        CancellationToken ct)
    {
        var startNow = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            var affected = await db.IntegrationEvents
                .Where(e =>
                    e.Id == evt.Id &&
                    e.TenantId == tenantId &&
                    e.Status ==
                        IntegrationEventStatus.Failed &&
                    e.RetryLeaseId == leaseId &&
                    e.Attempts == expectedAttempts &&
                    e.NextRetryAt > startNow &&
                    e.RetryCompletionLeaseId == null &&
                    e.RetryCompletionSucceeded == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        e => e.Attempts,
                        expectedAttempts + 1),
                    ct);
            return affected == 1;
        }

        await db.Entry(evt).ReloadAsync(ct);
        if (evt.TenantId != tenantId ||
            evt.Status != IntegrationEventStatus.Failed ||
            evt.RetryLeaseId != leaseId ||
            evt.Attempts != expectedAttempts ||
            evt.NextRetryAt <= startNow ||
            evt.RetryCompletionLeaseId.HasValue ||
            evt.RetryCompletionSucceeded.HasValue)
        {
            return false;
        }
        evt.Attempts = expectedAttempts + 1;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> TryReleaseOwnedSpaceLeaseAsync(
        CP6Context db,
        Guid tenantId,
        IntegrationEvent evt,
        Guid leaseId,
        int expectedAttempts,
        string safeErrorCode,
        DateTime nextRetryAt,
        CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            var affected = await db.IntegrationEvents
                .Where(e =>
                    e.Id == evt.Id &&
                    e.TenantId == tenantId &&
                    e.Status ==
                        IntegrationEventStatus.Failed &&
                    e.RetryLeaseId == leaseId &&
                    e.Attempts == expectedAttempts &&
                    e.RetryCompletionLeaseId == null &&
                    e.RetryCompletionSucceeded == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            e => e.RetryLeaseId,
                            (Guid?)null)
                        .SetProperty(
                            e => e.LastError,
                            safeErrorCode)
                        .SetProperty(
                            e => e.NextRetryAt,
                            nextRetryAt),
                    ct);
            return affected == 1;
        }

        await db.Entry(evt).ReloadAsync(ct);
        if (evt.TenantId != tenantId ||
            evt.Status != IntegrationEventStatus.Failed ||
            evt.RetryLeaseId != leaseId ||
            evt.Attempts != expectedAttempts ||
            evt.RetryCompletionLeaseId.HasValue ||
            evt.RetryCompletionSucceeded.HasValue)
        {
            return false;
        }
        evt.RetryLeaseId = null;
        evt.LastError = safeErrorCode;
        evt.NextRetryAt = nextRetryAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private SpaceLeaseHeartbeat StartSpaceLeaseHeartbeat(
        Guid tenantId,
        Guid eventId,
        Guid leaseId)
    {
        var heartbeat = new SpaceLeaseHeartbeat();
        heartbeat.Completion = RunSpaceLeaseHeartbeatAsync(
            heartbeat,
            tenantId,
            eventId,
            leaseId,
            heartbeat.Token);
        return heartbeat;
    }

    private async Task RunSpaceLeaseHeartbeatAsync(
        SpaceLeaseHeartbeat heartbeat,
        Guid tenantId,
        Guid eventId,
        Guid leaseId,
        CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            _opts.SpaceRetryHeartbeatSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (ct.IsCancellationRequested)
                    return;

                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                sp.GetRequiredService<ITenantContext>()
                    .CurrentTenantId = tenantId;
                var db = sp.GetRequiredService<CP6Context>();
                var leaseUntil = DateTime.UtcNow.AddSeconds(
                    _opts.SpaceRetryLeaseSeconds);
                int affected;
                if (db.Database.IsRelational())
                {
                    affected = await db.IntegrationEvents
                        .Where(e =>
                            e.Id == eventId &&
                            e.TenantId == tenantId &&
                            e.Status ==
                                IntegrationEventStatus.Failed &&
                            e.RetryLeaseId == leaseId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                e => e.NextRetryAt,
                                leaseUntil),
                            ct);
                }
                else
                {
                    var owned = await db.IntegrationEvents
                        .SingleOrDefaultAsync(
                            e => e.Id == eventId &&
                                 e.TenantId == tenantId &&
                                 e.Status ==
                                     IntegrationEventStatus.Failed &&
                                 e.RetryLeaseId == leaseId,
                            ct);
                    if (owned is null)
                    {
                        affected = 0;
                    }
                    else
                    {
                        owned.NextRetryAt = leaseUntil;
                        await db.SaveChangesAsync(ct);
                        affected = 1;
                    }
                }

                if (affected == 0)
                {
                    heartbeat.MarkLeaseLost();
                    return;
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_RETRY_HEARTBEAT_FAILED");
                _logger.LogWarning(
                    "Space retry heartbeat failed {ReasonCode} {ErrorType} {Fingerprint} {EventId} {RetryLeaseId}",
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint,
                    eventId,
                    leaseId);
            }
        }
    }

    private static SpaceAuditEventInput CreateSpaceRetryAudit(
        IntegrationEvent evt,
        int attemptNo,
        string outcome,
        string? reasonCode = null,
        string? status = null,
        SpaceSafeError? safeError = null)
        => new(
            SpaceRetryAction,
            SpaceResourceType,
            evt.Id.ToString(),
            outcome,
            ReasonCode: reasonCode,
            Evidence: new SpaceAuditEvidence(
                Status: status,
                ExceptionType: safeError?.ExceptionType,
                ErrorFingerprint: safeError?.Fingerprint),
            AttemptNo: attemptNo,
            ClientType: "Worker");

    private static void ApplySpaceTerminalState(
        IntegrationEvent evt,
        string status,
        string? safeErrorCode,
        DateTime? nextRetryAt)
    {
        evt.Status = status;
        evt.LastError = safeErrorCode;
        evt.NextRetryAt = nextRetryAt;
        evt.RetryLeaseId = null;
        evt.RetryCompletionLeaseId = null;
        evt.RetryCompletionSucceeded = null;
        evt.DeadLetterNotifiedAtUtc = null;
        evt.DeadLetterNotificationLeaseId = null;
        evt.DeadLetterNotificationLeaseUntilUtc = null;
    }

    private async Task<bool> TryAppendSpaceAuditAsync(
        ISpaceAuditWriter writer,
        SpaceAuditEventInput input,
        CancellationToken ct)
    {
        try
        {
            return await writer.TryAppendAsync(input, ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safe = SpaceErrorSanitizer.Classify(
                ex,
                "SPACE_AUDIT_WRITE_FAILED");
            _logger.LogError(
                "Space retry audit failed {ReasonCode} {ErrorType} {Fingerprint} {EventId}",
                safe.ReasonCode,
                safe.ExceptionType,
                safe.Fingerprint,
                input.ResourceId);
            return false;
        }
    }

    private DateTime NextSpaceRecoveryAt(
        DateTime now,
        int attempts)
        => now.AddSeconds(Math.Max(
            1,
            _opts.GetBackoffSeconds(attempts)));

    private void LogSpaceFailure(
        IntegrationEvent evt,
        string reasonCode)
    {
        _logger.LogWarning(
            "Space integration retry failed {EventId} {CorrelationId} {ReasonCode} {AttemptNo}",
            evt.Id,
            evt.CorrelationId,
            reasonCode,
            evt.Attempts);
    }

    private sealed class SpaceLeaseHeartbeat
    {
        private readonly CancellationTokenSource _stop = new();
        private int _lostLease;

        public CancellationToken Token => _stop.Token;

        public bool LostLease =>
            Volatile.Read(ref _lostLease) != 0;

        public Task Completion { get; set; } =
            Task.CompletedTask;

        public void MarkLeaseLost() =>
            Interlocked.Exchange(ref _lostLease, 1);

        public async Task StopAsync()
        {
            _stop.Cancel();
            try
            {
                await Completion;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }
}
