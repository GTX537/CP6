using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space.Observability;

public interface ISpaceAuditQueryService
{
    Task<SpaceAuditPageDto> QueryAsync(
        SpaceAuditQueryDto query,
        CancellationToken ct = default);

    Task<IReadOnlyList<SpaceAuditTimelineItemDto>> GetTimelineAsync(
        Guid correlationId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SpacePublishEventDto>> GetPublishEventsAsync(
        int page,
        int pageSize,
        CancellationToken ct = default);
}
