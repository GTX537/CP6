using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>审批人映射维护(②b)。租户隔离走全局过滤器。校验非法 → InvalidOperationException("E-WF-015")(控制器 catch 转 BizException)。</summary>
public class ApproverMapService : IApproverMapService
{
    private readonly CP6Context _db;
    public ApproverMapService(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<Wf_ApproverMap>> ListAsync(string? mapKey)
    {
        var q = _db.Wf_ApproverMaps.AsQueryable();
        if (!string.IsNullOrWhiteSpace(mapKey)) q = q.Where(m => m.MapKey == mapKey);
        return await q.OrderBy(m => m.MapKey).ThenBy(m => m.MatchValue).ThenBy(m => m.OrderNo).ToListAsync();
    }

    public async Task<IReadOnlyList<string>> DistinctKeysAsync()
        => await _db.Wf_ApproverMaps.Select(m => m.MapKey).Distinct().OrderBy(k => k).ToListAsync();

    public async Task<Wf_ApproverMap> CreateAsync(string mapKey, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo = 0)
    {
        Validate(mapKey, matchValue, approverUserId, approverRoleId);
        await AssertNoDuplicateAsync(mapKey, matchValue, approverUserId, approverRoleId, null);
        var row = new Wf_ApproverMap
        {
            Id = Guid.NewGuid(), MapKey = mapKey.Trim(), MatchValue = matchValue.Trim(),
            ApproverUserId = approverUserId, ApproverRoleId = approverRoleId, OrderNo = orderNo, Enable = true,
        };
        _db.Wf_ApproverMaps.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    public async Task UpdateAsync(Guid id, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo, bool enable)
    {
        var row = await _db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.Id == id)
                  ?? throw new InvalidOperationException("E-WF-015");
        Validate(row.MapKey, matchValue, approverUserId, approverRoleId);
        await AssertNoDuplicateAsync(row.MapKey, matchValue, approverUserId, approverRoleId, id);
        row.MatchValue = matchValue.Trim(); row.ApproverUserId = approverUserId;
        row.ApproverRoleId = approverRoleId; row.OrderNo = orderNo; row.Enable = enable;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var row = await _db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.Id == id);
        if (row is null) return;
        _db.Wf_ApproverMaps.Remove(row);
        await _db.SaveChangesAsync();
    }

    private static void Validate(string mapKey, string matchValue, Guid? uid, int? rid)
    {
        if (string.IsNullOrWhiteSpace(mapKey) || string.IsNullOrWhiteSpace(matchValue)) throw new InvalidOperationException("E-WF-015");
        if (uid is null && rid is null) throw new InvalidOperationException("E-WF-015");   // 双目标皆空
    }

    private async Task AssertNoDuplicateAsync(string mapKey, string matchValue, Guid? uid, int? rid, Guid? excludeId)
    {
        var exists = await _db.Wf_ApproverMaps.AnyAsync(m =>
            m.MapKey == mapKey && m.MatchValue == matchValue &&
            m.ApproverUserId == uid && m.ApproverRoleId == rid &&
            (excludeId == null || m.Id != excludeId));
        if (exists) throw new InvalidOperationException("E-WF-015");
    }
}
