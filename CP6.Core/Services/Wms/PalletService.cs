using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class PalletService : IPalletService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "PLT";

    public PalletService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<Pallet>> SearchAsync(PalletSearchQuery q)
    {
        var query = _db.Pallets.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.PalletNo)) query = query.Where(x => x.PalletNo.Contains(q.PalletNo));
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (!string.IsNullOrWhiteSpace(q.LotNo)) query = query.Where(x => x.LotNo == q.LotNo);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        return await query.OrderByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<Pallet?> GetAsync(string palletNo)
        => await _db.Pallets.AsNoTracking().FirstOrDefaultAsync(x => x.PalletNo == palletNo && !x.IsDeleted);

    public async Task<string> CreateAsync(PalletDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ProductCd) || string.IsNullOrWhiteSpace(dto.LotNo))
            throw new InvalidOperationException("ProductCd / LotNo 必須（混載禁止原則）");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd) || string.IsNullOrWhiteSpace(dto.LocationCd))
            throw new InvalidOperationException("Warehouse / Location 必須");
        if (dto.CartonQty <= 0) throw new InvalidOperationException("CartonQty > 0");

        var no = await _seq.NextAsync(Prefix);
        _db.Pallets.Add(new Pallet
        {
            PalletNo = no,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName, LotNo = dto.LotNo,
            CartonQty = dto.CartonQty, WeightKg = dto.WeightKg, HeightMm = dto.HeightMm,
            MaxStackLayers = dto.MaxStackLayers,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Status = PalletStatus.Building,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateAsync(string palletNo, PalletDto dto, string? userName)
    {
        var p = await _db.Pallets.FirstOrDefaultAsync(x => x.PalletNo == palletNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (p.Status == PalletStatus.Shipped)
            throw new InvalidOperationException("WM-MSG-043: 出荷済は訂正不可");
        // 業務 PK（製品+ロット）変更は組成中のみ
        if (p.Status != PalletStatus.Building && (p.ProductCd != dto.ProductCd || p.LotNo != dto.LotNo))
            throw new InvalidOperationException("WM-MSG-043: 組成中以外は製品/ロット変更不可");

        p.ProductCd = dto.ProductCd; p.ProductName = dto.ProductName; p.LotNo = dto.LotNo;
        p.CartonQty = dto.CartonQty; p.WeightKg = dto.WeightKg; p.HeightMm = dto.HeightMm;
        p.MaxStackLayers = dto.MaxStackLayers;
        p.WarehouseCd = dto.WarehouseCd; p.LocationCd = dto.LocationCd;
        p.Remarks = dto.Remarks;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CompleteBuildingAsync(string palletNo, string? userName)
    {
        var p = await GetTracked(palletNo);
        if (p.Status != PalletStatus.Building)
            throw new InvalidOperationException("WM-MSG-043: 組成中以外は完成化不可");
        p.Status = PalletStatus.InStock;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MoveToShippingWaitAsync(string palletNo, string toLocationCd, string? userName)
    {
        if (string.IsNullOrWhiteSpace(toLocationCd))
            throw new InvalidOperationException("toLocationCd required");
        var p = await GetTracked(palletNo);
        if (p.Status != PalletStatus.InStock)
            throw new InvalidOperationException("WM-MSG-043: 保管中のみ出荷待機へ移動可");
        p.LocationCd = toLocationCd;
        p.Status = PalletStatus.WaitingShip;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkShippedAsync(string palletNo, string outboundNo, string? userName)
    {
        if (string.IsNullOrWhiteSpace(outboundNo))
            throw new InvalidOperationException("outboundNo required");
        var p = await GetTracked(palletNo);
        if (p.Status != PalletStatus.WaitingShip && p.Status != PalletStatus.InStock)
            throw new InvalidOperationException("WM-MSG-043: 保管中 or 出荷待機のみ出荷化可");
        p.Status = PalletStatus.Shipped;
        p.ShippedOutboundNo = outboundNo;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string palletNo, string? userName)
    {
        var p = await GetTracked(palletNo);
        if (p.Status == PalletStatus.Shipped)
            throw new InvalidOperationException("WM-MSG-043: 出荷済は削除不可");
        p.IsDeleted = true;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<Pallet> GetTracked(string palletNo)
        => await _db.Pallets.FirstOrDefaultAsync(x => x.PalletNo == palletNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
