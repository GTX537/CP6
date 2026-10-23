using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class MobileService
{
    public Task<List<MobileTask>> GetTasksAsync(MobileTaskQuery q)
    {
        if (!_db.Database.IsNpgsql()) return GetTasksCoreAsync(q);
        var frozen = OperationsOwnerV1.Freeze(q);
        frozen.PageSize = Math.Clamp(frozen.PageSize, 1, 200);
        frozen.Page = Math.Clamp(frozen.Page, 1, int.MaxValue / frozen.PageSize);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mobile, "view", false, async (access, ct) =>
        {
            access.RequireMobileAssigneeFilter(frozen.AssignedTo);
            var allowed = access.MobileTasks();
            // A COUNT book quantity cannot be projected into this old non-nullable MOVE DTO.
            // Explicitly unavailable is different from an empty collection or a fabricated zero.
            if (!string.IsNullOrWhiteSpace(frozen.TaskType) && frozen.TaskType != MobileTaskType.Move
                || string.IsNullOrWhiteSpace(frozen.TaskType) && await allowed.AnyAsync(t => t.TaskType != MobileTaskType.Move, ct))
                throw new OrderPriceCorrectionException("MOBILE_KIND_READ_UNPROVEN", 503);
            var rows = await GetTasksCoreAsync(frozen, allowed, ct);
            foreach (var row in rows) access.ProjectMobile(row, list: true);
            return rows;
        });
    }
    public Task<MobileTask?> GetAsync(string mobileTaskNo) => !_db.Database.IsNpgsql() ? GetCoreAsync(mobileTaskNo)
        : Owner.ExecuteAsync(OperationsOwnerV1.Mobile, "view", false, async (access, ct) =>
        {
            var row = await access.MobileTasks().SingleOrDefaultAsync(t => t.MobileTaskNo == mobileTaskNo, ct);
            if (row != null) access.ProjectMobile(row, list: false);
            return row;
        });

    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private Task<T> RejectMobile<T>(string action, string? number, CancellationToken ct = default) =>
        Owner.ExecuteAsync<T>(OperationsOwnerV1.Mobile, action, false, async (access, token) =>
        {
            if (number != null) await access.RequireMobileTask(number, null, token);
            throw new OrderPriceCorrectionException("MOBILE_OWNER_ADOPTION_UNPROVEN", 503);
        }, ct);

    public Task<string> CreateAsync(MobileTaskDto dto, string? userName) => !_db.Database.IsNpgsql()
        ? CreateCoreAsync(dto, userName) : RejectMobile<string>("add", null);
    public Task StartAsync(string mobileTaskNo, string? userName) => !_db.Database.IsNpgsql()
        ? StartCoreAsync(mobileTaskNo, userName) : RejectMobile<bool>("start", mobileTaskNo);
    public Task<MobileScanResult> ScanAsync(MobileScanRequest req) => !_db.Database.IsNpgsql()
        ? ScanCoreAsync(req) : RejectMobile<MobileScanResult>("scan", req.TaskNo);
    public Task CompleteAsync(string mobileTaskNo, MobileCompleteRequest req, string? userName) => !_db.Database.IsNpgsql()
        ? CompleteCoreAsync(mobileTaskNo, req, userName) : RejectMobile<bool>("complete", mobileTaskNo);
    public Task CancelAsync(string mobileTaskNo, string? userName) => !_db.Database.IsNpgsql()
        ? CancelCoreAsync(mobileTaskNo, userName) : RejectMobile<bool>("cancel", mobileTaskNo);
}
