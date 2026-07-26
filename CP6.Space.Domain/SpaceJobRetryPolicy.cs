namespace CP6.Space.Domain;

public sealed record SpaceJobRetryDecision(
    SpaceJobStatus NextStatus,
    DateTime? NextAttemptAtUtc)
{
    public bool WillRetry => NextStatus == SpaceJobStatus.Queued;
}

public static class SpaceJobRetryPolicy
{
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(15);

    public static SpaceJobRetryDecision DecideAutomatic(
        SpaceJobFailureKind failureKind,
        int attemptCount,
        int maxAttempts,
        DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (attemptCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        if (maxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var automaticallyRetryable =
            failureKind is SpaceJobFailureKind.Transient or SpaceJobFailureKind.Bug;
        if (!automaticallyRetryable)
            return new SpaceJobRetryDecision(SpaceJobStatus.Failed, null);
        if (attemptCount >= maxAttempts)
            return new SpaceJobRetryDecision(SpaceJobStatus.DeadLetter, null);

        var exponent = Math.Min(attemptCount - 1, 16);
        var seconds = Math.Min(
            5d * Math.Pow(2, exponent),
            MaximumBackoff.TotalSeconds);
        return new SpaceJobRetryDecision(
            SpaceJobStatus.Queued,
            nowUtc.AddSeconds(seconds));
    }

    public static void EnsureManualRetryAllowed(
        SpaceJob original,
        string newBusinessKey)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!original.IsTerminal)
            throw new SpaceJobNotRetryableException(
                "Only a terminal Job can create an explicit retry.");
        if (original.Status == SpaceJobStatus.Succeeded)
            throw new SpaceJobNotRetryableException(
                "A succeeded Job cannot be retried.");
        if (original.LastFailureKind == SpaceJobFailureKind.Security)
            throw new SpaceJobNotRetryableException(
                "Security failures cannot be retried.");
        if (original.LastFailureKind == SpaceJobFailureKind.Input &&
            string.Equals(
                original.BusinessKey,
                newBusinessKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceJobNotRetryableException(
                "Input failures require changed input and a new business key.");
        }
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", nameof(value));
    }
}
