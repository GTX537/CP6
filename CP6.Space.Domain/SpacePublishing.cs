using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpacePublishPlan : SpaceTenantEntity
{
    private SpacePublishPlan() { }

    public Guid SiteId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public Guid? BaseVersionId { get; private set; }
    public Guid ValidationRunId { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string AdapterId { get; private set; } = string.Empty;
    public string CapabilityHash { get; private set; } = string.Empty;
    public string PlanHash { get; private set; } = string.Empty;
    public int ItemCount { get; private set; }
    public string PlanJson { get; private set; } = string.Empty;

    public static SpacePublishPlan Create(
        Guid tenantId,
        Guid siteId,
        Guid targetVersionId,
        Guid? baseVersionId,
        Guid validationRunId,
        string contentHash,
        string adapterId,
        string capabilityHash,
        string planHash,
        int itemCount,
        string planJson)
    {
        RequireIdentity(siteId, nameof(siteId));
        RequireIdentity(targetVersionId, nameof(targetVersionId));
        if (baseVersionId == Guid.Empty)
            throw new ArgumentException("Base version identity cannot be empty.", nameof(baseVersionId));
        RequireIdentity(validationRunId, nameof(validationRunId));
        if (itemCount < 0)
            throw new ArgumentOutOfRangeException(nameof(itemCount));

        var plan = new SpacePublishPlan
        {
            SiteId = siteId,
            TargetVersionId = targetVersionId,
            BaseVersionId = baseVersionId,
            ValidationRunId = validationRunId,
            ContentHash = RequireHash(contentHash, nameof(contentHash)),
            AdapterId = RequireText(adapterId, 100, nameof(adapterId)),
            CapabilityHash = RequireHash(capabilityHash, nameof(capabilityHash)),
            PlanHash = RequireHash(planHash, nameof(planHash)),
            ItemCount = itemCount,
            PlanJson = RequireText(planJson, 4_000_000, nameof(planJson)),
        };
        plan.SetTenant(tenantId);
        return plan;
    }

    internal static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty identity is required.", parameterName);
    }

    internal static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hexadecimal hash is required.", parameterName);
        return value.ToLowerInvariant();
    }

    internal static string RequireText(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
            throw new ArgumentException($"A value between 1 and {maximumLength} characters is required.", parameterName);
        return normalized;
    }
}

public sealed class SpacePublishAttempt : SpaceTenantEntity
{
    private SpacePublishAttempt() { }

    public Guid SiteId { get; private set; }
    public Guid PublishPlanId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public Guid? BaseVersionId { get; private set; }
    public string AdapterId { get; private set; } = string.Empty;
    public SpacePublishAttemptStatus Status { get; private set; }
    public SpacePublishStep CurrentStep { get; private set; }
    public string BusinessIdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public bool OwnsPublishSlot { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public Guid RequestedBy { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public string? ApprovalReference { get; private set; }
    public DateTime? WmsCommittedAtUtc { get; private set; }
    public DateTime? RuntimeActivatedAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? Summary { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid? JobId { get; private set; }
    public string RequestJson { get; private set; } = string.Empty;
    public DateTime QueuedAtUtc { get; private set; }
    public int ManualRetryCount { get; private set; }
    public DateTime? LastRetriedAtUtc { get; private set; }
    public Guid? LastRetriedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsTerminal =>
        Status is SpacePublishAttemptStatus.Completed or SpacePublishAttemptStatus.FailedNoEffect;

    public static SpacePublishAttempt Create(
        Guid tenantId,
        Guid siteId,
        Guid publishPlanId,
        Guid targetVersionId,
        Guid? baseVersionId,
        string adapterId,
        string businessIdempotencyKey,
        string requestHash,
        Guid requestedBy,
        Guid? approvedBy,
        string? approvalReference,
        string requestJson,
        DateTime startedAtUtc,
        Guid correlationId)
    {
        SpacePublishPlan.RequireIdentity(siteId, nameof(siteId));
        SpacePublishPlan.RequireIdentity(publishPlanId, nameof(publishPlanId));
        SpacePublishPlan.RequireIdentity(targetVersionId, nameof(targetVersionId));
        if (baseVersionId == Guid.Empty)
            throw new ArgumentException("Base version identity cannot be empty.", nameof(baseVersionId));
        SpacePublishPlan.RequireIdentity(requestedBy, nameof(requestedBy));
        SpacePublishPlan.RequireIdentity(correlationId, nameof(correlationId));
        RequireUtc(startedAtUtc, nameof(startedAtUtc));

        var attempt = new SpacePublishAttempt
        {
            SiteId = siteId,
            PublishPlanId = publishPlanId,
            TargetVersionId = targetVersionId,
            BaseVersionId = baseVersionId,
            AdapterId = SpacePublishPlan.RequireText(adapterId, 100, nameof(adapterId)),
            Status = SpacePublishAttemptStatus.Requested,
            CurrentStep = SpacePublishStep.Requested,
            BusinessIdempotencyKey = SpacePublishPlan.RequireText(
                businessIdempotencyKey, 200, nameof(businessIdempotencyKey)),
            RequestHash = SpacePublishPlan.RequireHash(requestHash, nameof(requestHash)),
            OwnsPublishSlot = true,
            StartedAtUtc = startedAtUtc,
            RequestedBy = requestedBy,
            ApprovedBy = approvedBy,
            ApprovalReference = OptionalText(approvalReference, 500, nameof(approvalReference)),
            CorrelationId = correlationId,
            RequestJson = RequireJson(requestJson, nameof(requestJson)),
            QueuedAtUtc = startedAtUtc,
        };
        attempt.SetTenant(tenantId);
        return attempt;
    }

    public void BindInitialJob(Guid jobId)
    {
        SpacePublishPlan.RequireIdentity(jobId, nameof(jobId));
        if (JobId.HasValue)
            throw new SpaceVersionStateException("The publish attempt already has a Job.");
        JobId = jobId;
    }

    public void BeginPreflight()
    {
        if (Status is not (
                SpacePublishAttemptStatus.Requested or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention or
                SpacePublishAttemptStatus.ReconciliationRequired))
        {
            throw new SpaceVersionStateException(
                "Only a queued or retryable publish attempt can begin preflight.");
        }
        Status = SpacePublishAttemptStatus.Preflighting;
        CurrentStep = SpacePublishStep.Preflight;
    }

    public void BeginApplyingWms()
    {
        if (Status is not (
                SpacePublishAttemptStatus.Preflighting or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention or
                SpacePublishAttemptStatus.ReconciliationRequired))
        {
            throw new SpaceVersionStateException(
                "The publish attempt cannot enter WMS apply from its current state.");
        }
        Status = SpacePublishAttemptStatus.ApplyingWms;
        CurrentStep = SpacePublishStep.ApplyWms;
        LastErrorCode = null;
        Summary = null;
    }

    public void BeginVerifyingWms(DateTime nowUtc)
    {
        if (Status is not (
                SpacePublishAttemptStatus.ApplyingWms or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention or
                SpacePublishAttemptStatus.ReconciliationRequired))
        {
            throw new SpaceVersionStateException(
                "The publish attempt cannot verify WMS from its current state.");
        }
        RequireUtc(nowUtc, nameof(nowUtc));
        Status = SpacePublishAttemptStatus.VerifyingWms;
        CurrentStep = SpacePublishStep.VerifyWms;
        WmsCommittedAtUtc = nowUtc;
    }

    public void BeginActivatingRuntime()
    {
        if (Status is not (
                SpacePublishAttemptStatus.VerifyingWms or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention or
                SpacePublishAttemptStatus.ReconciliationRequired))
        {
            throw new SpaceVersionStateException(
                "The publish attempt cannot activate runtime from its current state.");
        }
        Status = SpacePublishAttemptStatus.ActivatingRuntime;
        CurrentStep = SpacePublishStep.ActivateRuntime;
    }

    public void Complete(DateTime nowUtc, string summary)
    {
        RequireStatus(SpacePublishAttemptStatus.ActivatingRuntime);
        RequireUtc(nowUtc, nameof(nowUtc));
        RuntimeActivatedAtUtc = nowUtc;
        FinishedAtUtc = nowUtc;
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        Status = SpacePublishAttemptStatus.Completed;
        CurrentStep = SpacePublishStep.Complete;
        OwnsPublishSlot = false;
        LastErrorCode = null;
    }

    public void FailNoEffect(string errorCode, string summary, DateTime nowUtc)
    {
        if (Status is not (
                SpacePublishAttemptStatus.Preflighting or
                SpacePublishAttemptStatus.ApplyingWms or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention))
            throw new SpaceVersionStateException("Only a preflight or zero-effect WMS failure can fail safely.");
        RequireUtc(nowUtc, nameof(nowUtc));
        LastErrorCode = SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        FinishedAtUtc = nowUtc;
        Status = SpacePublishAttemptStatus.FailedNoEffect;
        OwnsPublishSlot = false;
    }

    public void RequireReconciliation(string errorCode, string summary)
    {
        if (Status is not (
                SpacePublishAttemptStatus.ApplyingWms or
                SpacePublishAttemptStatus.VerifyingWms or
                SpacePublishAttemptStatus.ActivatingRuntime or
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ManualIntervention))
            throw new SpaceVersionStateException("The current publish state cannot enter reconciliation.");
        LastErrorCode = SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        Status = SpacePublishAttemptStatus.ReconciliationRequired;
        CurrentStep = SpacePublishStep.Reconcile;
    }

    public void WaitForRetry(
        SpacePublishStep step,
        string errorCode,
        string summary)
    {
        if (!OwnsPublishSlot || Status is (
                SpacePublishAttemptStatus.Completed or
                SpacePublishAttemptStatus.FailedNoEffect))
        {
            throw new SpaceVersionStateException(
                "A terminal publish attempt cannot wait for retry.");
        }
        if (step is SpacePublishStep.Requested or SpacePublishStep.Complete)
            throw new ArgumentOutOfRangeException(nameof(step));
        CurrentStep = step;
        LastErrorCode = SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        Status = SpacePublishAttemptStatus.WaitingRetry;
    }

    public void RequireManualIntervention(string errorCode, string summary)
    {
        if (!OwnsPublishSlot || Status is (
                SpacePublishAttemptStatus.Completed or
                SpacePublishAttemptStatus.FailedNoEffect))
        {
            throw new SpaceVersionStateException(
                "A terminal publish attempt cannot require manual intervention.");
        }
        LastErrorCode = SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        Status = SpacePublishAttemptStatus.ManualIntervention;
    }

    public void ScheduleManualRetry(
        Guid jobId,
        Guid actorId,
        DateTime nowUtc,
        bool reconciliation)
    {
        SpacePublishPlan.RequireIdentity(jobId, nameof(jobId));
        SpacePublishPlan.RequireIdentity(actorId, nameof(actorId));
        RequireUtc(nowUtc, nameof(nowUtc));
        if (!OwnsPublishSlot || Status is not (
                SpacePublishAttemptStatus.WaitingRetry or
                SpacePublishAttemptStatus.ReconciliationRequired or
                SpacePublishAttemptStatus.ManualIntervention))
        {
            throw new SpaceVersionStateException(
                "Only an active failed publish attempt can be retried manually.");
        }
        JobId = jobId;
        ManualRetryCount = checked(ManualRetryCount + 1);
        LastRetriedAtUtc = nowUtc;
        LastRetriedBy = actorId;
        Status = SpacePublishAttemptStatus.WaitingRetry;
        CurrentStep = reconciliation
            ? SpacePublishStep.Reconcile
            : CurrentStep == SpacePublishStep.Requested
                ? SpacePublishStep.Preflight
                : CurrentStep;
        LastErrorCode = null;
        Summary = "A manual publish recovery Job was queued.";
    }

    private void RequireStatus(SpacePublishAttemptStatus expected)
    {
        if (Status != expected)
            throw new SpaceVersionStateException($"Publish attempt state must be {expected}, but was {Status}.");
    }

    private static string? OptionalText(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }

    private static string RequireJson(string value, string parameterName)
    {
        var normalized = SpacePublishPlan.RequireText(value, 65_536, parameterName);
        try
        {
            using var _ = JsonDocument.Parse(normalized);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Publish request JSON is invalid.", parameterName, exception);
        }
        return normalized;
    }
}

public sealed class SpacePublishBatch : SpaceTenantEntity
{
    private SpacePublishBatch() { }

    public Guid AttemptId { get; private set; }
    public int BatchNo { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public SpacePublishBatchStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ExternalOperationId { get; private set; }
    public string? ResultJson { get; private set; }
    public DateTime? ObservedAtUtc { get; private set; }
    public string RequestJson { get; private set; } = string.Empty;
    public int BatchAttemptNo { get; private set; }

    public static SpacePublishBatch Create(
        Guid tenantId,
        Guid attemptId,
        int batchNo,
        string operationKey,
        string payloadHash,
        string requestJson)
    {
        SpacePublishPlan.RequireIdentity(attemptId, nameof(attemptId));
        if (batchNo < 1)
            throw new ArgumentOutOfRangeException(nameof(batchNo));
        var batch = new SpacePublishBatch
        {
            AttemptId = attemptId,
            BatchNo = batchNo,
            OperationKey = SpacePublishPlan.RequireText(operationKey, 300, nameof(operationKey)),
            PayloadHash = SpacePublishPlan.RequireHash(payloadHash, nameof(payloadHash)),
            RequestJson = RequireJson(requestJson),
            Status = SpacePublishBatchStatus.Pending,
        };
        batch.SetTenant(tenantId);
        return batch;
    }

    public void BeginApply(int jobAttemptNo)
    {
        if (jobAttemptNo < 1)
            throw new ArgumentOutOfRangeException(nameof(jobAttemptNo));
        if (Status is not (
                SpacePublishBatchStatus.Pending or
                SpacePublishBatchStatus.FailedNoEffect or
                SpacePublishBatchStatus.Uncertain or
                SpacePublishBatchStatus.Partial))
        {
            throw new SpaceVersionStateException(
                "Only a safe or uncertain publish batch can begin another apply attempt.");
        }
        Status = SpacePublishBatchStatus.Applying;
        AttemptCount = checked(AttemptCount + 1);
        BatchAttemptNo = jobAttemptNo;
    }

    public void RecordResult(
        SpacePublishBatchStatus status,
        string? externalOperationId,
        string resultJson,
        DateTime observedAtUtc)
    {
        if (Status != SpacePublishBatchStatus.Applying)
            throw new SpaceVersionStateException("Only an applying publish batch can record a result.");
        if (status is not (
                SpacePublishBatchStatus.Applied or
                SpacePublishBatchStatus.FailedNoEffect or
                SpacePublishBatchStatus.Partial or
                SpacePublishBatchStatus.Uncertain))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (observedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Observed time must be UTC.", nameof(observedAtUtc));
        Status = status;
        ExternalOperationId = OptionalText(externalOperationId, 200);
        ResultJson = SpacePublishPlan.RequireText(resultJson, 4_000_000, nameof(resultJson));
        ObservedAtUtc = observedAtUtc;
    }

    public void MarkVerified()
    {
        if (Status != SpacePublishBatchStatus.Applied)
            throw new SpaceVersionStateException("Only an applied publish batch can be verified.");
        Status = SpacePublishBatchStatus.Verified;
    }

    private static string RequireJson(string value)
    {
        var normalized = SpacePublishPlan.RequireText(value, 4_000_000, nameof(value));
        try
        {
            using var _ = JsonDocument.Parse(normalized);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Publish batch request JSON is invalid.", nameof(value), exception);
        }
        return normalized;
    }

    private static string? OptionalText(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        return normalized;
    }
}

public sealed class SpaceWmsReceipt : SpaceTenantEntity
{
    private SpaceWmsReceipt() { }

    public Guid BatchId { get; private set; }
    public Guid LogicalId { get; private set; }
    public string LocationCode { get; private set; } = string.Empty;
    public short Action { get; private set; }
    public SpaceWmsReceiptOutcome Outcome { get; private set; }
    public string? ExternalLocationId { get; private set; }
    public string? ExternalVersion { get; private set; }
    public string? ResponseHash { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    public static SpaceWmsReceipt Create(
        Guid tenantId,
        Guid batchId,
        Guid logicalId,
        string locationCode,
        short action,
        SpaceWmsReceiptOutcome outcome,
        string? externalLocationId,
        string? externalVersion,
        string? responseHash,
        string? errorCode,
        DateTime receivedAtUtc)
    {
        SpacePublishPlan.RequireIdentity(batchId, nameof(batchId));
        SpacePublishPlan.RequireIdentity(logicalId, nameof(logicalId));
        if (receivedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Receipt time must be UTC.", nameof(receivedAtUtc));
        var receipt = new SpaceWmsReceipt
        {
            BatchId = batchId,
            LogicalId = logicalId,
            LocationCode = SpacePublishPlan.RequireText(locationCode, 256, nameof(locationCode)),
            Action = action,
            Outcome = outcome,
            ExternalLocationId = OptionalText(externalLocationId, 200),
            ExternalVersion = OptionalText(externalVersion, 100),
            ResponseHash = responseHash is null ? null : SpacePublishPlan.RequireHash(responseHash, nameof(responseHash)),
            ErrorCode = OptionalText(errorCode, 100),
            ReceivedAtUtc = receivedAtUtc,
        };
        receipt.SetTenant(tenantId);
        return receipt;
    }

    private static string? OptionalText(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        return normalized;
    }
}

public sealed class SpaceReconciliationIssue : SpaceTenantEntity
{
    private SpaceReconciliationIssue() { }

    public Guid AttemptId { get; private set; }
    public Guid? LogicalId { get; private set; }
    public string? ExpectedStateHash { get; private set; }
    public string? WmsStateHash { get; private set; }
    public string? RuntimeStateHash { get; private set; }
    public SpaceReconciliationClassification Classification { get; private set; }
    public SpaceReconciliationStatus Status { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string? Resolution { get; private set; }

    public static SpaceReconciliationIssue Create(
        Guid tenantId,
        Guid attemptId,
        Guid? logicalId,
        string? expectedStateHash,
        string? wmsStateHash,
        string? runtimeStateHash,
        SpaceReconciliationClassification classification,
        string summary)
    {
        SpacePublishPlan.RequireIdentity(attemptId, nameof(attemptId));
        if (logicalId == Guid.Empty)
            throw new ArgumentException("Logical identity cannot be empty.", nameof(logicalId));
        var issue = new SpaceReconciliationIssue
        {
            AttemptId = attemptId,
            LogicalId = logicalId,
            ExpectedStateHash = OptionalHash(expectedStateHash, nameof(expectedStateHash)),
            WmsStateHash = OptionalHash(wmsStateHash, nameof(wmsStateHash)),
            RuntimeStateHash = OptionalHash(runtimeStateHash, nameof(runtimeStateHash)),
            Classification = classification,
            Status = SpaceReconciliationStatus.Open,
            Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary)),
        };
        issue.SetTenant(tenantId);
        return issue;
    }

    public void BeginInvestigation(string resolution)
    {
        if (Status is SpaceReconciliationStatus.Resolved)
            throw new SpaceVersionStateException("A resolved reconciliation issue cannot be reopened.");
        Resolution = SpacePublishPlan.RequireText(resolution, 4000, nameof(resolution));
        Status = SpaceReconciliationStatus.Investigating;
    }

    public void Resolve(string resolution)
    {
        Resolution = SpacePublishPlan.RequireText(resolution, 4000, nameof(resolution));
        Status = SpaceReconciliationStatus.Resolved;
    }

    private static string? OptionalHash(string? value, string parameterName) =>
        value is null ? null : SpacePublishPlan.RequireHash(value, parameterName);
}

public sealed class SpacePublishAuditEvent : SpaceTenantEntity
{
    private SpacePublishAuditEvent() { }

    public Guid AttemptId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid? BatchId { get; private set; }
    public int EventNo { get; private set; }
    public SpacePublishAuditEventType EventType { get; private set; }
    public SpacePublishAttemptStatus AttemptStatus { get; private set; }
    public SpacePublishStep Step { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string? ErrorCode { get; private set; }
    public string EvidenceJson { get; private set; } = string.Empty;
    public string EvidenceHash { get; private set; } = string.Empty;
    public string? PreviousEventHash { get; private set; }
    public string EventHash { get; private set; } = string.Empty;

    public static SpacePublishAuditEvent Create(
        Guid tenantId,
        Guid attemptId,
        Guid jobId,
        Guid? batchId,
        int eventNo,
        SpacePublishAuditEventType eventType,
        SpacePublishAttemptStatus attemptStatus,
        SpacePublishStep step,
        Guid actorId,
        Guid correlationId,
        DateTime occurredAtUtc,
        string deduplicationKey,
        string summary,
        string? errorCode,
        string evidenceJson,
        string? previousEventHash)
    {
        SpacePublishPlan.RequireIdentity(attemptId, nameof(attemptId));
        SpacePublishPlan.RequireIdentity(jobId, nameof(jobId));
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch identity cannot be empty.", nameof(batchId));
        if (eventNo < 1)
            throw new ArgumentOutOfRangeException(nameof(eventNo));
        SpacePublishPlan.RequireIdentity(actorId, nameof(actorId));
        SpacePublishPlan.RequireIdentity(correlationId, nameof(correlationId));
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Audit time must be UTC.", nameof(occurredAtUtc));
        var normalizedEvidence = SpacePublishPlan.RequireText(
            evidenceJson, 1_000_000, nameof(evidenceJson));
        try
        {
            using var _ = JsonDocument.Parse(normalizedEvidence);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Publish audit evidence JSON is invalid.", nameof(evidenceJson), exception);
        }
        var evidenceHash = Hash(normalizedEvidence);
        var previous = previousEventHash is null
            ? null
            : SpacePublishPlan.RequireHash(previousEventHash, nameof(previousEventHash));
        var normalizedKey = SpacePublishPlan.RequireText(
            deduplicationKey, 300, nameof(deduplicationKey));
        var normalizedSummary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        var normalizedError = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        var eventHash = Hash(string.Join(
            "\n",
            tenantId.ToString("D"),
            attemptId.ToString("D"),
            jobId.ToString("D"),
            batchId?.ToString("D") ?? "-",
            eventNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((short)eventType).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((short)attemptStatus).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((short)step).ToString(System.Globalization.CultureInfo.InvariantCulture),
            actorId.ToString("D"),
            correlationId.ToString("D"),
            occurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            normalizedKey,
            normalizedSummary,
            normalizedError ?? "-",
            evidenceHash,
            previous ?? "-"));
        var result = new SpacePublishAuditEvent
        {
            AttemptId = attemptId,
            JobId = jobId,
            BatchId = batchId,
            EventNo = eventNo,
            EventType = eventType,
            AttemptStatus = attemptStatus,
            Step = step,
            ActorId = actorId,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredAtUtc,
            DeduplicationKey = normalizedKey,
            Summary = normalizedSummary,
            ErrorCode = normalizedError,
            EvidenceJson = normalizedEvidence,
            EvidenceHash = evidenceHash,
            PreviousEventHash = previous,
            EventHash = eventHash,
        };
        result.SetTenant(tenantId);
        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public sealed class SpaceRuntimeElement : SpaceTenantEntity
{
    private SpaceRuntimeElement() { }

    public Guid SiteId { get; private set; }
    public Guid ModelVersionId { get; private set; }
    public Guid LogicalId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public bool IsActive { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;

    public static SpaceRuntimeElement Create(
        Guid tenantId,
        Guid siteId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        bool isActive,
        string payloadJson,
        string payloadHash)
    {
        var element = new SpaceRuntimeElement();
        element.SetTenant(tenantId);
        element.Update(siteId, modelVersionId, logicalId, floorLogicalId, isActive, payloadJson, payloadHash);
        return element;
    }

    public void Update(
        Guid siteId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        bool isActive,
        string payloadJson,
        string payloadHash)
    {
        SpacePublishPlan.RequireIdentity(siteId, nameof(siteId));
        SpacePublishPlan.RequireIdentity(modelVersionId, nameof(modelVersionId));
        SpacePublishPlan.RequireIdentity(logicalId, nameof(logicalId));
        SpacePublishPlan.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        SiteId = siteId;
        ModelVersionId = modelVersionId;
        LogicalId = logicalId;
        FloorLogicalId = floorLogicalId;
        IsActive = isActive;
        PayloadJson = SpacePublishPlan.RequireText(payloadJson, 4_000_000, nameof(payloadJson));
        PayloadHash = SpacePublishPlan.RequireHash(payloadHash, nameof(payloadHash));
    }

    public void Deactivate(Guid modelVersionId)
    {
        SpacePublishPlan.RequireIdentity(modelVersionId, nameof(modelVersionId));
        ModelVersionId = modelVersionId;
        IsActive = false;
    }
}
