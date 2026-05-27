using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class PlateMoldService : IPlateMoldService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "PLT2"; // PLT は Pallet で利用済の為 PLT2

    public PlateMoldService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<PlateMoldStock>> SearchAsync(PlateMoldSearchQuery q)
    {
        var query = _db.PlateMoldStocks.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.PlateNo)) query = query.Where(x => x.PlateNo.Contains(q.PlateNo));
        if (!string.IsNullOrWhiteSpace(q.PlateType)) query = query.Where(x => x.PlateType == q.PlateType);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        return await query.OrderByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<PlateMoldStock?> GetAsync(string plateNo)
        => await _db.PlateMoldStocks.AsNoTracking().FirstOrDefaultAsync(x => x.PlateNo == plateNo && !x.IsDeleted);

    public async Task<string> CreateAsync(PlateMoldDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.PlateType))
            throw new InvalidOperationException("PlateType required");
        if (dto.MaxShots.HasValue && dto.MaxShots.Value <= 0)
            throw new InvalidOperationException("MaxShots > 0");

        var no = await _seq.NextAsync(Prefix);
        _db.PlateMoldStocks.Add(new PlateMoldStock
        {
            PlateNo = no,
            PlateType = dto.PlateType,
            CustomerCd = dto.CustomerCd, CustomerName = dto.CustomerName,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName,
            ColorCount = dto.ColorCount, SizeNote = dto.SizeNote,
            MadeDate = dto.MadeDate, MadeCost = dto.MadeCost,
            MaxShots = dto.MaxShots, UsedShots = 0,
            NextMaintenanceDate = dto.NextMaintenanceDate,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Status = PlateMoldStatus.Usable,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateAsync(string plateNo, PlateMoldDto dto, string? userName)
    {
        var p = await GetTracked(plateNo);
        if (p.Status == PlateMoldStatus.Discarded)
            throw new InvalidOperationException("WM-MSG-043: 廃版は訂正不可");

        p.PlateType = dto.PlateType;
        p.CustomerCd = dto.CustomerCd; p.CustomerName = dto.CustomerName;
        p.ProductCd = dto.ProductCd; p.ProductName = dto.ProductName;
        p.ColorCount = dto.ColorCount; p.SizeNote = dto.SizeNote;
        p.MadeDate = dto.MadeDate; p.MadeCost = dto.MadeCost;
        p.MaxShots = dto.MaxShots;
        p.NextMaintenanceDate = dto.NextMaintenanceDate;
        p.WarehouseCd = dto.WarehouseCd; p.LocationCd = dto.LocationCd;
        p.Remarks = dto.Remarks;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task RecordUsageAsync(string plateNo, int shots, string? userName)
    {
        if (shots <= 0) throw new InvalidOperationException("shots > 0");
        var p = await GetTracked(plateNo);
        if (p.Status != PlateMoldStatus.Usable)
            throw new InvalidOperationException("WM-MSG-043: 使用可状态のみ記録可");
        p.UsedShots += shots;
        p.LastUsedAt = DateTime.Now;
        // 寿命チェック：MaxShots 設定済 かつ 到達 → 自動 2
        if (p.MaxShots.HasValue && p.UsedShots >= p.MaxShots.Value)
            p.Status = PlateMoldStatus.LifeReached;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task StartMaintenanceAsync(string plateNo, DateTime? nextMaintDate, string? userName)
    {
        var p = await GetTracked(plateNo);
        if (p.Status != PlateMoldStatus.Usable && p.Status != PlateMoldStatus.LifeReached)
            throw new InvalidOperationException("WM-MSG-043: 使用可/寿命到達のみメンテ可");
        p.Status = PlateMoldStatus.Maintenance;
        if (nextMaintDate.HasValue) p.NextMaintenanceDate = nextMaintDate;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CompleteMaintenanceAsync(string plateNo, string? userName)
    {
        var p = await GetTracked(plateNo);
        if (p.Status != PlateMoldStatus.Maintenance)
            throw new InvalidOperationException("WM-MSG-043: メンテ中のみ完了可");
        p.Status = PlateMoldStatus.Usable;
        // メンテ完了 → 寿命カウンタ リセット（ポリシ：印版研磨で再使用可）
        p.UsedShots = 0;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DiscardAsync(string plateNo, string? userName)
    {
        var p = await GetTracked(plateNo);
        if (p.Status == PlateMoldStatus.Discarded)
            throw new InvalidOperationException("WM-MSG-043: 既に廃版");
        p.Status = PlateMoldStatus.Discarded;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<PlateMoldStock>> WarningListAsync(decimal threshold = 0.9m)
    {
        var list = await _db.PlateMoldStocks.AsNoTracking()
            .Where(x => !x.IsDeleted
                && (x.Status == PlateMoldStatus.LifeReached
                    || (x.MaxShots.HasValue && x.MaxShots.Value > 0)))
            .ToListAsync();
        return list.Where(x => x.Status == PlateMoldStatus.LifeReached
                || (x.MaxShots.HasValue && (decimal)x.UsedShots / x.MaxShots.Value >= threshold))
            .OrderByDescending(x => x.MaxShots.HasValue ? ((decimal)x.UsedShots / x.MaxShots.Value) : 0m)
            .ToList();
    }

    public async Task DeleteAsync(string plateNo, string? userName)
    {
        var p = await GetTracked(plateNo);
        p.IsDeleted = true;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<PlateMoldStock> GetTracked(string plateNo)
        => await _db.PlateMoldStocks.FirstOrDefaultAsync(x => x.PlateNo == plateNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
