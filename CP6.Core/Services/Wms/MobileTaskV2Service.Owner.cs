using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class MobileTaskV2Service
{
    public Task<MobileTaskCapabilitiesV1> GetCapabilitiesAsync(CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? Task.FromResult(new MobileTaskCapabilitiesV1(true, "LEGACY"))
        : Owner.ExecuteAsync(OperationsOwnerV1.Mobile, "view", false, (_, _) =>
            Task.FromResult(new MobileTaskCapabilitiesV1(false, "MOBILE_OWNER_ADOPTION_UNPROVEN")), ct);

    public Task<PagedResult<MobileTaskV2Dto>> GetTasksAsync(MobileTaskV2Query query, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return GetTasksCoreAsync(query, ct);
        var frozen = OperationsOwnerV1.Freeze(query);
        frozen.PageSize = Math.Clamp(frozen.PageSize, 1, 200);
        frozen.Page = Math.Clamp(frozen.Page, 1, int.MaxValue / frozen.PageSize);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mobile, "view", false, (access, token) =>
        {
            access.RequireMobileAssigneeFilter(frozen.AssignedTo);
            return GetTasksCoreAsync(frozen, token, access.MobileTasks(), r => access.ProjectMobile(r, list: true));
        }, ct);
    }
    public Task<MobileTaskV2Dto?> GetAsync(string taskNo, CancellationToken ct = default) => !_db.Database.IsNpgsql() ? GetCoreAsync(taskNo, ct)
        : Owner.ExecuteAsync<MobileTaskV2Dto?>(OperationsOwnerV1.Mobile, "view", false, async (access, token) =>
        {
            var row = await access.MobileTasks().SingleOrDefaultAsync(t => t.MobileTaskNo == taskNo && t.ContractVersion == 2, token);
            if (row == null) return null;
            access.ProjectMobile(row, list: false); return Map(row);
        }, ct);

    public Task<IReadOnlyList<MobileTaskEventDto>> GetEventsAsync(string taskNo, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? GetEventsCoreAsync(taskNo, ct)
        : Owner.ExecuteAsync<IReadOnlyList<MobileTaskEventDto>>(OperationsOwnerV1.Mobile, "view", false, async (access, token) =>
        {
            OperationsAccessV1.RequireLegacyMoveKind(await access.RequireMobileTask(taskNo, 2, token));
            // Task visibility alone does not authorize arbitrary JSON, actors or device identities.
            return await _db.MobileTaskEvents.AsNoTracking().Where(e => e.TaskNo == taskNo).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                .Select(e => new MobileTaskEventDto { TaskNo = e.TaskNo, EventType = e.EventType, OperationId = e.OperationId,
                    ExecutionVersion = e.ExecutionVersion, OccurredAt = e.OccurredAt }).ToArrayAsync(token);
        }, ct);

    public Task<TaskScanProfileDto> GetScanProfileAsync(string taskNo, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? GetScanProfileCoreAsync(taskNo, ct) : RejectMobile<TaskScanProfileDto>("view", taskNo, ct);

    public Task<IReadOnlyList<MobileTaskV2Dto>> GetSourceTasksAsync(string sourceType, string sourceNo, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? GetSourceTasksCoreAsync(sourceType, sourceNo, ct)
        // Filtering by a hidden source is itself a disclosure. The old display-number
        // link does not prove current source Owner authority or a complete source family.
        : RejectMobile<IReadOnlyList<MobileTaskV2Dto>>("view", null, ct);

    public Task<TaskAnalyticsDto> GetAnalyticsAsync(TaskAnalyticsQuery query, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return GetAnalyticsCoreAsync(query, ct);
        var frozen = OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mobile, "analytics", false, async (access, token) =>
        {
            access.RequireMobileAssigneeFilter(frozen.AssignedTo);
            var allowed = access.MobileTasks();
            if (await allowed.AnyAsync(t => t.ContractVersion == 2 && t.TaskType != MobileTaskType.Move, token))
                throw new OrderPriceCorrectionException("MOBILE_KIND_READ_UNPROVEN", 503);
            return await GetAnalyticsCoreAsync(frozen, token, allowed);
        }, ct);
    }

    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private Task<T> RejectMobile<T>(string action, string? number, CancellationToken ct = default) =>
        Owner.ExecuteAsync<T>(OperationsOwnerV1.Mobile, action, false, async (access, token) =>
        {
            if (number != null) await access.RequireMobileTask(number, 2, token);
            throw new OrderPriceCorrectionException("MOBILE_OWNER_ADOPTION_UNPROVEN", 503);
        }, ct);

    public Task<MobileTaskV2Dto> CreateAsync(CreateMoveTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CreateCoreAsync(request, userName, ct) : RejectMobile<MobileTaskV2Dto>("add", null, ct);
    public Task<MobileTaskV2Dto> SynchronizePendingSourceTaskAsync(string taskNo, CreateMoveTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? SynchronizePendingSourceTaskCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("add", taskNo, ct);
    public Task<IReadOnlyList<MobileTaskV2Dto>> CancelPendingSourceTasksAsync(string sourceType, string sourceNo, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CancelPendingSourceTasksCoreAsync(sourceType, sourceNo, userName, ct) : RejectMobile<IReadOnlyList<MobileTaskV2Dto>>("cancel", null, ct);
    public Task<MobileTaskV2Dto> AssignAsync(string taskNo, AssignTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? AssignCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("assign", taskNo, ct);
    public Task<MobileTaskV2Dto> ClaimAsync(string taskNo, ClaimTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ClaimCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("claim", taskNo, ct);
    public Task<MobileTaskV2Dto> StartAsync(string taskNo, StartTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? StartCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("start", taskNo, ct);
    public Task<MobileTaskV2Dto> PauseAsync(string taskNo, PauseTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? PauseCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("pause", taskNo, ct);
    public Task<MobileTaskV2Dto> ReleaseAsync(string taskNo, ReleaseTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ReleaseCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("release", taskNo, ct);
    public Task<MobileTaskV2Dto> TakeoverAsync(string taskNo, TakeoverTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? TakeoverCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("takeover", taskNo, ct);
    public Task<MobileTaskV2Dto> RaiseExceptionAsync(string taskNo, RaiseTaskExceptionRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? RaiseExceptionCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("exception", taskNo, ct);
    public Task<MobileTaskV2Dto> ResolveExceptionAsync(string taskNo, ResolveTaskExceptionRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ResolveExceptionCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("exception", taskNo, ct);
    public Task<ScanResult> ScanAsync(string taskNo, ScanCommand request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ScanCoreAsync(taskNo, request, userName, ct) : RejectMobile<ScanResult>("scan", taskNo, ct);
    public Task<MobileTaskV2Dto> CompleteAsync(string taskNo, CompleteMoveV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CompleteCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("complete", taskNo, ct);
    public Task<MobileTaskV2Dto> CancelAsync(string taskNo, CancelTaskV2Request request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CancelCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV2Dto>("cancel", taskNo, ct);
}
