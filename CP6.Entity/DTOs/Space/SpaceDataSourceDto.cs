using System.Text.Json.Serialization;

namespace CP6.Entity.DTOs.Space;

/// <summary>
/// Trust state of runtime data shown by Space. Serialized as a stable string
/// so clients never have to infer availability from an empty collection.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceDataSourceKind
{
    Real = 0,
    Simulated = 1,
    Unavailable = 2,
}

/// <summary>
/// Mandatory provenance metadata for Space runtime data.
/// This DTO is part of the response contract and must not be field-filtered.
/// </summary>
public sealed class SpaceDataSourceDto
{
    public SpaceDataSourceKind Kind { get; init; }

    public string DataSourceId { get; init; } = "";

    public DateTimeOffset ObservedAtUtc { get; init; }

    public bool IsSimulated => Kind == SpaceDataSourceKind.Simulated;

    public bool IsAvailable => Kind != SpaceDataSourceKind.Unavailable;

    public static SpaceDataSourceDto Capture(
        SpaceDataSourceKind kind,
        string dataSourceId,
        DateTimeOffset? observedAtUtc = null)
    {
        var observed = observedAtUtc ?? DateTimeOffset.UtcNow;
        return new SpaceDataSourceDto
        {
            Kind = kind,
            DataSourceId = dataSourceId,
            ObservedAtUtc = observed.ToUniversalTime(),
        };
    }

    public static SpaceDataSourceDto Runtime(DateTimeOffset? observedAtUtc = null) =>
        Capture(SpaceDataSourceKind.Real, "CP6_SPACE_RUNTIME", observedAtUtc);
}
