using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class PalletService : IPalletService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "PLT";

    public PalletService(CP6Context db, IWmsSequenceService seq)
        : this(db, seq, null, null, CancellationToken.None) { }

    public PalletService(CP6Context db, IWmsSequenceService seq, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation)
    {
        _db = db; _seq = seq; _administration = administration;
        _session = session; _requestCancellation = requestCancellation;
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

    public Task<string> CreateAsync(PalletDto dto, string? userName)
        => ExecutePalletCommand("add", userName, async (actor, who, token) =>
    {
        if (string.IsNullOrWhiteSpace(dto.ProductCd) || string.IsNullOrWhiteSpace(dto.LotNo))
            throw new InvalidOperationException("ProductCd / LotNo 必須（混載禁止原則）");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd) || string.IsNullOrWhiteSpace(dto.LocationCd))
            throw new InvalidOperationException("Warehouse / Location 必須");
        if (dto.CartonQty <= 0) throw new InvalidOperationException("CartonQty > 0");
        RequirePalletFields(actor, dto, null);
        await RequirePalletPosition(actor, dto.WarehouseCd, dto.LocationCd, true, token);
        if (actor != null && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("PALLET_AUTHORITY_UNAVAILABLE", 503);

        var no = await _seq.NextAsync(Prefix);
        _db.Pallets.Add(new Pallet
        {
            PalletNo = no,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName, LotNo = dto.LotNo,
            CartonQty = dto.CartonQty, WeightKg = dto.WeightKg, HeightMm = dto.HeightMm,
            MaxStackLayers = dto.MaxStackLayers,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Status = PalletStatus.Building,
            Remarks = dto.Remarks, Creator = who,
        });
        await _db.SaveChangesAsync(token);
        return no;
    });

    public Task UpdateAsync(string palletNo, PalletDto dto, string? userName)
        => ExecutePalletCommand("edit", userName, async (actor, who, token) =>
    {
        var p = await GetTracked(palletNo, token);
        await RequirePalletPosition(actor, p.WarehouseCd, p.LocationCd, false, token);
        if (p.Status == PalletStatus.Shipped)
            throw new InvalidOperationException("WM-MSG-043: 出荷済は訂正不可");
        // 業務 PK（製品+ロット）変更は組成中のみ
        if (p.Status != PalletStatus.Building && (p.ProductCd != dto.ProductCd || p.LotNo != dto.LotNo))
            throw new InvalidOperationException("WM-MSG-043: 組成中以外は製品/ロット変更不可");
        RequirePalletFields(actor, dto, p);
        if (p.WarehouseCd != dto.WarehouseCd || p.LocationCd != dto.LocationCd)
            await RequirePalletPosition(actor, dto.WarehouseCd, dto.LocationCd, true, token);

        p.ProductCd = dto.ProductCd; p.ProductName = dto.ProductName; p.LotNo = dto.LotNo;
        p.CartonQty = dto.CartonQty; p.WeightKg = dto.WeightKg; p.HeightMm = dto.HeightMm;
        p.MaxStackLayers = dto.MaxStackLayers;
        p.WarehouseCd = dto.WarehouseCd; p.LocationCd = dto.LocationCd;
        p.Remarks = dto.Remarks;
        p.Modifier = who; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(token);
        return true;
    });

    public Task CompleteBuildingAsync(string palletNo, string? userName)
        => ExecutePalletCommand("complete", userName, async (actor, who, token) =>
    {
        var p = await GetTracked(palletNo, token);
        await RequirePalletPosition(actor, p.WarehouseCd, p.LocationCd, false, token);
        if (p.Status != PalletStatus.Building)
            throw new InvalidOperationException("WM-MSG-043: 組成中以外は完成化不可");
        p.Status = PalletStatus.InStock;
        p.Modifier = who; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(token);
        return true;
    });

    public Task MoveToShippingWaitAsync(string palletNo, string toLocationCd, string? userName)
        => ExecutePalletCommand("move", userName, async (actor, who, token) =>
    {
        if (string.IsNullOrWhiteSpace(toLocationCd))
            throw new InvalidOperationException("toLocationCd required");
        var p = await GetTracked(palletNo, token);
        await RequirePalletPosition(actor, p.WarehouseCd, p.LocationCd, false, token);
        if (p.Status != PalletStatus.InStock)
            throw new InvalidOperationException("WM-MSG-043: 保管中のみ出荷待機へ移動可");
        RequirePalletField(actor, nameof(Pallet.LocationCd), p.LocationCd != toLocationCd);
        await RequirePalletPosition(actor, p.WarehouseCd, toLocationCd, true, token);
        p.LocationCd = toLocationCd;
        p.Status = PalletStatus.WaitingShip;
        p.Modifier = who; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(token);
        return true;
    });

    public Task MarkShippedAsync(string palletNo, string outboundNo, string? userName)
        => ExecutePalletCommand("ship", userName, async (actor, who, token) =>
    {
        if (string.IsNullOrWhiteSpace(outboundNo))
            throw new InvalidOperationException("outboundNo required");
        var p = await GetTracked(palletNo, token);
        await RequirePalletPosition(actor, p.WarehouseCd, p.LocationCd, false, token);
        if (p.Status != PalletStatus.WaitingShip && p.Status != PalletStatus.InStock)
            throw new InvalidOperationException("WM-MSG-043: 保管中 or 出荷待機のみ出荷化可");
        RequirePalletField(actor, nameof(Pallet.ShippedOutboundNo), p.ShippedOutboundNo != outboundNo);
        p.Status = PalletStatus.Shipped;
        p.ShippedOutboundNo = outboundNo;
        p.Modifier = who; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(token);
        return true;
    });

    public Task DeleteAsync(string palletNo, string? userName)
        => ExecutePalletCommand("del", userName, async (actor, who, token) =>
    {
        var p = await GetTracked(palletNo, token);
        await RequirePalletPosition(actor, p.WarehouseCd, p.LocationCd, false, token);
        if (p.Status == PalletStatus.Shipped)
            throw new InvalidOperationException("WM-MSG-043: 出荷済は削除不可");
        p.IsDeleted = true;
        p.Modifier = who; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(token);
        return true;
    });

    private async Task<Pallet> GetTracked(string palletNo, CancellationToken ct)
        => await _db.Pallets.FirstOrDefaultAsync(x => x.PalletNo == palletNo && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
