namespace CP6.Core.Options;

/// <summary>
/// IntegrationEvent retry worker configuration.
/// </summary>
public class IntegrationEventOptions
{
    /// <summary>Maximum attempts before an event is moved to dead letter.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Backoff in seconds for attempt N (zero-indexed). Default: [60, 120, 240, 480, 960]</summary>
    public int[] BackoffSeconds { get; set; } = new[] { 60, 120, 240, 480, 960 };

    /// <summary>Worker polling interval in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>If false, the worker is a no-op.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Space retry processing lease duration in seconds.</summary>
    public int SpaceRetryLeaseSeconds { get; set; } = 900;

    /// <summary>Space retry lease heartbeat interval in seconds.</summary>
    public int SpaceRetryHeartbeatSeconds { get; set; } = 60;

    /// <summary>Space dead-letter notification claim duration in seconds.</summary>
    public int SpaceDeadLetterNotificationLeaseSeconds { get; set; } = 300;

    public void ValidateSpaceRetryLease()
    {
        if (SpaceRetryLeaseSeconds <= 0 ||
            SpaceRetryHeartbeatSeconds <= 0 ||
            (long)SpaceRetryHeartbeatSeconds * 3 >
                SpaceRetryLeaseSeconds ||
            SpaceDeadLetterNotificationLeaseSeconds <= 0)
        {
            throw new InvalidOperationException(
                "SPACE_RETRY_LEASE_OPTIONS_INVALID");
        }
    }

    /// <summary>
    /// Gets the configured backoff in seconds for the current attempt count.
    /// </summary>
    public int GetBackoffSeconds(int attempts)
    {
        if (BackoffSeconds.Length == 0)
        {
            return 60;
        }

        if (attempts <= 0)
        {
            return BackoffSeconds[0];
        }

        var idx = Math.Min(attempts - 1, BackoffSeconds.Length - 1);
        return BackoffSeconds[idx];
    }
}
