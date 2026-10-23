using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.ProcessCostOwnerValuesV1;

namespace CP6.Core.Services.Mes;

public partial class WorkCenterService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static WorkCenterOwnerV1 Project(WorkCenter row) => new()
    { Id = row.Id, RowVersion = row.RowVersion, WgCd = row.WgCd, WgName = row.WgName,
        DailyCapacityHours = Format(row.DailyCapacityHours), Enable = row.Enable };
    private static IQueryable<WorkCenter> Filter(IQueryable<WorkCenter> rows, string? keyword) => string.IsNullOrWhiteSpace(keyword)
        ? rows : rows.Where(x => x.WgCd.Contains(keyword) || x.WgName != null && x.WgName.Contains(keyword));
    public Task<List<WorkCenter>> ListAsync(string? keyword) => !_db.Database.IsNpgsql() ? ListCoreAsync(keyword) :
        Owner.ExecuteAsync(OperationsOwnerV1.WorkCenter, "query", false,
            (a, ct) => Filter(a.WorkCenters(), keyword).OrderBy(x => x.WgCd).ToListAsync(ct));
    public Task<WorkCenter?> GetAsync(string code) => !_db.Database.IsNpgsql() ? GetCoreAsync(code) :
        Owner.ExecuteAsync<WorkCenter?>(OperationsOwnerV1.WorkCenter, "query", false, async (a, ct) =>
            await a.WorkCenters().SingleOrDefaultAsync(x => x.WgCd == code, ct) ?? throw Error("OBJECT_FORBIDDEN", 403));
    public async Task<List<WorkCenterOwnerV1>> ListOwnerAsync(string? filter) => (await ListAsync(filter)).Select(Project).ToList();
    public async Task<WorkCenterOwnerV1?> GetOwnerAsync(string code) => await GetAsync(code) is { } row ? Project(row) : null;
    public Task UpsertAsync(WorkCenter dto, string? user) => !_db.Database.IsNpgsql() ? UpsertCoreAsync(dto, user) : SaveOwnerAsync(Project(dto));
    public Task DeleteAsync(string code, string? user) => !_db.Database.IsNpgsql() ? DeleteCoreAsync(code, user) :
        Owner.ExecuteAsync<bool>(OperationsOwnerV1.WorkCenter, "del", true, (_, _) => throw Error("ROW_VERSION_REQUIRED", 409));

    public Task SaveOwnerAsync(WorkCenterOwnerV1 input)
    {
        var dto = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.WorkCenter, "edit", true, async (a, ct) =>
        {
            Code(dto.WgCd);
            if (dto.WgName?.Length > 100) throw Error("WORK_CENTER_NAME_INVALID", 400);
            var capacity = dto.DailyCapacityHours == null ? (decimal?)null : Parse(dto.DailyCapacityHours);
            WorkCenter row;
            if (dto.Id is { } id && id != Guid.Empty)
            {
                await a.RequireWorkCenter(id, ct);
                row = await _db.WorkCenters.SingleAsync(x => x.Id == id, ct);
                if (row.WgCd != dto.WgCd) throw Error("OBJECT_IDENTITY_CONFLICT", 409);
                Version(dto.RowVersion, row.RowVersion);
                row.Modifier = a.Actor; row.ModifyDate = DateTime.Now;
            }
            else
            {
                if (dto.RowVersion is { Length: > 0 }) throw Error("OBJECT_IDENTITY_CONFLICT", 409);
                // Deleted keys cannot be reused to silently re-parent historical rate rows.
                if (await _db.WorkCenters.AnyAsync(x => x.WgCd == dto.WgCd, ct)) throw Error("OBJECT_IDENTITY_CONFLICT", 409);
                row = new() { WgCd = dto.WgCd, Creator = a.Actor, CreateDate = DateTime.Now };
                _db.WorkCenters.Add(row);
            }
            row.WgName = dto.WgName; row.DailyCapacityHours = capacity; row.Enable = dto.Enable;
            await _db.SaveChangesAsync(ct);
            a.AtCommit(t => a.RequireWorkCenter(row.Id, t)); return true;
        });
    }
    public Task DeleteOwnerAsync(Guid id, byte[]? version)
    {
        var expected = version?.ToArray();
        return Owner.ExecuteAsync(OperationsOwnerV1.WorkCenter, "del", true, async (a, ct) =>
        {
            await a.RequireWorkCenter(id, ct);
            var row = await _db.WorkCenters.SingleAsync(x => x.Id == id, ct); Version(expected, row.RowVersion);
            row.IsDeleted = true; row.Modifier = a.Actor; row.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(ct); a.AtCommit(t => a.RequireWorkCenter(id, t, true)); return true;
        });
    }
}
