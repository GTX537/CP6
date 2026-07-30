using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceModelIssue : SpaceTenantEntity
{
    private SpaceModelIssue()
    {
    }

    public Guid? ModelVersionId { get; private set; }
    public Guid? SourceId { get; private set; }
    public Guid? JobId { get; private set; }
    public SpaceIssueSeverity Severity { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string? SourceRef { get; private set; }
    public Guid? TargetLogicalId { get; private set; }
    public string MessageArgsJson { get; private set; } = "{}";
    public string? SuggestedActionCode { get; private set; }
    public SpaceIssueStatus Status { get; private set; }
    public Guid? ResolutionCommandBatchId { get; private set; }
    public Guid? AcknowledgedBy { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public string? AcknowledgementReason { get; private set; }

    public static SpaceModelIssue Create(
        Guid tenantId,
        Guid? modelVersionId,
        Guid? sourceId,
        Guid? jobId,
        SpaceIssueSeverity severity,
        string code,
        string? sourceRef = null,
        Guid? targetLogicalId = null,
        string messageArgsJson = "{}",
        string? suggestedActionCode = null)
    {
        if (!modelVersionId.HasValue && !sourceId.HasValue && !jobId.HasValue)
            throw new ArgumentException("At least one Issue context is required.");
        if (sourceId.HasValue && !modelVersionId.HasValue)
            throw new ArgumentException(
                "A source Issue must also identify its model version.");
        EnsureOptionalId(modelVersionId, nameof(modelVersionId));
        EnsureOptionalId(sourceId, nameof(sourceId));
        EnsureOptionalId(jobId, nameof(jobId));
        EnsureOptionalId(targetLogicalId, nameof(targetLogicalId));

        var issue = new SpaceModelIssue
        {
            ModelVersionId = modelVersionId,
            SourceId = sourceId,
            JobId = jobId,
            Severity = severity,
            Code = RequireText(code, 100, nameof(code)),
            SourceRef = OptionalText(sourceRef, 500, nameof(sourceRef)),
            TargetLogicalId = targetLogicalId,
            MessageArgsJson = RequireJson(messageArgsJson, nameof(messageArgsJson)),
            SuggestedActionCode = OptionalText(
                suggestedActionCode,
                100,
                nameof(suggestedActionCode)),
            Status = SpaceIssueStatus.Open,
        };
        issue.SetTenant(tenantId);
        return issue;
    }

    public void AcknowledgeWarning(
        Guid actorId,
        string reason,
        DateTime nowUtc)
    {
        RequireOpen();
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Severity != SpaceIssueSeverity.Warning)
            throw new SpaceJobStateException(
                "Only Warning issues can be acknowledged.");
        if (actorId == Guid.Empty)
            throw new ArgumentException("Acknowledging actor is required.", nameof(actorId));

        AcknowledgedBy = actorId;
        AcknowledgedAtUtc = nowUtc;
        AcknowledgementReason = RequireText(reason, 1000, nameof(reason));
        Status = SpaceIssueStatus.Acknowledged;
    }

    public void Resolve(Guid commandBatchId)
    {
        RequireOpen();
        if (commandBatchId == Guid.Empty)
            throw new ArgumentException(
                "Resolution command batch is required.",
                nameof(commandBatchId));

        ResolutionCommandBatchId = commandBatchId;
        Status = SpaceIssueStatus.Resolved;
    }

    private void RequireOpen()
    {
        if (Status != SpaceIssueStatus.Open)
            throw new SpaceJobStateException("The Issue is already closed.");
    }

    private static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
            throw new ArgumentException("Message arguments are required and too large.", parameterName);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Message arguments JSON is invalid.", parameterName, exception);
        }
        return value;
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, maxLength, parameterName);

    private static void EnsureOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ID cannot be empty.", parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
