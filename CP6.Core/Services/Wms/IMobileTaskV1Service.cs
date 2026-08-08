namespace CP6.Core.Services.Wms;

public interface IMobileTaskV1Service
{
    Task<PagedResult<MobileTaskV1Dto>> GetTasksAsync(MobileTaskV1Query query, CancellationToken ct = default);
    Task<MobileTaskV1Dto?> GetAsync(string taskNo, CancellationToken ct = default);
    Task<MobileTaskV1Dto> CreateAsync(CreateMoveTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV1Dto> AssignAsync(string taskNo, AssignTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV1Dto> ClaimAsync(string taskNo, ClaimTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV1Dto> StartAsync(string taskNo, StartTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileScanResult> ScanAsync(string taskNo, MobileScanRequest request, CancellationToken ct = default);
    Task<MobileTaskV1Dto> CompleteAsync(string taskNo, CompleteMoveRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV1Dto> CancelAsync(string taskNo, CancelTaskRequest request, string? userName, CancellationToken ct = default);
}
