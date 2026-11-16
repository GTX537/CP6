using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<WorkCenter> WorkCenters(bool includeDeleted = false)
    {
        var rows = db.WorkCenters.AsNoTracking().Where(x => includeDeleted || !x.IsDeleted);
        if (authority.Scope == 1) return rows.Where(x => x.Creator == Actor);
        if (authority.Scope == 5) return rows;
        var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "work-center");
        return rows.Where(x => ids.Contains(x.Id));
    }
    internal IQueryable<ProcessCostRate> ProcessCostRates(bool includeDeleted = false)
    {
        var parents = RateParents();
        var rows = db.ProcessCostRates.AsNoTracking().Where(x => (includeDeleted || !x.IsDeleted)
            && parents.Any(p => p.WgCd == x.WgCd));
        return authority.Scope == 1 ? rows.Where(x => x.Creator == Actor) : rows;
    }

    internal IQueryable<CostSheet> CostSheetsWithReadableRates(IQueryable<CostSheet> sheets)
    {
        // 全租户费率读取权可以查看同租户的历史快照，包括旧库没有费率来源行的记录。
        if (authority.Scope == 5) return sheets;

        // 新归集按保存的来源 UUID 读取，旧单仍按原有可证明的费率匹配。
        var rates = ProcessCostRates();
        var parents = RateParents();
        var versioned = CostCollectionBasisStoreV1.VersionedSheetIds(db);
        var sources = CostCollectionBasisStoreV1.CurrentRates(db);
        return sheets.Where(sheet => versioned.Contains(sheet.Id)
            ? !sources.Any(source => source.CostSheetId == sheet.Id
                && (!parents.Any(center => center.Id == source.WorkCenterId && center.WgCd == source.WorkCenter)
                    || !rates.Any(rate => rate.Id == source.RateId && rate.WgCd == source.WorkCenter)))
            :
            ((sheet.LaborActual == 0m && sheet.LaborStandard == 0m) || sheet.Lines.Any(line => line.Element == CostElement.Labor))
            && ((sheet.OverheadActual == 0m && sheet.OverheadStandard == 0m) || sheet.Lines.Any(line => line.Element == CostElement.Overhead))
            && !sheet.Lines.Any(line => (line.Element == CostElement.Labor || line.Element == CostElement.Overhead)
                && !rates.Any(rate => rate.WgCd == line.WgCd && rate.ValidFrom == line.RateValidFrom
                    && (line.Element == CostElement.Labor ? rate.LaborRate : rate.OverheadRate) == line.UnitPrice)));
    }

    // A rate's department comes from the actual same-tenant parent UUID binding, under the RATE grant.
    private IQueryable<WorkCenter> RateParents()
    {
        var rows = db.WorkCenters.AsNoTracking().Where(x => !x.IsDeleted);
        if (authority.Scope is 1 or 5) return rows;
        var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "work-center");
        return rows.Where(x => ids.Contains(x.Id));
    }
    internal async Task RequireRateParent(string code, CancellationToken ct)
    {
        if (!await RateParents().AnyAsync(x => x.WgCd == code, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal async Task RequireWorkCenter(Guid id, CancellationToken ct, bool includeDeleted = false)
    {
        if (!await WorkCenters(includeDeleted).AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal async Task RequireProcessCostRate(Guid id, CancellationToken ct, bool includeDeleted = false)
    {
        if (!await ProcessCostRates(includeDeleted).AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal bool RateReadable(string field) => authority.Fields.Fields.TryGetValue(field, out var grant) && grant.CanRead;
    internal bool RateWritable(string field) => authority.Fields.Fields.TryGetValue(field, out var grant) && grant.CanWrite;
    internal void RequireRateWrite(string field)
    { if (!RateWritable(field)) throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403); }
    internal void RequireUnmaskedRates()
    {
        if (!RateReadable("LaborRate") || !RateReadable("OverheadRate"))
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
    }
}
