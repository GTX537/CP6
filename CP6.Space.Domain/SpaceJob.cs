using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceJob : SpaceTenantEntity
{
    private SpaceJob()
    {
    }

    public SpaceJobType JobType { get; private set; }
    public SpaceJobSubjectType SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public string BusinessKey { get; private set; } = string.Empty;
    public string InputHash { get; private set; } = string.Empty;
    public SpaceJobStatus Status { get; private set; }
    public short Priority { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }
    public DateTime? LockExpiresAtUtc { get; private set; }
    public Guid? ActiveAttemptId { get; private set; }
    public long LeaseRevision { get; private set; }
    public long ProgressDone { get; private set; }
    public long ProgressTotal { get; private set; }
    public string? ProgressStage { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public string? ResultSummaryJson { get; private set; }
    public SpaceJobFailureKind? LastFailureKind { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public Guid? RetryOfJobId { get; private set; }
    public DateTime? CancellationRequestedAtUtc { get; private set; }
    public Guid? CancellationRequestedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsTerminal =>
        Status is SpaceJobStatus.Succeeded
            or SpaceJobStatus.Failed
            or SpaceJobStatus.Cancelled
            or SpaceJobStatus.DeadLetter;

    public static SpaceJob CreateQueued(
        Guid tenantId,
        SpaceJobType jobType,
        SpaceJobSubjectType subjectType,
        Guid subjectId,
        string businessKey,
        string inputHash,
        short priority,
        int maxAttempts,
        Guid requestedBy,
        DateTime requestedAtUtc,
        Guid correlationId,
        string payloadJson = "{}",
        Guid? retryOfJobId = null)
    {
        if (subjectId == Guid.Empty)
            throw new ArgumentException("Job subject is required.", nameof(subjectId));
        if (priority is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(priority));
        if (maxAttempts is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (requestedBy == Guid.Empty)
            throw new ArgumentException("Job requester is required.", nameof(requestedBy));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));

        var job = new SpaceJob
        {
            JobType = jobType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            BusinessKey = RequireHash(businessKey, nameof(businessKey)),
            InputHash = RequireHash(inputHash, nameof(inputHash)),
            Status = SpaceJobStatus.Queued,
            Priority = priority,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = requestedAtUtc,
            RequestedBy = requestedBy,
            RequestedAtUtc = requestedAtUtc,
            CorrelationId = correlationId,
            PayloadJson = RequireJson(payloadJson, nameof(payloadJson)),
            RetryOfJobId = retryOfJobId,
        };
        job.SetTenant(tenantId);
        return job;
    }

    public SpaceJobAttempt Claim(
        string workerId,
        string processorVersion,
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (AttemptCount >= MaxAttempts)
            throw new SpaceJobStateException("The Job exhausted its attempt limit.");

        var canClaimQueued =
            Status == SpaceJobStatus.Queued &&
            NextAttemptAtUtc <= nowUtc;
        var canTakeOver =
            Status == SpaceJobStatus.Running &&
            LockExpiresAtUtc <= nowUtc;
        if (!canClaimQueued && !canTakeOver)
            throw new SpaceJobStateException("The Job is not claimable.");

        var attempt = SpaceJobAttempt.Start(
            TenantId,
            Id,
            checked(AttemptCount + 1),
            workerId,
            InputHash,
            processorVersion,
            nowUtc);

        Status = SpaceJobStatus.Running;
        AttemptCount = attempt.AttemptNo;
        LockedBy = attempt.WorkerId;
        LockedAtUtc = nowUtc;
        LockExpiresAtUtc = nowUtc.Add(leaseDuration);
        ActiveAttemptId = attempt.Id;
        StartedAtUtc ??= nowUtc;
        ProgressStage ??= "Starting";
        LeaseRevision = checked(LeaseRevision + 1);
        return attempt;
    }

    public void RenewLease(
        Guid attemptId,
        string workerId,
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        LockExpiresAtUtc = nowUtc.Add(leaseDuration);
        LeaseRevision = checked(LeaseRevision + 1);
    }

    public void ReportProgress(
        Guid attemptId,
        string workerId,
        long done,
        long total,
        string stage,
        DateTime nowUtc)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        if (done < 0 || total < 0 || (total != 0 && done > total))
            throw new ArgumentOutOfRangeException(nameof(done));
        if (done < ProgressDone)
            throw new SpaceJobStateException("Job progress cannot move backwards.");
        if (ProgressTotal != 0 && total != ProgressTotal)
            throw new SpaceJobStateException(
                "Job progress total cannot change after it is established.");

        ProgressDone = done;
        ProgressTotal = total;
        ProgressStage = RequireText(stage, 100, nameof(stage));
        LeaseRevision = checked(LeaseRevision + 1);
    }

    public void FenceCheckpoint(
        Guid attemptId,
        string workerId,
        DateTime nowUtc)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        LeaseRevision = checked(LeaseRevision + 1);
    }

    public void Complete(
        Guid attemptId,
        string workerId,
        DateTime nowUtc,
        string? resultSummaryJson = null)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        if (CancellationRequestedAtUtc.HasValue)
            throw new SpaceJobStateException(
                "A cancellation-requested Job must stop at a safe checkpoint.");

        Status = SpaceJobStatus.Succeeded;
        ProgressDone = Math.Max(ProgressDone, ProgressTotal);
        ProgressStage = "Completed";
        ResultSummaryJson = OptionalJson(resultSummaryJson, nameof(resultSummaryJson));
        FinishedAtUtc = nowUtc;
        ClearLease();
    }

    public void Fail(
        Guid attemptId,
        string workerId,
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError,
        SpaceJobRetryDecision decision,
        DateTime nowUtc)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.NextStatus is not (
                SpaceJobStatus.Queued or
                SpaceJobStatus.Failed or
                SpaceJobStatus.DeadLetter))
        {
            throw new ArgumentException("Invalid failure disposition.", nameof(decision));
        }

        LastFailureKind = failureKind;
        LastErrorCode = RequireText(errorCode, 100, nameof(errorCode));
        LastErrorSummary = RequireText(
            sanitizedError,
            1000,
            nameof(sanitizedError));
        Status = decision.NextStatus;
        NextAttemptAtUtc = decision.NextAttemptAtUtc ?? NextAttemptAtUtc;
        ProgressStage = decision.WillRetry ? "RetryScheduled" : "Failed";
        FinishedAtUtc = decision.WillRetry ? null : nowUtc;
        ClearLease();
    }

    public void RequestCancellation(Guid actorId, DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (actorId == Guid.Empty)
            throw new ArgumentException("Cancelling actor is required.", nameof(actorId));
        if (Status == SpaceJobStatus.Cancelled || CancellationRequestedAtUtc.HasValue)
            return;
        if (IsTerminal)
            throw new SpaceJobStateException("A terminal Job cannot be cancelled.");

        CancellationRequestedAtUtc = nowUtc;
        CancellationRequestedBy = actorId;
        if (Status == SpaceJobStatus.Queued)
        {
            Status = SpaceJobStatus.Cancelled;
            ProgressStage = "Cancelled";
            FinishedAtUtc = nowUtc;
            ClearLease();
        }
    }

    public void AcknowledgeCancellation(
        Guid attemptId,
        string workerId,
        DateTime nowUtc)
    {
        EnsureLease(attemptId, workerId, nowUtc);
        if (!CancellationRequestedAtUtc.HasValue)
            throw new SpaceJobStateException("Cancellation was not requested.");

        Status = SpaceJobStatus.Cancelled;
        ProgressStage = "Cancelled";
        FinishedAtUtc = nowUtc;
        ClearLease();
    }

    public void DeadLetterExpiredLease(DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != SpaceJobStatus.Running ||
            LockExpiresAtUtc > nowUtc ||
            AttemptCount < MaxAttempts)
        {
            throw new SpaceJobStateException(
                "Only an expired final lease can be dead-lettered.");
        }

        Status = SpaceJobStatus.DeadLetter;
        LastFailureKind ??= SpaceJobFailureKind.Transient;
        LastErrorCode = "SPACE_JOB_LEASE_LOST";
        LastErrorSummary = "The final worker lease expired.";
        ProgressStage = "DeadLetter";
        FinishedAtUtc = nowUtc;
        ClearLease();
    }

    public SpaceJob CreateExplicitRetry(
        string newBusinessKey,
        string newInputHash,
        Guid requestedBy,
        DateTime requestedAtUtc,
        Guid correlationId,
        string? payloadJson = null)
    {
        SpaceJobRetryPolicy.EnsureManualRetryAllowed(this, newBusinessKey);
        return CreateQueued(
            TenantId,
            JobType,
            SubjectType,
            SubjectId,
            newBusinessKey,
            newInputHash,
            Priority,
            MaxAttempts,
            requestedBy,
            requestedAtUtc,
            correlationId,
            payloadJson ?? PayloadJson,
            Id);
    }

    public void RequeueSameInput(DateTime requestedAtUtc)
    {
        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
        SpaceJobRetryPolicy.EnsureSameInputRetryAllowed(this);
        if (AttemptCount >= MaxAttempts)
            MaxAttempts = checked(AttemptCount + 1);

        Status = SpaceJobStatus.Queued;
        NextAttemptAtUtc = requestedAtUtc;
        ProgressStage = "ManualRetryScheduled";
        FinishedAtUtc = null;
        CancellationRequestedAtUtc = null;
        CancellationRequestedBy = null;
        ClearLease();
    }

    public void EnsureLease(
        Guid attemptId,
        string workerId,
        DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != SpaceJobStatus.Running ||
            ActiveAttemptId != attemptId ||
            !string.Equals(LockedBy, workerId, StringComparison.Ordinal) ||
            !LockExpiresAtUtc.HasValue ||
            LockExpiresAtUtc <= nowUtc)
        {
            throw new SpaceJobLeaseLostException(
                "The worker no longer owns an active Job lease.");
        }
    }

    private void ClearLease()
    {
        LockedBy = null;
        LockedAtUtc = null;
        LockExpiresAtUtc = null;
        ActiveAttemptId = null;
        LeaseRevision = checked(LeaseRevision + 1);
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", parameterName);
        return value.ToLowerInvariant();
    }

    private static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 65_536)
            throw new ArgumentException("Job JSON is required and is too large.", parameterName);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Job JSON is invalid.", parameterName, exception);
        }
        return value;
    }

    private static string? OptionalJson(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireJson(value, parameterName);

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
