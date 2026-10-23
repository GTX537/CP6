using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.ProcessCostOwnerValuesV1;

namespace CP6.Core.Services.Mes;

public partial class ProcessCostRateService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static IQueryable<ProcessCostRate> Filter(IQueryable<ProcessCostRate> rows, string? code) =>
        string.IsNullOrWhiteSpace(code) ? rows : rows.Where(x => x.WgCd == code);
    private static IQueryable<ProcessCostRate> Effective(IQueryable<ProcessCostRate> rows, string code, DateTime at) =>
        rows.Where(x => x.WgCd == code && x.ValidFrom <= at && (x.ValidTo == null || x.ValidTo >= at)).OrderByDescending(x => x.ValidFrom);
    private static ProcessCostRateOwnerV1 Project(ProcessCostRate row, OperationsAccessV1 a) => new()
    { Id = row.Id, RowVersion = row.RowVersion, WgCd = row.WgCd, ValidFrom = row.ValidFrom, ValidTo = row.ValidTo,
        LaborRate = a.RateReadable("LaborRate") ? Format(row.LaborRate) : null,
        OverheadRate = a.RateReadable("OverheadRate") ? Format(row.OverheadRate) : null,
        LaborRateWritable = a.RateWritable("LaborRate"), OverheadRateWritable = a.RateWritable("OverheadRate") };
    public Task<List<ProcessCostRate>> ListAsync(string? code) => !_db.Database.IsNpgsql() ? ListCoreAsync(code) :
        Owner.ExecuteAsync(OperationsOwnerV1.ProcessCostRate, "query", false, (a, ct) =>
        { a.RequireUnmaskedRates(); return Filter(a.ProcessCostRates(), code).OrderBy(x => x.WgCd).ThenByDescending(x => x.ValidFrom).ToListAsync(ct); });
    public Task<ProcessCostRate?> ResolveAsync(string code, DateTime at) => !_db.Database.IsNpgsql() ? ResolveCoreAsync(code, at) :
        Owner.ExecuteAsync(OperationsOwnerV1.ProcessCostRate, "query", false, (a, ct) =>
        { a.RequireUnmaskedRates(); return Effective(a.ProcessCostRates(), code, at).FirstOrDefaultAsync(ct); });
    public Task<List<ProcessCostRateOwnerV1>> ListOwnerAsync(string? code) =>
        Owner.ExecuteAsync(OperationsOwnerV1.ProcessCostRate, "query", false, async (a, ct) =>
            (await Filter(a.ProcessCostRates(), code).OrderBy(x => x.WgCd).ThenByDescending(x => x.ValidFrom).ToListAsync(ct)).Select(x => Project(x, a)).ToList());
    public Task<ProcessCostRateOwnerV1?> ResolveOwnerAsync(string code, DateTime at) =>
        Owner.ExecuteAsync<ProcessCostRateOwnerV1?>(OperationsOwnerV1.ProcessCostRate, "query", false, async (a, ct) =>
            await Effective(a.ProcessCostRates(), code, at).FirstOrDefaultAsync(ct) is { } row ? Project(row, a) : null);
    public Task UpsertAsync(ProcessCostRate dto, string? user) => !_db.Database.IsNpgsql() ? UpsertCoreAsync(dto, user) :
        SaveOwnerAsync(new() { Id = dto.Id, RowVersion = dto.RowVersion, WgCd = dto.WgCd, LaborRate = Format(dto.LaborRate),
            OverheadRate = Format(dto.OverheadRate), ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo });
    public Task DeleteAsync(Guid id, string? user) => !_db.Database.IsNpgsql() ? DeleteCoreAsync(id, user) :
        Owner.ExecuteAsync<bool>(OperationsOwnerV1.ProcessCostRate, "del", true, (_, _) => throw Error("ROW_VERSION_REQUIRED", 409));

    public Task SaveOwnerAsync(ProcessCostRateOwnerV1 input)
    {
        var dto = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.ProcessCostRate, "edit", true, async (a, ct) =>
        {
            Code(dto.WgCd);
            if (dto.ValidTo < dto.ValidFrom)
                throw Error("RATE_PERIOD_INVALID", 400);
            ProcessCostRate row;
            if (dto.Id is { } id && id != Guid.Empty)
            {
                await a.RequireProcessCostRate(id, ct);
                row = await _db.ProcessCostRates.SingleAsync(x => x.Id == id, ct);
                if (row.WgCd != dto.WgCd) throw Error("OBJECT_IDENTITY_CONFLICT", 409);
                Version(dto.RowVersion, row.RowVersion);
                row.Modifier = a.Actor; row.ModifyDate = DateTime.Now;
            }
            else
            {
                if (dto.RowVersion is { Length: > 0 }) throw Error("OBJECT_IDENTITY_CONFLICT", 409);
                if (dto.LaborRate == null || dto.OverheadRate == null) throw Error("RATE_VALUES_REQUIRED", 400);
                row = new() { WgCd = dto.WgCd, Creator = a.Actor, CreateDate = DateTime.Now };
                _db.ProcessCostRates.Add(row);
            }
            await a.RequireRateParent(dto.WgCd, ct);
            if (dto.LaborRate != null) { a.RequireRateWrite("LaborRate"); row.LaborRate = Parse(dto.LaborRate); }
            if (dto.OverheadRate != null) { a.RequireRateWrite("OverheadRate"); row.OverheadRate = Parse(dto.OverheadRate); }
            // Same Owner table fence serializes overlapping inserts and raw PostgreSQL writers.
            if (await _db.ProcessCostRates.AnyAsync(x => !x.IsDeleted && x.Id != row.Id && x.WgCd == row.WgCd
                && (dto.ValidTo == null || x.ValidFrom <= dto.ValidTo) && (x.ValidTo == null || x.ValidTo >= dto.ValidFrom), ct))
                throw Error("RATE_PERIOD_CONFLICT", 409);
            row.ValidFrom = dto.ValidFrom; row.ValidTo = dto.ValidTo;
            await _db.SaveChangesAsync(ct); a.AtCommit(t => a.RequireProcessCostRate(row.Id, t)); return true;
        });
    }
    public Task DeleteOwnerAsync(Guid id, byte[]? version)
    {
        var expected = version?.ToArray();
        return Owner.ExecuteAsync(OperationsOwnerV1.ProcessCostRate, "del", true, async (a, ct) =>
        {
            await a.RequireProcessCostRate(id, ct);
            // Deleting a rate affects both protected monetary values.
            a.RequireRateWrite("LaborRate"); a.RequireRateWrite("OverheadRate");
            var row = await _db.ProcessCostRates.SingleAsync(x => x.Id == id, ct); Version(expected, row.RowVersion);
            row.IsDeleted = true; row.Modifier = a.Actor; row.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(ct); a.AtCommit(t => a.RequireProcessCostRate(id, t, true)); return true;
        });
    }
}
