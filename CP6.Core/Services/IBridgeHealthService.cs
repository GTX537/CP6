using CP6.Entity.DTOs;

namespace CP6.Core.Services;

public interface IBridgeHealthService
{
    Task<BridgeHealthMetricsDto> GetMetricsAsync(DateTime? nowUtc = null, CancellationToken ct = default);

    Task<bool> CompensateAsync(Guid eventId, string? userName = null, CancellationToken ct = default);
}
