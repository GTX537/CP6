namespace CP6.Space.Domain;

public sealed record SpaceGenerationRunDefinition(
    Guid TenantId,
    Guid SiteId,
    Guid ModelVersionId,
    Guid SourceId,
    string SourceHash,
    long BaseContentRevision,
    string IdempotencyKeyHash,
    string BusinessKeyHash,
    Guid? BasedOnRunId,
    Guid? MappingProfileVersionId,
    Guid? RackGenerationProfileVersionId,
    string RuleVersion,
    SpaceAiPolicySnapshot PolicySnapshot,
    Guid? ProviderConfigVersionId,
    string InputSchemaVersion,
    Guid JobId,
    Guid? TargetFloorLogicalId = null);

public sealed class SpaceGenerationRun : SpaceTenantEntity
{
    private SpaceGenerationRun()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelVersionId { get; private set; }
    public Guid SourceId { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public long BaseContentRevision { get; private set; }
    public SpaceGenerationRunStatus Status { get; private set; }
    public int Progress { get; private set; }
    public string IdempotencyKeyHash { get; private set; } = string.Empty;
    public string BusinessKeyHash { get; private set; } = string.Empty;
    public Guid? BasedOnRunId { get; private set; }
    public bool IsCurrent { get; private set; }
    public Guid? MappingProfileVersionId { get; private set; }
    public Guid? RackGenerationProfileVersionId { get; private set; }
    public string RuleVersion { get; private set; } = string.Empty;
    public SpaceAiPolicySnapshot PolicySnapshot { get; private set; }
    public Guid? ProviderConfigVersionId { get; private set; }
    public string? ProviderCode { get; private set; }
    public string? ProviderModel { get; private set; }
    public string InputSchemaVersion { get; private set; } = string.Empty;
    public string? OutputSchemaVersion { get; private set; }
    public Guid JobId { get; private set; }
    public Guid? TargetFloorLogicalId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public string? DegradedReason { get; private set; }
    public DateTime? CancelRequestedAtUtc { get; private set; }
    public bool CancelPending { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? ReviewCompletedAtUtc { get; private set; }
    public long? AppliedContentRevision { get; private set; }
    public Guid? ApplyJobId { get; private set; }
    public Guid? ApplyCommandBatchId { get; private set; }
    public string? ApplyReviewEtag { get; private set; }
    public string? ApplyExpectedRunRowVersion { get; private set; }
    public string? ApplyPlanHash { get; private set; }
    public DateTime? ApplyPreparedAtUtc { get; private set; }
    public string? AppliedCountsJson { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsTerminal =>
        Status is SpaceGenerationRunStatus.Succeeded
            or SpaceGenerationRunStatus.Cancelled;

    public static SpaceGenerationRun Create(
        SpaceGenerationRunDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        RequireId(definition.SiteId, nameof(definition.SiteId));
        RequireId(
            definition.ModelVersionId,
            nameof(definition.ModelVersionId));
        RequireId(definition.SourceId, nameof(definition.SourceId));
        RequireId(definition.JobId, nameof(definition.JobId));
        if (definition.BaseContentRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.BaseContentRevision));
        }
        if (definition.BasedOnRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Based-on run cannot be empty.",
                nameof(definition.BasedOnRunId));
        }
        if (definition.MappingProfileVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Mapping profile version cannot be empty.",
                nameof(definition.MappingProfileVersionId));
        }
        if (definition.RackGenerationProfileVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Rack generation profile version cannot be empty.",
                nameof(definition.RackGenerationProfileVersionId));
        }
        if (definition.ProviderConfigVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Provider config version cannot be empty.",
                nameof(definition.ProviderConfigVersionId));
        }
        if (definition.TargetFloorLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Target floor logical identity cannot be empty.",
                nameof(definition.TargetFloorLogicalId));
        }
        if (!Enum.IsDefined(definition.PolicySnapshot))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.PolicySnapshot));
        }
        if (definition.PolicySnapshot == SpaceAiPolicySnapshot.Disabled &&
            definition.ProviderConfigVersionId is not null)
        {
            throw new ArgumentException(
                "Disabled policy cannot pin a Provider config.",
                nameof(definition.ProviderConfigVersionId));
        }
        if (definition.PolicySnapshot != SpaceAiPolicySnapshot.Disabled &&
            definition.ProviderConfigVersionId is null)
        {
            throw new ArgumentException(
                "An enabled policy must pin a Provider config version.",
                nameof(definition.ProviderConfigVersionId));
        }

        var run = new SpaceGenerationRun
        {
            SiteId = definition.SiteId,
            ModelVersionId = definition.ModelVersionId,
            SourceId = definition.SourceId,
            SourceHash = RequireHash(
                definition.SourceHash,
                nameof(definition.SourceHash)),
            BaseContentRevision = definition.BaseContentRevision,
            Status = SpaceGenerationRunStatus.Queued,
            Progress = 0,
            IdempotencyKeyHash = RequireHash(
                definition.IdempotencyKeyHash,
                nameof(definition.IdempotencyKeyHash)),
            BusinessKeyHash = RequireHash(
                definition.BusinessKeyHash,
                nameof(definition.BusinessKeyHash)),
            BasedOnRunId = definition.BasedOnRunId,
            IsCurrent = true,
            MappingProfileVersionId =
                definition.MappingProfileVersionId,
            RackGenerationProfileVersionId =
                definition.RackGenerationProfileVersionId,
            RuleVersion = RequireText(
                definition.RuleVersion,
                64,
                nameof(definition.RuleVersion)),
            PolicySnapshot = definition.PolicySnapshot,
            ProviderConfigVersionId =
                definition.ProviderConfigVersionId,
            InputSchemaVersion = RequireText(
                definition.InputSchemaVersion,
                32,
                nameof(definition.InputSchemaVersion)),
            JobId = definition.JobId,
            TargetFloorLogicalId = definition.TargetFloorLogicalId,
        };
        run.SetTenant(definition.TenantId);
        return run;
    }

    public void BeginPreparing() =>
        Transition(
            SpaceGenerationRunStatus.Queued,
            SpaceGenerationRunStatus.Preparing);

    public void BeginInferring() =>
        Transition(
            SpaceGenerationRunStatus.Preparing,
            SpaceGenerationRunStatus.Inferring);

    public void BeginValidating() =>
        Transition(
            SpaceGenerationRunStatus.Inferring,
            SpaceGenerationRunStatus.Validating);

    public void MarkAwaitingReview() =>
        Transition(
            SpaceGenerationRunStatus.Validating,
            SpaceGenerationRunStatus.AwaitingReview);

    public void MarkReviewCompleted(DateTime reviewCompletedAtUtc)
    {
        RequireUtc(reviewCompletedAtUtc, nameof(reviewCompletedAtUtc));
        RequireStatus(SpaceGenerationRunStatus.AwaitingReview);
        if (ReviewCompletedAtUtc is not null)
        {
            throw new SpaceGenerationStateException(
                "Generation review is already complete.");
        }
        ReviewCompletedAtUtc = reviewCompletedAtUtc;
    }

    public void BeginApplying(
        Guid applyJobId,
        Guid applyCommandBatchId,
        string reviewEtag,
        string expectedRunRowVersion)
    {
        RequireId(applyJobId, nameof(applyJobId));
        RequireId(applyCommandBatchId, nameof(applyCommandBatchId));
        if (ReviewCompletedAtUtc is null)
        {
            throw new SpaceGenerationStateException(
                "Generation review must complete before apply can begin.");
        }
        Transition(
            SpaceGenerationRunStatus.AwaitingReview,
            SpaceGenerationRunStatus.Applying);
        ApplyJobId = applyJobId;
        ApplyCommandBatchId = applyCommandBatchId;
        ApplyReviewEtag = RequireHash(reviewEtag, nameof(reviewEtag));
        ApplyExpectedRunRowVersion = RequireText(
            expectedRunRowVersion,
            128,
            nameof(expectedRunRowVersion));
    }

    public void RecordApplyPlan(
        string applyPlanHash,
        DateTime preparedAtUtc)
    {
        RequireStatus(SpaceGenerationRunStatus.Applying);
        RequireUtc(preparedAtUtc, nameof(preparedAtUtc));
        var normalized = RequireHash(applyPlanHash, nameof(applyPlanHash));
        if (ApplyPlanHash is not null && ApplyPlanHash != normalized)
        {
            throw new SpaceGenerationStateException(
                "The generation Apply plan is immutable once prepared.");
        }

        ApplyPlanHash = normalized;
        ApplyPreparedAtUtc ??= preparedAtUtc;
    }

    public void MarkSucceeded(
        long appliedContentRevision,
        string appliedCountsJson)
    {
        RequireStatus(SpaceGenerationRunStatus.Applying);
        if (appliedContentRevision <= BaseContentRevision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedContentRevision),
                "Applied revision must advance beyond the run base.");
        }

        AppliedContentRevision = appliedContentRevision;
        AppliedCountsJson = RequireJson(
            appliedCountsJson,
            nameof(appliedCountsJson));
        Progress = 100;
        Status = SpaceGenerationRunStatus.Succeeded;
        IsCurrent = false;
    }

    private static string RequireJson(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Canonical JSON is required.",
                parameterName);
        }
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException(
                "Valid JSON is required.",
                parameterName,
                exception);
        }
        return value;
    }

    public void RecordProviderResult(
        string providerCode,
        string providerModel,
        string outputSchemaVersion)
    {
        RequireStatus(SpaceGenerationRunStatus.Inferring);
        ProviderCode = RequireText(
            providerCode,
            64,
            nameof(providerCode));
        ProviderModel = RequireText(
            providerModel,
            128,
            nameof(providerModel));
        OutputSchemaVersion = RequireText(
            outputSchemaVersion,
            32,
            nameof(outputSchemaVersion));
    }

    public void RecordDegradedReason(string reason)
    {
        if (Status is not (
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating))
        {
            throw StateError("record a degraded reason");
        }
        DegradedReason = RequireText(reason, 64, nameof(reason));
    }

    public void ReportProgress(int progress)
    {
        if (Status is not (
            SpaceGenerationRunStatus.Queued or
            SpaceGenerationRunStatus.Preparing or
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating or
            SpaceGenerationRunStatus.AwaitingReview or
            SpaceGenerationRunStatus.Applying))
        {
            throw StateError("report progress");
        }
        if (progress is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }
        if (progress < Progress)
        {
            throw new SpaceGenerationStateException(
                "Generation progress cannot move backwards.");
        }
        Progress = progress;
    }

    public void MarkFailed(string failureCode, string failureSummary)
    {
        if (Status is not (
            SpaceGenerationRunStatus.Queued or
            SpaceGenerationRunStatus.Preparing or
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating or
            SpaceGenerationRunStatus.Applying))
        {
            throw StateError("fail");
        }

        FailureCode = RequireText(
            failureCode,
            64,
            nameof(failureCode));
        FailureSummary = RequireText(
            failureSummary,
            1024,
            nameof(failureSummary));
        Status = SpaceGenerationRunStatus.Failed;
    }

    public void Retry()
    {
        RequireStatus(SpaceGenerationRunStatus.Failed);
        FailureCode = null;
        FailureSummary = null;
        ProviderCode = null;
        ProviderModel = null;
        OutputSchemaVersion = null;
        DegradedReason = null;
        CancelRequestedAtUtc = null;
        CancelPending = false;
        Status = SpaceGenerationRunStatus.Queued;
    }

    public void MarkStale()
    {
        if (Status is not (
            SpaceGenerationRunStatus.AwaitingReview or
            SpaceGenerationRunStatus.Applying))
        {
            throw StateError("become stale");
        }
        Status = SpaceGenerationRunStatus.Stale;
        IsCurrent = false;
    }

    public void RequestCancellation(
        DateTime requestedAtUtc,
        bool providerResponsePending)
    {
        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
        if (Status is not (
            SpaceGenerationRunStatus.Queued or
            SpaceGenerationRunStatus.Preparing or
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating))
        {
            throw StateError("request cancellation");
        }

        CancelRequestedAtUtc = requestedAtUtc;
        CancelPending = providerResponsePending;
        if (!providerResponsePending)
            CompleteCancellation(requestedAtUtc);
    }

    public void CompleteCancellation(DateTime cancelledAtUtc)
    {
        RequireUtc(cancelledAtUtc, nameof(cancelledAtUtc));
        if (CancelRequestedAtUtc is null)
        {
            throw new SpaceGenerationStateException(
                "Cancellation must be requested before completion.");
        }
        if (Status is not (
            SpaceGenerationRunStatus.Queued or
            SpaceGenerationRunStatus.Preparing or
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating))
        {
            throw StateError("complete cancellation");
        }

        CancelPending = false;
        CancelledAtUtc = cancelledAtUtc;
        Status = SpaceGenerationRunStatus.Cancelled;
        IsCurrent = false;
    }

    public void Discard(DateTime discardedAtUtc)
    {
        RequireUtc(discardedAtUtc, nameof(discardedAtUtc));
        if (Status is not (
            SpaceGenerationRunStatus.AwaitingReview or
            SpaceGenerationRunStatus.Failed or
            SpaceGenerationRunStatus.Stale))
        {
            throw StateError("be discarded");
        }

        CancelRequestedAtUtc = discardedAtUtc;
        CancelledAtUtc = discardedAtUtc;
        CancelPending = false;
        Status = SpaceGenerationRunStatus.Cancelled;
        IsCurrent = false;
    }

    private void Transition(
        SpaceGenerationRunStatus expected,
        SpaceGenerationRunStatus next)
    {
        RequireStatus(expected);
        Status = next;
    }

    private void RequireStatus(SpaceGenerationRunStatus expected)
    {
        if (Status != expected)
        {
            throw new SpaceGenerationStateException(
                $"Generation run state must be {expected}, " +
                $"but was {Status}.");
        }
    }

    private SpaceGenerationStateException StateError(string action) =>
        new($"Generation run cannot {action} from {Status}.");

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    internal static string RequireHash(
        string value,
        string parameterName)
    {
        if (value is null ||
            value.Length != 64 ||
            !value.All(character =>
                character is >= '0' and <= '9' ||
                character is >= 'a' and <= 'f'))
        {
            throw new ArgumentException(
                "A lowercase SHA-256 hex value is required.",
                parameterName);
        }
        return value;
    }

    internal static string RequireText(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"A value up to {maxLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    internal static void RequireUtc(
        DateTime value,
        string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Time must be UTC.",
                parameterName);
        }
    }
}
