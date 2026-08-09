namespace CP6.Core.Services.Wms;

public interface IMobileTaskV2Service
{
    Task<PagedResult<MobileTaskV2Dto>> GetTasksAsync(MobileTaskV2Query query, CancellationToken ct = default);
    Task<MobileTaskV2Dto?> GetAsync(string taskNo, CancellationToken ct = default);
    Task<IReadOnlyList<MobileTaskEventDto>> GetEventsAsync(string taskNo, CancellationToken ct = default);
    Task<TaskScanProfileDto> GetScanProfileAsync(string taskNo, CancellationToken ct = default);
    Task<MobileTaskV2Dto> CreateAsync(CreateMoveTaskV2Request request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> AssignAsync(string taskNo, AssignTaskV2Request request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> ClaimAsync(string taskNo, ClaimTaskV2Request request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> StartAsync(string taskNo, StartTaskV2Request request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> PauseAsync(string taskNo, PauseTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> ReleaseAsync(string taskNo, ReleaseTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> TakeoverAsync(string taskNo, TakeoverTaskRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> RaiseExceptionAsync(string taskNo, RaiseTaskExceptionRequest request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> ResolveExceptionAsync(string taskNo, ResolveTaskExceptionRequest request, string? userName, CancellationToken ct = default);
    Task<ScanResult> ScanAsync(string taskNo, ScanCommand request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> CompleteAsync(string taskNo, CompleteMoveV2Request request, string? userName, CancellationToken ct = default);
    Task<MobileTaskV2Dto> CancelAsync(string taskNo, CancelTaskV2Request request, string? userName, CancellationToken ct = default);
    Task<TaskAnalyticsDto> GetAnalyticsAsync(TaskAnalyticsQuery query, CancellationToken ct = default);

    /// <summary>
    /// Internal source-document integration surface. These methods deliberately
    /// reuse the same scope, reservation, concurrency, audit and notification
    /// rules as commands issued through the v2 controller.
    /// </summary>
    Task<IReadOnlyList<MobileTaskV2Dto>> GetSourceTasksAsync(
        string sourceType,
        string sourceNo,
        CancellationToken ct = default);
    Task<MobileTaskV2Dto> SynchronizePendingSourceTaskAsync(
        string taskNo,
        CreateMoveTaskV2Request request,
        string? userName,
        CancellationToken ct = default);
    Task<IReadOnlyList<MobileTaskV2Dto>> CancelPendingSourceTasksAsync(
        string sourceType,
        string sourceNo,
        string? userName,
        CancellationToken ct = default);
}
