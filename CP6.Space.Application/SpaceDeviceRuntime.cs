using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed class SpaceDeviceRuntimeOptions
{
    public TimeSpan CurrentFreshness { get; init; } = TimeSpan.FromMinutes(5);
    public int DefaultPageSize { get; init; } = 100;
    public int MaximumPageSize { get; init; } = 500;

    public void Validate()
    {
        if (CurrentFreshness <= TimeSpan.Zero)
            throw new InvalidOperationException("Device freshness must be positive.");
        if (DefaultPageSize is < 1 || DefaultPageSize > MaximumPageSize ||
            MaximumPageSize is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Device page sizes are invalid.");
        }
    }
}

public interface ISpaceDeviceRuntimeService
{
    Task<SpaceDeviceCurrentPageDto> GetCurrentAsync(
        Guid siteId,
        string? sourceKind,
        string? deviceKind,
        string? operatingState,
        Guid? floorLogicalId,
        bool? hasActiveAlarm,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
}
