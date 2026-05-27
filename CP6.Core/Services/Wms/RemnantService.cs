using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class RemnantService : IRemnantService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "REM";

    public RemnantService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<RemnantMaterial>> SearchAsync(RemnantSearchQuery q)
    {
        var query = _db.RemnantMaterials.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.RemnantNo)) query = query.Where(x => x.RemnantNo.Contains(q.RemnantNo));
        if (!string.IsNullOrWhiteSpace(q.MaterialType)) query = query.Where(x => x.MaterialType == q.MaterialType);
        if (!string.IsNullOrWhiteSpace(q.MaterialGrade)) query = query.Where(x => x.MaterialGrade == q.MaterialGrade);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (!string.IsNullOrWhiteSpace(q.SourceWorkOrderNo)) query = query.Where(x => x.SourceWorkOrderNo == q.SourceWorkOrderNo);
        if (!string.IsNullOrWhiteSpace(q.SourceRollNo)) query = query.Where(x => x.SourceRollNo == q.SourceRollNo);
        return await query.OrderByDescending(x => x.RegisteredAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<RemnantMaterial?> GetAsync(string remnantNo)
        => await _db.RemnantMaterials.AsNoTracking().FirstOrDefaultAsync(x => x.RemnantNo == remnantNo && !x.IsDeleted);

    public async Task<string> CreateAsync(RemnantDto dto, string? userName)
    {
        if (dto.WidthMm <= 0 || dto.LengthMm <= 0) throw new InvalidOperationException("Width / Length > 0");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd) || string.IsNullOrWhiteSpace(dto.LocationCd))
            throw new InvalidOperationException("Warehouse / Location 必須");
        if (dto.Quantity <= 0) throw new InvalidOperationException("Quantity > 0");

        var no = await _seq.NextAsync(Prefix);
        _db.RemnantMaterials.Add(new RemnantMaterial
        {
            RemnantNo = no,
            MaterialType = dto.MaterialType, MaterialGrade = dto.MaterialGrade,
            WidthMm = dto.WidthMm, LengthMm = dto.LengthMm, ThicknessUm = dto.ThicknessUm,
            Quantity = dto.Quantity, UnitCd = dto.UnitCd,
            SourceWorkOrderNo = dto.SourceWorkOrderNo, SourceRollNo = dto.SourceRollNo,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Status = RemnantStatus.Available,
            RegisteredAt = DateTime.Now,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateAsync(string remnantNo, RemnantDto dto, string? userName)
    {
        var r = await GetTracked(remnantNo);
        if (r.Status == RemnantStatus.Used || r.Status == RemnantStatus.Disposed)
            throw new InvalidOperationException("WM-MSG-043: 使用済/廃棄は訂正不可");

        r.MaterialType = dto.MaterialType; r.MaterialGrade = dto.MaterialGrade;
        r.WidthMm = dto.WidthMm; r.LengthMm = dto.LengthMm; r.ThicknessUm = dto.ThicknessUm;
        r.Quantity = dto.Quantity; r.UnitCd = dto.UnitCd;
        r.SourceWorkOrderNo = dto.SourceWorkOrderNo; r.SourceRollNo = dto.SourceRollNo;
        r.WarehouseCd = dto.WarehouseCd; r.LocationCd = dto.LocationCd;
        r.Remarks = dto.Remarks;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<RemnantMaterial>> MatchAsync(string materialType, int minWidthMm, int minLengthMm)
        => await _db.RemnantMaterials.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == RemnantStatus.Available
                && x.MaterialType == materialType
                && x.WidthMm >= minWidthMm && x.LengthMm >= minLengthMm)
            .OrderBy(x => x.WidthMm).ThenBy(x => x.LengthMm)
            .Take(100).ToListAsync();

    public async Task ReserveAsync(string remnantNo, string reservedFor, string? userName)
    {
        if (string.IsNullOrWhiteSpace(reservedFor))
            throw new InvalidOperationException("reservedFor required");
        var r = await GetTracked(remnantNo);
        if (r.Status != RemnantStatus.Available)
            throw new InvalidOperationException("WM-MSG-043: 利用可のみ予約可");
        r.Status = RemnantStatus.Reserved;
        r.ReservedFor = reservedFor;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task UnreserveAsync(string remnantNo, string? userName)
    {
        var r = await GetTracked(remnantNo);
        if (r.Status != RemnantStatus.Reserved)
            throw new InvalidOperationException("WM-MSG-043: 予約済のみ解除可");
        r.Status = RemnantStatus.Available;
        r.ReservedFor = null;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkUsedAsync(string remnantNo, string? userName)
    {
        var r = await GetTracked(remnantNo);
        if (r.Status == RemnantStatus.Used || r.Status == RemnantStatus.Disposed)
            throw new InvalidOperationException("WM-MSG-043: 既に終了状态");
        r.Status = RemnantStatus.Used;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync(string remnantNo, string? userName)
    {
        var r = await GetTracked(remnantNo);
        if (r.Status == RemnantStatus.Disposed)
            throw new InvalidOperationException("WM-MSG-043: 既に廃棄");
        r.Status = RemnantStatus.Disposed;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string remnantNo, string? userName)
    {
        var r = await GetTracked(remnantNo);
        r.IsDeleted = true;
        r.Modifier = userName; r.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<RemnantMaterial> GetTracked(string remnantNo)
        => await _db.RemnantMaterials.FirstOrDefaultAsync(x => x.RemnantNo == remnantNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
