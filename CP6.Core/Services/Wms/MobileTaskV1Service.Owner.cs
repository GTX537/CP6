using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class MobileTaskV1Service
{
    public Task<PagedResult<MobileTaskV1Dto>> GetTasksAsync(MobileTaskV1Query query, CancellationToken ct = default)
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
    public Task<MobileTaskV1Dto?> GetAsync(string taskNo, CancellationToken ct = default) => !_db.Database.IsNpgsql() ? GetCoreAsync(taskNo, ct)
        : Owner.ExecuteAsync<MobileTaskV1Dto?>(OperationsOwnerV1.Mobile, "view", false, async (access, token) =>
        {
            var row = await access.MobileTasks().SingleOrDefaultAsync(t => t.MobileTaskNo == taskNo && t.TaskType == MobileTaskType.Move, token);
            if (row == null) return null;
            access.ProjectMobile(row, list: false); return Map(row);
        }, ct);

    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private Task<T> RejectMobile<T>(string action, string? number, CancellationToken ct = default) =>
        Owner.ExecuteAsync<T>(OperationsOwnerV1.Mobile, action, false, async (access, token) =>
        {
            if (number != null) await access.RequireMobileTask(number, null, token);
            throw new OrderPriceCorrectionException("MOBILE_OWNER_ADOPTION_UNPROVEN", 503);
        }, ct);

    public Task<MobileTaskV1Dto> CreateAsync(CreateMoveTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CreateCoreAsync(request, userName, ct) : RejectMobile<MobileTaskV1Dto>("add", null, ct);
    public Task<MobileTaskV1Dto> AssignAsync(string taskNo, AssignTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? AssignCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV1Dto>("assign", taskNo, ct);
    public Task<MobileTaskV1Dto> ClaimAsync(string taskNo, ClaimTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ClaimCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV1Dto>("claim", taskNo, ct);
    public Task<MobileTaskV1Dto> StartAsync(string taskNo, StartTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? StartCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV1Dto>("start", taskNo, ct);
    public Task<MobileScanResult> ScanAsync(string taskNo, MobileScanRequest request, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? ScanCoreAsync(taskNo, request, ct) : RejectMobile<MobileScanResult>("scan", taskNo, ct);
    public Task<MobileTaskV1Dto> CompleteAsync(string taskNo, CompleteMoveRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CompleteCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV1Dto>("complete", taskNo, ct);
    public Task<MobileTaskV1Dto> CancelAsync(string taskNo, CancelTaskRequest request, string? userName, CancellationToken ct = default) => !_db.Database.IsNpgsql()
        ? CancelCoreAsync(taskNo, request, userName, ct) : RejectMobile<MobileTaskV1Dto>("cancel", taskNo, ct);
}
