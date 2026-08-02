using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed class SpacePersonnelRuntimeOptions
{
    public TimeSpan CurrentFreshness { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan TrajectoryRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan MaximumTrajectoryWindow { get; init; } = TimeSpan.FromHours(24);
    public int DefaultPageSize { get; init; } = 100;
    public int MaximumPageSize { get; init; } = 500;

    public void Validate()
    {
        if (CurrentFreshness <= TimeSpan.Zero)
            throw new InvalidOperationException("Personnel freshness must be positive.");
        if (TrajectoryRetention <= TimeSpan.Zero)
            throw new InvalidOperationException("Personnel retention must be positive.");
        if (MaximumTrajectoryWindow <= TimeSpan.Zero ||
            MaximumTrajectoryWindow > TrajectoryRetention)
        {
            throw new InvalidOperationException(
                "The personnel trajectory window must be positive and no longer than retention.");
        }
        if (DefaultPageSize is < 1 || DefaultPageSize > MaximumPageSize ||
            MaximumPageSize is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Personnel page sizes are invalid.");
        }
    }
}

public interface ISpacePersonnelRuntimeService
{
    Task<SpacePersonnelCurrentPageDto> GetCurrentAsync(
        Guid siteId,
        string? sourceKind,
        string? workState,
        Guid? floorLogicalId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<SpacePersonnelTrajectoryResponse> GetTrajectoryAsync(
        Guid siteId,
        string sourceId,
        string personExternalId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
}
