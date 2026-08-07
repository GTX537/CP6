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
        };
        attempt.SetTenant(tenantId);
        return attempt;
    }

    public void BeginPreflight()
    {
        RequireStatus(SpacePublishAttemptStatus.Requested);
        Status = SpacePublishAttemptStatus.Preflighting;
        CurrentStep = SpacePublishStep.Preflight;
    }

    public void BeginApplyingWms()
    {
        RequireStatus(SpacePublishAttemptStatus.Preflighting);
        Status = SpacePublishAttemptStatus.ApplyingWms;
        CurrentStep = SpacePublishStep.ApplyWms;
        LastErrorCode = null;
        Summary = null;
    }

    public void BeginVerifyingWms(DateTime nowUtc)
    {
        RequireStatus(SpacePublishAttemptStatus.ApplyingWms);
        RequireUtc(nowUtc, nameof(nowUtc));
        Status = SpacePublishAttemptStatus.VerifyingWms;
        CurrentStep = SpacePublishStep.VerifyWms;
        WmsCommittedAtUtc = nowUtc;
    }

    public void BeginActivatingRuntime()
    {
        RequireStatus(SpacePublishAttemptStatus.VerifyingWms);
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
        if (Status is not (SpacePublishAttemptStatus.Preflighting or SpacePublishAttemptStatus.ApplyingWms))
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
                SpacePublishAttemptStatus.ActivatingRuntime))
            throw new SpaceVersionStateException("The current publish state cannot enter reconciliation.");
        LastErrorCode = SpacePublishPlan.RequireText(errorCode, 100, nameof(errorCode));
        Summary = SpacePublishPlan.RequireText(summary, 2000, nameof(summary));
        Status = SpacePublishAttemptStatus.ReconciliationRequired;
        CurrentStep = SpacePublishStep.Reconcile;
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

    public static SpacePublishBatch Create(
        Guid tenantId,
        Guid attemptId,
        int batchNo,
        string operationKey,
        string payloadHash)
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
            Status = SpacePublishBatchStatus.Pending,
        };
        batch.SetTenant(tenantId);
        return batch;
    }

    public void BeginApply()
    {
        if (Status != SpacePublishBatchStatus.Pending)
            throw new SpaceVersionStateException("Only a pending publish batch can be applied.");
        Status = SpacePublishBatchStatus.Applying;
        AttemptCount = checked(AttemptCount + 1);
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

    private static string? OptionalHash(string? value, string parameterName) =>
        value is null ? null : SpacePublishPlan.RequireHash(value, parameterName);
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
