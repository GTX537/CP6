using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Integration;

public static class SpaceDataSourceErrors
{
    public const string Unavailable = "SPACE_DATA_SOURCE_UNAVAILABLE";
}

/// <summary>
/// Identifies the trust state behind a Space runtime query adapter.
/// Simulator implementations use Simulated; an unconfigured adapter must use
/// Unavailable instead of returning an unlabelled empty result.
/// </summary>
public interface ISpaceDataSourceDescriptor
{
    SpaceDataSourceKind DataSourceKind { get; }

    string DataSourceId { get; }
}

public static class SpaceDataSourceDescriptorExtensions
{
    public static SpaceDataSourceDto CaptureSource(
        this ISpaceDataSourceDescriptor descriptor,
        DateTimeOffset? observedAtUtc = null) =>
        SpaceDataSourceDto.Capture(
            descriptor.DataSourceKind,
            descriptor.DataSourceId,
            observedAtUtc);
}
