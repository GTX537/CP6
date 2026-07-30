using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceJobAttempt : SpaceTenantEntity
{
    private SpaceJobAttempt()
    {
    }

    public Guid JobId { get; private set; }
    public int AttemptNo { get; private set; }
    public string WorkerId { get; private set; } = string.Empty;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public SpaceJobAttemptOutcome Outcome { get; private set; }
    public string InputHash { get; private set; } = string.Empty;
    public string ProcessorVersion { get; private set; } = string.Empty;
    public string? ResourceUsageJson { get; private set; }
    public SpaceJobFailureKind? FailureKind { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? SanitizedError { get; private set; }
    public Guid? DiagnosticArtifactId { get; private set; }

    internal static SpaceJobAttempt Start(
        Guid tenantId,
        Guid jobId,
        int attemptNo,
        string workerId,
        string inputHash,
        string processorVersion,
        DateTime startedAtUtc)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("Job is required.", nameof(jobId));
        if (attemptNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(attemptNo));
        RequireUtc(startedAtUtc, nameof(startedAtUtc));

        var attempt = new SpaceJobAttempt
        {
            JobId = jobId,
            AttemptNo = attemptNo,
            WorkerId = RequireText(workerId, 200, nameof(workerId)),
            StartedAtUtc = startedAtUtc,
            Outcome = SpaceJobAttemptOutcome.Running,
            InputHash = RequireHash(inputHash),
            ProcessorVersion = RequireText(
                processorVersion,
                100,
                nameof(processorVersion)),
        };
        attempt.SetTenant(tenantId);
        return attempt;
    }

    public void Succeed(
        DateTime finishedAtUtc,
        string? resourceUsageJson = null)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        Outcome = SpaceJobAttemptOutcome.Succeeded;
        FinishedAtUtc = finishedAtUtc;
        ResourceUsageJson = OptionalJson(resourceUsageJson, nameof(resourceUsageJson));
    }

    public void Fail(
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError,
        DateTime finishedAtUtc,
        Guid? diagnosticArtifactId = null,
        string? resourceUsageJson = null)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        if (diagnosticArtifactId == Guid.Empty)
            throw new ArgumentException(
                "Diagnostic Artifact ID cannot be empty.",
                nameof(diagnosticArtifactId));

        Outcome = SpaceJobAttemptOutcome.Failed;
        FailureKind = failureKind;
        ErrorCode = RequireText(errorCode, 100, nameof(errorCode));
        SanitizedError = RequireText(
            sanitizedError,
            1000,
            nameof(sanitizedError));
        DiagnosticArtifactId = diagnosticArtifactId;
        ResourceUsageJson = OptionalJson(resourceUsageJson, nameof(resourceUsageJson));
        FinishedAtUtc = finishedAtUtc;
    }

    public void Abandon(DateTime finishedAtUtc, string reason)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        Outcome = SpaceJobAttemptOutcome.Abandoned;
        FailureKind = SpaceJobFailureKind.Transient;
        ErrorCode = "SPACE_JOB_LEASE_LOST";
        SanitizedError = RequireText(reason, 1000, nameof(reason));
        FinishedAtUtc = finishedAtUtc;
    }

    public void Cancel(DateTime finishedAtUtc)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        Outcome = SpaceJobAttemptOutcome.Cancelled;
        FinishedAtUtc = finishedAtUtc;
    }

    private void RequireRunning()
    {
        if (Outcome != SpaceJobAttemptOutcome.Running)
            throw new SpaceJobStateException("The Job attempt is already terminal.");
    }

    private static string RequireHash(string value)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", nameof(value));
        return value.ToLowerInvariant();
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

    private static string? OptionalJson(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Length > 65_536)
            throw new ArgumentException("JSON is too large.", parameterName);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON is invalid.", parameterName, exception);
        }
        return value;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
