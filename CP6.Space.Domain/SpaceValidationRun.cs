namespace CP6.Space.Domain;

public sealed class SpaceValidationRun : SpaceTenantEntity
{
    private SpaceValidationRun()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public long ContentRevision { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string RuleSetVersion { get; private set; } = string.Empty;
    public string AdapterId { get; private set; } = string.Empty;
    public string CapabilityHash { get; private set; } = string.Empty;
    public SpaceValidationStatus Status { get; private set; }
    public int BlockingCount { get; private set; }
    public int WarningCount { get; private set; }
    public int InfoCount { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public Guid JobId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsReusable =>
        Status is SpaceValidationStatus.Passed or SpaceValidationStatus.Blocked;

    public bool IsTerminal =>
        Status is SpaceValidationStatus.Passed
            or SpaceValidationStatus.Blocked
            or SpaceValidationStatus.Failed;

    public static SpaceValidationRun CreateQueued(
        Guid tenantId,
        Guid modelVersionId,
        long contentRevision,
        string contentHash,
        string ruleSetVersion,
        string adapterId,
        string capabilityHash,
        Guid requestedBy,
        DateTime requestedAtUtc,
        Guid jobId,
        Guid correlationId)
    {
        if (modelVersionId == Guid.Empty)
            throw new ArgumentException(
                "Model version is required.",
                nameof(modelVersionId));
        if (contentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(contentRevision));
        if (requestedBy == Guid.Empty)
            throw new ArgumentException(
                "Validation requester is required.",
                nameof(requestedBy));
        if (jobId == Guid.Empty)
            throw new ArgumentException("Validation Job is required.", nameof(jobId));
        if (correlationId == Guid.Empty)
            throw new ArgumentException(
                "Correlation ID is required.",
                nameof(correlationId));
        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));

        var run = new SpaceValidationRun
        {
            ModelVersionId = modelVersionId,
            ContentRevision = contentRevision,
            ContentHash = RequireHash(contentHash, nameof(contentHash)),
            RuleSetVersion = RequireText(
                ruleSetVersion,
                50,
                nameof(ruleSetVersion)),
            AdapterId = RequireText(adapterId, 100, nameof(adapterId)),
            CapabilityHash = RequireHash(
                capabilityHash,
                nameof(capabilityHash)),
            Status = SpaceValidationStatus.Queued,
            RequestedAtUtc = requestedAtUtc,
            RequestedBy = requestedBy,
            JobId = jobId,
            CorrelationId = correlationId,
        };
        run.SetTenant(tenantId);
        return run;
    }

    public void Start(DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != SpaceValidationStatus.Queued)
            throw new SpaceVersionStateException(
                "Only a queued ValidationRun can start.");

        Status = SpaceValidationStatus.Running;
        StartedAtUtc = nowUtc;
    }

    public void Pass(
        int blockingCount,
        int warningCount,
        int infoCount,
        DateTime nowUtc)
    {
        if (blockingCount != 0)
            throw new ArgumentOutOfRangeException(
                nameof(blockingCount),
                "A passed ValidationRun cannot contain Blocking issues.");
        Complete(
            SpaceValidationStatus.Passed,
            blockingCount,
            warningCount,
            infoCount,
            nowUtc);
    }

    public void Block(
        int blockingCount,
        int warningCount,
        int infoCount,
        DateTime nowUtc)
    {
        if (blockingCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(blockingCount),
                "A blocked ValidationRun requires a Blocking issue.");
        Complete(
            SpaceValidationStatus.Blocked,
            blockingCount,
            warningCount,
            infoCount,
            nowUtc);
    }

    public void Fail(string code, string summary, DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status is not (
                SpaceValidationStatus.Queued or
                SpaceValidationStatus.Running))
        {
            throw new SpaceVersionStateException(
                "Only an active ValidationRun can fail.");
        }

        FailureCode = RequireText(code, 100, nameof(code));
        FailureSummary = RequireText(summary, 1000, nameof(summary));
        Status = SpaceValidationStatus.Failed;
        FinishedAtUtc = nowUtc;
    }

    private void Complete(
        SpaceValidationStatus status,
        int blockingCount,
        int warningCount,
        int infoCount,
        DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != SpaceValidationStatus.Running)
            throw new SpaceVersionStateException(
                "Only a running ValidationRun can complete.");
        if (blockingCount < 0 || warningCount < 0 || infoCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(blockingCount),
                "Validation issue counts cannot be negative.");

        BlockingCount = blockingCount;
        WarningCount = warningCount;
        InfoCount = infoCount;
        Status = status;
        FinishedAtUtc = nowUtc;
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "A SHA-256 hex value is required.",
                parameterName);
        return value.ToLowerInvariant();
    }

    private static string RequireText(
        string value,
        int maxLength,
        string parameterName)
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
