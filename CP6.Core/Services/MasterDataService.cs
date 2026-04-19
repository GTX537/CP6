using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

public class MasterDataService : IMasterDataService
{
    private readonly CP6Context _db;
    public MasterDataService(CP6Context db) => _db = db;

    public Task<List<MasterBase>> GetBasesAsync() =>
        _db.MasterBases.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.BaseCd)
            .ToListAsync();

    public Task<List<MasterStaff>> GetStaffsAsync(string? baseCd)
    {
        var q = _db.MasterStaffs.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(baseCd))
            q = q.Where(x => x.BaseCd == baseCd);
        return q.OrderBy(x => x.SortOrder).ThenBy(x => x.StaffCd).ToListAsync();
    }

    public Task<List<MasterGenericCode>> GetGenericCodesAsync(string groupCode) =>
        _db.MasterGenericCodes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.GroupCode == groupCode)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .ToListAsync();
}
