using CP6.Entity.DTOs;

namespace CP6.Core.Services;

public interface IBackorderService
{
    Task<List<BackorderQueueItemDto>> GetQueueAsync(BackorderQueueQuery query, CancellationToken ct = default);

    Task<BackorderActionResultDto> CloseRemainingAsync(
        string webOrderNo,
        int detailNo,
        BackorderActionRequest request,
        string? userName = null,
        CancellationToken ct = default);

    Task<BackorderActionResultDto> SplitToNewOrderAsync(
        string webOrderNo,
        int detailNo,
        BackorderActionRequest request,
        string? userName = null,
        CancellationToken ct = default);
}
