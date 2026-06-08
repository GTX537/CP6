using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class MaterialShortageService : IMaterialShortageService
{
    private readonly CP6Context _db;

    public MaterialShortageService(CP6Context db)
    {
        _db = db;
    }

    public async Task<MaterialShortage> CreateAsync(MaterialShortage entity)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }
        if (string.IsNullOrWhiteSpace(entity.Status))
        {
            entity.Status = MaterialShortageStatus.Open;
        }
        if (entity.DetectedAt == default)
        {
            entity.DetectedAt = DateTime.UtcNow;
        }
        if (entity.CreateDate == default)
        {
            entity.CreateDate = DateTime.Now;
        }

        _db.MaterialShortages.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task<PagedResultDto<MaterialShortage>> SearchAsync(MaterialShortageQuery query)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 50 : query.PageSize;
        var dbQuery = _db.MaterialShortages.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            dbQuery = dbQuery.Where(x => x.Status == query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.WorkOrderNo))
        {
            dbQuery = dbQuery.Where(x => x.WorkOrderNo == query.WorkOrderNo);
        }

        var total = await dbQuery.CountAsync();
        var items = await dbQuery
            .OrderByDescending(x => x.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<MaterialShortage>
        {
            Total = total,
            PageIndex = page,
            PageSize = pageSize,
            Items = items,
        };
    }

    public async Task<MaterialShortage> ResolveAsync(Guid id, string? remark, string? userName)
        => await TransitionAsync(id, MaterialShortageStatus.Resolved, remark, userName);

    public async Task<MaterialShortage> DismissAsync(Guid id, string? remark, string? userName)
        => await TransitionAsync(id, MaterialShortageStatus.Dismissed, remark, userName);

    private async Task<MaterialShortage> TransitionAsync(Guid id, string status, string? remark, string? userName)
    {
        var entity = await _db.MaterialShortages
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (entity.Status != MaterialShortageStatus.Open)
        {
            throw new InvalidOperationException("WM-MSG-SHORTAGE-409");
        }

        entity.Status = status;
        entity.Remark = remark;
        entity.ResolvedAt = DateTime.UtcNow;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return entity;
    }
}
