using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class WorkCenterService : IWorkCenterService
{
    private readonly CP6Context _db;
    private readonly CP6.Core.Services.Integration.OperationsOwnerV1? _owner;
    public WorkCenterService(CP6Context db, CP6.Core.Services.Integration.OperationsOwnerV1? owner = null) { _db = db; _owner = owner; }

    private async Task<List<WorkCenter>> ListCoreAsync(string? keyword)
    {
        var q = _db.WorkCenters.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(x => x.WgCd.Contains(keyword) || (x.WgName != null && x.WgName.Contains(keyword)));
        return await q.OrderBy(x => x.WgCd).ToListAsync();
    }

    private Task<WorkCenter?> GetCoreAsync(string wgCd)
        => _db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(x => x.WgCd == wgCd && !x.IsDeleted);

    private async Task UpsertCoreAsync(WorkCenter dto, string? user)
    {
        if (string.IsNullOrWhiteSpace(dto.WgCd))
            throw new InvalidOperationException("E-A2-WC-001: 工作中心CD必填");
        if (dto.DailyCapacityHours is < 0m)
            throw new InvalidOperationException("E-A2-WC-003: 日可用产能不可为负");

        var existing = await _db.WorkCenters.FirstOrDefaultAsync(x => x.WgCd == dto.WgCd && !x.IsDeleted);
        if (existing == null)
        {
            dto.Creator = user; dto.CreateDate = DateTime.Now;
            _db.WorkCenters.Add(dto);
        }
        else
        {
            existing.WgName = dto.WgName;
            existing.DailyCapacityHours = dto.DailyCapacityHours;
            existing.Enable = dto.Enable;
            existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    private async Task DeleteCoreAsync(string wgCd, string? user)
    {
        var row = await _db.WorkCenters.FirstOrDefaultAsync(x => x.WgCd == wgCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("E-A2-WC-001: 工作中心不存在");
        row.IsDeleted = true; row.Modifier = user; row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
