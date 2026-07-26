using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceRetryFinalizer : ISpaceRetryFinalizer
{
    private readonly ISpaceAuditDbContextFactory _factory;
    private readonly ISpaceExecutionContextAccessor _execution;
    private readonly ILogger<SpaceRetryFinalizer> _logger;

    public SpaceRetryFinalizer(
        ISpaceAuditDbContextFactory factory,
        ISpaceExecutionContextAccessor execution,
        ILogger<SpaceRetryFinalizer> logger)
    {
        _factory = factory;
        _execution = execution;
        _logger = logger;
    }

    public async Task<SpaceRetryFinalizationResult> TryFinalizeAsync(
        SpaceRetryFinalizationInput input,
        CancellationToken ct = default)
    {
        try
        {
            Validate(input);
            var context = _execution.RequireOutcomeCurrent();
            if (context.TenantId != input.TenantId)
                throw new InvalidOperationException(
                    "SPACE_RETRY_FINALIZER_TENANT_MISMATCH");

            await using var strategyDb = _factory.CreateDbContext();
            var strategy = strategyDb.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var db = _factory.CreateDbContext();
                if (db.CurrentTenantId != context.TenantId)
                    throw new InvalidOperationException(
                        "SPACE_AUDIT_TENANT_CONTEXT_MISMATCH");
                if (await VerifyCommittedAsync(
                        db,
                        input,
                        ct))
                {
                    return SpaceRetryFinalizationResult.Committed;
                }
                await using var transaction = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(ct)
                    : null;

                db.SpaceAuditEvents.Add(
                    SpaceAuditWriter.Materialize(
                        input.Audit,
                        context,
                        DateTime.UtcNow,
                        auditId: input.AuditId));
                // Deliberately flush the audit first. The fenced event update
                // follows in the same transaction; a lost fence must roll the
                // inserted audit back.
                await db.SaveChangesAsync(ct);

                var affected = await UpdateOwnedEventAsync(
                    db,
                    input,
                    ct);
                if (affected != 1)
                    throw new SpaceRetryLeaseLostException();

                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return SpaceRetryFinalizationResult.Committed;
            });
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SpaceRetryLeaseLostException)
        {
            return await TryVerifyCommittedAsync(input)
                ? SpaceRetryFinalizationResult.Committed
                : SpaceRetryFinalizationResult.LostLease;
        }
        catch (Exception ex)
        {
            if (await TryVerifyCommittedAsync(input))
                return SpaceRetryFinalizationResult.Committed;
            var safe = SpaceErrorSanitizer.Classify(
                ex,
                "SPACE_RETRY_FINALIZE_FAILED");
            _logger.LogError(
                "Space retry finalization failed {ReasonCode} {ErrorType} {Fingerprint} {EventId} {RetryLeaseId}",
                safe.ReasonCode,
                safe.ExceptionType,
                safe.Fingerprint,
                input.EventId,
                input.RetryLeaseId);
            return SpaceRetryFinalizationResult.AuditUnavailable;
        }
    }

    private static async Task<int> UpdateOwnedEventAsync(
        CP6.Core.EFDbContext.CP6Context db,
        SpaceRetryFinalizationInput input,
        CancellationToken ct)
    {
        var owned = db.IntegrationEvents.Where(e =>
            e.Id == input.EventId &&
            e.TenantId == input.TenantId &&
            e.Status == IntegrationEventStatus.Failed &&
            e.RetryLeaseId == input.RetryLeaseId &&
            e.Attempts == input.ExpectedAttempts &&
            e.RetryCompletionLeaseId ==
                input.ExpectedCompletionLeaseId &&
            e.RetryCompletionSucceeded ==
                input.ExpectedCompletionSucceeded);

        if (db.Database.IsRelational())
        {
            return await owned.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.Status, input.Status)
                    .SetProperty(e => e.LastError, input.LastError)
                    .SetProperty(e => e.NextRetryAt, input.NextRetryAt)
                    .SetProperty(
                        e => e.RetryLeaseId,
                        (Guid?)null)
                    .SetProperty(
                        e => e.RetryCompletionLeaseId,
                        (Guid?)null)
                    .SetProperty(
                        e => e.RetryCompletionSucceeded,
                        (bool?)null)
                    .SetProperty(
                        e => e.DeadLetterNotifiedAtUtc,
                        (DateTime?)null)
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseId,
                        (Guid?)null)
                    .SetProperty(
                        e => e.DeadLetterNotificationLeaseUntilUtc,
                        (DateTime?)null),
                ct);
        }

        var evt = await owned.SingleOrDefaultAsync(ct);
        if (evt is null)
            return 0;
        evt.Status = input.Status;
        evt.LastError = input.LastError;
        evt.NextRetryAt = input.NextRetryAt;
        evt.RetryLeaseId = null;
        evt.RetryCompletionLeaseId = null;
        evt.RetryCompletionSucceeded = null;
        evt.DeadLetterNotifiedAtUtc = null;
        evt.DeadLetterNotificationLeaseId = null;
        evt.DeadLetterNotificationLeaseUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return 1;
    }

    private async Task<bool> TryVerifyCommittedAsync(
        SpaceRetryFinalizationInput input)
    {
        try
        {
            await using var db = _factory.CreateDbContext();
            return await VerifyCommittedAsync(
                db,
                input,
                CancellationToken.None);
        }
        catch (Exception verifyError)
        {
            var safe = SpaceErrorSanitizer.Classify(
                verifyError,
                "SPACE_RETRY_FINALIZE_VERIFY_FAILED");
            _logger.LogWarning(
                "Space retry finalization verification failed {ReasonCode} {ErrorType} {Fingerprint} {EventId} {AuditId}",
                safe.ReasonCode,
                safe.ExceptionType,
                safe.Fingerprint,
                input.EventId,
                input.AuditId);
            return false;
        }
    }

    private static async Task<bool> VerifyCommittedAsync(
        CP6.Core.EFDbContext.CP6Context db,
        SpaceRetryFinalizationInput input,
        CancellationToken ct)
    {
        var auditCommitted = await db.SpaceAuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(a =>
                a.Id == input.AuditId &&
                a.TenantId == input.TenantId &&
                a.Outcome == input.Audit.Outcome &&
                a.AttemptNo == input.ExpectedAttempts,
                ct);
        if (!auditCommitted)
            return false;

        return await db.IntegrationEvents
            .IgnoreQueryFilters()
            .AnyAsync(e =>
                e.Id == input.EventId &&
                e.TenantId == input.TenantId &&
                e.Status == input.Status &&
                e.Attempts == input.ExpectedAttempts &&
                e.RetryLeaseId == null &&
                e.RetryCompletionLeaseId == null &&
                e.RetryCompletionSucceeded == null &&
                e.LastError == input.LastError &&
                e.NextRetryAt == input.NextRetryAt,
                ct);
    }

    private static void Validate(
        SpaceRetryFinalizationInput input)
    {
        if (input.EventId == Guid.Empty ||
            input.TenantId == Guid.Empty ||
            input.RetryLeaseId == Guid.Empty ||
            input.AuditId == Guid.Empty ||
            input.ExpectedAttempts < 0)
        {
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_INPUT_INVALID");
        }

        var success =
            input.Status == IntegrationEventStatus.Success;
        var failed =
            input.Status == IntegrationEventStatus.Failed;
        var dead =
            input.Status == IntegrationEventStatus.DeadLetter;
        if (!success && !failed && !dead)
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_STATUS_INVALID");
        if ((success &&
             input.Audit.Outcome !=
                 SpaceAuditOutcome.Succeeded) ||
            (!success &&
             input.Audit.Outcome !=
                 SpaceAuditOutcome.Failed))
        {
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_OUTCOME_MISMATCH");
        }
        if (failed != input.NextRetryAt.HasValue)
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_RETRY_INVALID");
        if (input.ExpectedCompletionLeaseId.HasValue !=
            input.ExpectedCompletionSucceeded.HasValue)
        {
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_COMPLETION_INVALID");
        }
        if (input.Audit.AttemptNo != input.ExpectedAttempts)
            throw new ArgumentException(
                "SPACE_RETRY_FINALIZER_AUDIT_ATTEMPT_MISMATCH");
    }

    private sealed class SpaceRetryLeaseLostException :
        Exception
    {
    }
}
