using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceJobStep : SpaceTenantEntity
{
    private SpaceJobStep()
    {
    }

    public Guid AttemptId { get; private set; }
    public int StepNo { get; private set; }
    public string StepCode { get; private set; } = string.Empty;
    public SpaceJobStepStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? CheckpointJson { get; private set; }
    public string? OutputHash { get; private set; }

    public static SpaceJobStep Start(
        Guid tenantId,
        Guid attemptId,
        int stepNo,
        string stepCode,
        DateTime startedAtUtc)
    {
        if (attemptId == Guid.Empty)
            throw new ArgumentException("Attempt is required.", nameof(attemptId));
        if (stepNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepNo));
        RequireUtc(startedAtUtc, nameof(startedAtUtc));

        var step = new SpaceJobStep
        {
            AttemptId = attemptId,
            StepNo = stepNo,
            StepCode = RequireText(stepCode, 100, nameof(stepCode)),
            Status = SpaceJobStepStatus.Running,
            StartedAtUtc = startedAtUtc,
        };
        step.SetTenant(tenantId);
        return step;
    }

    public static SpaceJobStep Reuse(
        Guid tenantId,
        Guid attemptId,
        int stepNo,
        string stepCode,
        string checkpointJson,
        string outputHash,
        DateTime nowUtc)
    {
        var step = Start(tenantId, attemptId, stepNo, stepCode, nowUtc);
        step.Status = SpaceJobStepStatus.Reused;
        step.CheckpointJson = RequireJson(checkpointJson, nameof(checkpointJson));
        step.OutputHash = RequireHash(outputHash);
        step.FinishedAtUtc = nowUtc;
        return step;
    }

    public void Complete(
        string checkpointJson,
        string outputHash,
        DateTime finishedAtUtc)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        CheckpointJson = RequireJson(checkpointJson, nameof(checkpointJson));
        OutputHash = RequireHash(outputHash);
        Status = SpaceJobStepStatus.Succeeded;
        FinishedAtUtc = finishedAtUtc;
    }

    public void Fail(DateTime finishedAtUtc)
    {
        RequireRunning();
        RequireUtc(finishedAtUtc, nameof(finishedAtUtc));
        Status = SpaceJobStepStatus.Failed;
        FinishedAtUtc = finishedAtUtc;
    }

    private void RequireRunning()
    {
        if (Status != SpaceJobStepStatus.Running)
            throw new SpaceJobStateException("The Job step is already terminal.");
    }

    private static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 65_536)
            throw new ArgumentException("Checkpoint JSON is required and is too large.", parameterName);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Checkpoint JSON is invalid.", parameterName, exception);
        }
        return value;
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

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
