using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class InkService : IInkService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string LotPrefix = "INK";
    private const string MatchPrefix = "ICM";

    public InkService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<InkLot>> SearchLotsAsync(InkLotSearchQuery q)
    {
        var query = _db.InkLots.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.InkLotNo)) query = query.Where(x => x.InkLotNo.Contains(q.InkLotNo));
        if (!string.IsNullOrWhiteSpace(q.ColorCode)) query = query.Where(x => x.ColorCode == q.ColorCode);
        if (!string.IsNullOrWhiteSpace(q.InkType)) query = query.Where(x => x.InkType == q.InkType);
        if (!string.IsNullOrWhiteSpace(q.OpenStatus)) query = query.Where(x => x.OpenStatus == q.OpenStatus);
        if (q.ExpiringWithin30Days == true)
        {
            var threshold = DateTime.Today.AddDays(30);
            query = query.Where(x => x.ExpiryDate <= threshold);
        }
        return await query.OrderBy(x => x.ExpiryDate).ThenBy(x => x.ColorCode)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<InkLot?> GetLotAsync(string inkLotNo)
        => await _db.InkLots.AsNoTracking().FirstOrDefaultAsync(x => x.InkLotNo == inkLotNo && !x.IsDeleted);

    public async Task<string> CreateLotAsync(InkLotDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ColorCode)) throw new InvalidOperationException("ColorCode required");
        if (dto.Quantity <= 0) throw new InvalidOperationException("Quantity must be > 0");
        if (dto.ExpiryDate == default)
            throw new InvalidOperationException("ExpiryDate required");

        var no = await _seq.NextAsync(LotPrefix);
        _db.InkLots.Add(new InkLot
        {
            InkLotNo = no,
            ColorCode = dto.ColorCode, InkType = dto.InkType ?? "OFFSET",
            OpenStatus = InkOpenStatus.Unopened,
            ExpiryDate = dto.ExpiryDate, Quantity = dto.Quantity, UnitCd = dto.UnitCd ?? "kg",
            ViscosityCp = dto.ViscosityCp, SolidContent = dto.SolidContent,
            LocationCd = dto.LocationCd, SupplierCd = dto.SupplierCd,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task OpenLotAsync(string inkLotNo, DateTime? newExpiryDate, string? userName)
    {
        var lot = await _db.InkLots.FirstOrDefaultAsync(x => x.InkLotNo == inkLotNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (lot.OpenStatus == InkOpenStatus.Opened)
            throw new InvalidOperationException("WM-MSG-043: 既に開封済");
        lot.OpenStatus = InkOpenStatus.Opened;
        if (newExpiryDate.HasValue) lot.ExpiryDate = newExpiryDate.Value;
        lot.Modifier = userName; lot.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<string> MixLotsAsync(MixInkRequest req, string? userName)
    {
        if (string.IsNullOrWhiteSpace(req.ParentLotNoA) || string.IsNullOrWhiteSpace(req.ParentLotNoB))
            throw new InvalidOperationException("両親ロットNO 必須");
        if (req.ParentLotNoA == req.ParentLotNoB)
            throw new InvalidOperationException("同一ロット同士の混合は不可");
        if (req.ParentQtyA <= 0 || req.ParentQtyB <= 0)
            throw new InvalidOperationException("混合量 > 0 必須");

        var lotA = await _db.InkLots.FirstOrDefaultAsync(x => x.InkLotNo == req.ParentLotNoA && !x.IsDeleted)
            ?? throw new InvalidOperationException($"親A [{req.ParentLotNoA}] not found");
        var lotB = await _db.InkLots.FirstOrDefaultAsync(x => x.InkLotNo == req.ParentLotNoB && !x.IsDeleted)
            ?? throw new InvalidOperationException($"親B [{req.ParentLotNoB}] not found");

        if (lotA.Quantity < req.ParentQtyA) throw new InvalidOperationException($"親A 残量不足");
        if (lotB.Quantity < req.ParentQtyB) throw new InvalidOperationException($"親B 残量不足");

        // 親 2 ロットから引く
        lotA.Quantity -= req.ParentQtyA;
        lotA.Modifier = userName; lotA.ModifyDate = DateTime.Now;
        lotB.Quantity -= req.ParentQtyB;
        lotB.Modifier = userName; lotB.ModifyDate = DateTime.Now;

        // 賞味期限は最早を継承
        var minExpiry = lotA.ExpiryDate < lotB.ExpiryDate ? lotA.ExpiryDate : lotB.ExpiryDate;

        var newNo = await _seq.NextAsync(LotPrefix);
        _db.InkLots.Add(new InkLot
        {
            InkLotNo = newNo,
            ColorCode = req.NewColorCode ?? lotA.ColorCode,
            InkType = lotA.InkType,
            OpenStatus = InkOpenStatus.Opened, // 混合品は実質開封品
            ExpiryDate = minExpiry,
            Quantity = req.ParentQtyA + req.ParentQtyB,
            UnitCd = lotA.UnitCd,
            ParentLotNoA = lotA.InkLotNo, ParentLotNoB = lotB.InkLotNo,
            LocationCd = req.NewLocationCd ?? lotA.LocationCd,
            Remarks = req.Remarks ?? $"混合 {lotA.InkLotNo}({req.ParentQtyA}) + {lotB.InkLotNo}({req.ParentQtyB})",
            Creator = userName,
        });
        await _db.SaveChangesAsync();
        return newNo;
    }

    public async Task<List<InkColorMatchHistory>> SearchMatchesAsync(string? customerCd, string? colorCode)
    {
        var query = _db.InkColorMatchHistories.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(customerCd)) query = query.Where(x => x.CustomerCd == customerCd);
        if (!string.IsNullOrWhiteSpace(colorCode)) query = query.Where(x => x.ColorCode == colorCode);
        return await query.OrderByDescending(x => x.MatchedAt).Take(200).ToListAsync();
    }

    public async Task<string> RecordMatchAsync(InkColorMatchDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.CustomerCd) || string.IsNullOrWhiteSpace(dto.ColorCode))
            throw new InvalidOperationException("CustomerCd / ColorCode 必須");
        if (dto.ConsumedQty <= 0) throw new InvalidOperationException("ConsumedQty > 0");

        var no = await _seq.NextAsync(MatchPrefix);
        _db.InkColorMatchHistories.Add(new InkColorMatchHistory
        {
            MatchNo = no,
            CustomerCd = dto.CustomerCd, CustomerName = dto.CustomerName,
            ColorCode = dto.ColorCode, FormulaJson = dto.FormulaJson,
            ConsumedQty = dto.ConsumedQty,
            MatchedAt = DateTime.Now,
            OperatorCd = dto.OperatorCd ?? userName,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }
}
