using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public class ProcessCostRateService : IProcessCostRateService
{
    private readonly CP6Context _db;
    public ProcessCostRateService(CP6Context db) => _db = db;

    public async Task<List<ProcessCostRate>> ListAsync(string? wgCd)
    {
        var q = _db.ProcessCostRates.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(wgCd)) q = q.Where(x => x.WgCd == wgCd);
        return await q.OrderBy(x => x.WgCd).ThenByDescending(x => x.ValidFrom).ToListAsync();
    }

    public Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate)
        => _db.ProcessCostRates.AsNoTracking()
            .Where(x => !x.IsDeleted && x.WgCd == wgCd
                        && x.ValidFrom <= onDate
                        && (x.ValidTo == null || x.ValidTo >= onDate))
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(ProcessCostRate dto, string? user)
    {
        if (dto.LaborRate < 0m || dto.OverheadRate < 0m)
            throw new InvalidOperationException("E-A2-RATE-003: 费率不可为负");
        if (dto.ValidTo is { } vt && vt < dto.ValidFrom)
            throw new InvalidOperationException("E-A2-RATE-004: 失效日早于生效日");
        var wc = await _db.WorkCenters.AnyAsync(x => x.WgCd == dto.WgCd && !x.IsDeleted);
        if (!wc) throw new InvalidOperationException("E-A2-WC-001: 工作中心不存在");

        // 期间重叠校验：[newFrom,newTo??Max] 与同 WgCd 其它行 [oldFrom,oldTo??Max] 不得重叠
        var newTo = dto.ValidTo ?? DateTime.MaxValue;
        var siblings = await _db.ProcessCostRates
            .Where(x => !x.IsDeleted && x.WgCd == dto.WgCd && x.Id != dto.Id).ToListAsync();
        foreach (var s in siblings)
        {
            var sTo = s.ValidTo ?? DateTime.MaxValue;
            if (dto.ValidFrom <= sTo && s.ValidFrom <= newTo)
                throw new InvalidOperationException("E-A2-RATE-001: 费率生效区间重叠");
        }

        var existing = dto.Id != Guid.Empty
            ? await _db.ProcessCostRates.FirstOrDefaultAsync(x => x.Id == dto.Id && !x.IsDeleted)
            : null;
        if (existing == null)
        {
            dto.Creator = user; dto.CreateDate = DateTime.Now;
            _db.ProcessCostRates.Add(dto);
        }
        else
        {
            existing.LaborRate = dto.LaborRate; existing.OverheadRate = dto.OverheadRate;
            existing.ValidFrom = dto.ValidFrom; existing.ValidTo = dto.ValidTo;
            existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id, string? user)
    {
        var row = await _db.ProcessCostRates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new InvalidOperationException("E-A2-RATE-001: 费率不存在");
        row.IsDeleted = true; row.Modifier = user; row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
