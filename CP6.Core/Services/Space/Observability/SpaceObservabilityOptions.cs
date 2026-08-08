namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceObservabilityOptions
{
    public const string SectionName = "SpaceObservability";

    public bool AuditQueryEnabled { get; set; } = true;

    public bool MetricsEnabled { get; set; } = true;

    /// <summary>
    /// Windows or IANA time-zone identifier used only to normalize historical
    /// SPACE integration rows whose OccurredAtUtc is null. It is intentionally
    /// unset by default: startup fails closed when legacy rows exist and this
    /// value is missing or invalid.
    /// </summary>
    public string? LegacyIntegrationEventTimeZoneId { get; set; }
}
