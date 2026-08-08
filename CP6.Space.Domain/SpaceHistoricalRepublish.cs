namespace CP6.Space.Domain;

public sealed class SpaceHistoricalRepublish : SpaceTenantEntity
{
    private SpaceHistoricalRepublish()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid HistoricalVersionId { get; private set; }
    public Guid ExpectedPublishedVersionId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid? ValidationRunId { get; private set; }
    public Guid? PublishAttemptId { get; private set; }
    public string BusinessIdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? ApprovalReference { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid CorrelationId { get; private set; }
    public SpaceHistoricalRepublishStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceHistoricalRepublish Create(
        Guid tenantId,
        Guid siteId,
        Guid modelId,
        Guid historicalVersionId,
        Guid expectedPublishedVersionId,
        string businessIdempotencyKey,
        string requestHash,
        string reason,
        string? approvalReference,
        Guid requestedBy,
        DateTime requestedAtUtc,
        Guid correlationId)
    {
        RequireIdentity(siteId, nameof(siteId));
        RequireIdentity(modelId, nameof(modelId));
        RequireIdentity(historicalVersionId, nameof(historicalVersionId));
        RequireIdentity(
            expectedPublishedVersionId,
            nameof(expectedPublishedVersionId));
        RequireIdentity(requestedBy, nameof(requestedBy));
        RequireIdentity(correlationId, nameof(correlationId));
        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));

        var operation = new SpaceHistoricalRepublish
        {
            SiteId = siteId,
            ModelId = modelId,
            HistoricalVersionId = historicalVersionId,
            ExpectedPublishedVersionId = expectedPublishedVersionId,
            BusinessIdempotencyKey = RequireText(
                businessIdempotencyKey,
                128,
                nameof(businessIdempotencyKey)),
            RequestHash = RequireHash(requestHash, nameof(requestHash)),
            Reason = RequireText(reason, 1000, nameof(reason)),
            ApprovalReference = OptionalText(
                approvalReference,
                500,
                nameof(approvalReference)),
            RequestedBy = requestedBy,
            RequestedAtUtc = requestedAtUtc,
            CorrelationId = correlationId,
            Status = SpaceHistoricalRepublishStatus.Requested,
        };
        operation.SetTenant(tenantId);
        return operation;
    }

    public void BindReservation(Guid targetVersionId, Guid jobId)
    {
        RequireIdentity(targetVersionId, nameof(targetVersionId));
        RequireIdentity(jobId, nameof(jobId));
        if (TargetVersionId != Guid.Empty || JobId != Guid.Empty)
        {
            if (TargetVersionId == targetVersionId && JobId == jobId)
                return;
            throw new SpaceVersionStateException(
                "The historical republish reservation is already bound.");
        }
        TargetVersionId = targetVersionId;
        JobId = jobId;
    }

    public void MarkSnapshotCloned()
    {
        if (Status is SpaceHistoricalRepublishStatus.SnapshotCloned or
            SpaceHistoricalRepublishStatus.ValidationPassed or
            SpaceHistoricalRepublishStatus.ValidationBlocked or
            SpaceHistoricalRepublishStatus.PublishQueued)
        {
            return;
        }
        RequireStatus(SpaceHistoricalRepublishStatus.Requested);
        Status = SpaceHistoricalRepublishStatus.SnapshotCloned;
    }

    public void MarkValidationPassed(Guid validationRunId)
    {
        RequireIdentity(validationRunId, nameof(validationRunId));
        if (ValidationRunId.HasValue)
        {
            if (ValidationRunId == validationRunId &&
                Status is SpaceHistoricalRepublishStatus.ValidationPassed or
                    SpaceHistoricalRepublishStatus.PublishQueued)
            {
                return;
            }
            throw new SpaceVersionStateException(
                "The historical republish validation is already bound.");
        }
        RequireStatus(SpaceHistoricalRepublishStatus.SnapshotCloned);
        ValidationRunId = validationRunId;
        Status = SpaceHistoricalRepublishStatus.ValidationPassed;
    }

    public void MarkValidationBlocked(Guid validationRunId)
    {
        RequireIdentity(validationRunId, nameof(validationRunId));
        if (ValidationRunId.HasValue)
        {
            if (ValidationRunId == validationRunId &&
                Status == SpaceHistoricalRepublishStatus.ValidationBlocked)
            {
                return;
            }
            throw new SpaceVersionStateException(
                "The historical republish validation is already bound.");
        }
        RequireStatus(SpaceHistoricalRepublishStatus.SnapshotCloned);
        ValidationRunId = validationRunId;
        Status = SpaceHistoricalRepublishStatus.ValidationBlocked;
    }

    public void MarkPublishQueued(Guid publishAttemptId)
    {
        RequireIdentity(publishAttemptId, nameof(publishAttemptId));
        if (PublishAttemptId.HasValue)
        {
            if (PublishAttemptId == publishAttemptId &&
                Status == SpaceHistoricalRepublishStatus.PublishQueued)
            {
                return;
            }
            throw new SpaceVersionStateException(
                "The historical republish publish attempt is already bound.");
        }
        RequireStatus(SpaceHistoricalRepublishStatus.ValidationPassed);
        PublishAttemptId = publishAttemptId;
        Status = SpaceHistoricalRepublishStatus.PublishQueued;
    }

    private void RequireStatus(SpaceHistoricalRepublishStatus expected)
    {
        if (Status != expected)
        {
            throw new SpaceVersionStateException(
                $"Historical republish state must be {expected}, but was {Status}.");
        }
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty identity is required.", parameterName);
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hexadecimal hash is required.", parameterName);
        return value.ToLowerInvariant();
    }

    private static string RequireText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }
        return normalized;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A UTC timestamp is required.", parameterName);
    }
}
