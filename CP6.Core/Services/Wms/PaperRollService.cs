using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class PaperRollService : IPaperRollService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "ROLL";

    public PaperRollService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<PaperRoll>> SearchAsync(PaperRollSearchQuery q)
    {
        var query = _db.PaperRolls.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.RollNo)) query = query.Where(x => x.RollNo.Contains(q.RollNo));
        if (!string.IsNullOrWhiteSpace(q.PaperGrade)) query = query.Where(x => x.PaperGrade == q.PaperGrade);
        if (q.WidthMm.HasValue) query = query.Where(x => x.WidthMm == q.WidthMm.Value);
        if (!string.IsNullOrWhiteSpace(q.GrainDirection)) query = query.Where(x => x.GrainDirection == q.GrainDirection);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        return await query.OrderBy(x => x.PaperGrade).ThenBy(x => x.WidthMm).ThenBy(x => x.RollNo)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<PaperRoll?> GetAsync(string rollNo)
        => await _db.PaperRolls.AsNoTracking().FirstOrDefaultAsync(x => x.RollNo == rollNo && !x.IsDeleted);

    public async Task<string> CreateAsync(PaperRollDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.PaperGrade)) throw new InvalidOperationException("PaperGrade required");
        if (dto.WidthMm <= 0) throw new InvalidOperationException("WidthMm must be > 0");
        if (dto.OriginalLengthM <= 0) throw new InvalidOperationException("OriginalLengthM must be > 0");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd) || string.IsNullOrWhiteSpace(dto.LocationCd))
            throw new InvalidOperationException("Warehouse / Location required");

        var no = await _seq.NextAsync(Prefix);
        _db.PaperRolls.Add(new PaperRoll
        {
            RollNo = no,
            PaperGrade = dto.PaperGrade, WidthMm = dto.WidthMm,
            BasisWeight = dto.BasisWeight, GrainDirection = dto.GrainDirection ?? "T",
            OriginalLengthM = dto.OriginalLengthM, RemainingLengthM = dto.OriginalLengthM,
            CoreDiameterInch = dto.CoreDiameterInch == 0 ? 3m : dto.CoreDiameterInch,
            MfgDate = dto.MfgDate, MfgLotNo = dto.MfgLotNo, SupplierRollNo = dto.SupplierRollNo,
            LocationCd = dto.LocationCd, WarehouseCd = dto.WarehouseCd,
            Status = PaperRollStatus.InStock,
            DisposeThresholdM = dto.DisposeThresholdM,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task ConsumeAsync(string rollNo, decimal lengthM, string? userName)
    {
        if (lengthM <= 0) throw new InvalidOperationException("WM-MSG-202: 消費長は0より大きい値を指定");
        var roll = await _db.PaperRolls.FirstOrDefaultAsync(x => x.RollNo == rollNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (roll.Status == PaperRollStatus.Disposed)
            throw new InvalidOperationException("WM-MSG-043: 廃棄済ロールは消費不可");
        if (roll.RemainingLengthM < lengthM)
            throw new InvalidOperationException($"WM-MSG-202: 消費長({lengthM}) > 残米({roll.RemainingLengthM})");

        roll.RemainingLengthM -= lengthM;
        // 自動状态遷移
        if (roll.RemainingLengthM == 0)
            roll.Status = PaperRollStatus.Disposed; // 完全消費
        else if (roll.DisposeThresholdM.HasValue && roll.RemainingLengthM <= roll.DisposeThresholdM.Value)
            roll.Status = PaperRollStatus.Remnant; // 残米状态
        else if (roll.Status == PaperRollStatus.InStock)
            roll.Status = PaperRollStatus.InUse; // 在庫 → 使用中

        roll.Modifier = userName; roll.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<PaperRoll>> MatchAsync(string paperGrade, int widthMm, string grainDirection, decimal requiredLengthM)
    {
        // 巾≥必要巾、流れ一致、残米≥必要長
        return await _db.PaperRolls.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.PaperGrade == paperGrade
                        && x.GrainDirection == grainDirection
                        && x.WidthMm >= widthMm
                        && x.RemainingLengthM >= requiredLengthM
                        && (x.Status == PaperRollStatus.InStock || x.Status == PaperRollStatus.InUse))
            .OrderBy(x => x.RemainingLengthM) // 残米最小優先（端材作らない）
            .ThenBy(x => x.WidthMm)            // 巾近い順
            .Take(20)
            .ToListAsync();
    }

    public async Task<List<string>> SlitAsync(SlitRequest req, string? userName)
    {
        if (string.IsNullOrWhiteSpace(req.ParentRollNo)) throw new InvalidOperationException("ParentRollNo required");
        if (req.ChildWidths == null || req.ChildWidths.Count == 0)
            throw new InvalidOperationException("ChildWidths required");

        var parent = await _db.PaperRolls.FirstOrDefaultAsync(x => x.RollNo == req.ParentRollNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (parent.Status == PaperRollStatus.Disposed)
            throw new InvalidOperationException("WM-MSG-043: 廃棄済ロールは巾割り不可");

        int totalChildWidth = req.ChildWidths.Sum();
        if (totalChildWidth > parent.WidthMm)
            throw new InvalidOperationException($"WM-MSG-203: 子ロール巾合計({totalChildWidth}) > 親巾({parent.WidthMm})");

        var created = new List<string>();
        foreach (var w in req.ChildWidths)
        {
            if (w <= 0) throw new InvalidOperationException($"子巾 {w} は無効");
            var no = await _seq.NextAsync(Prefix);
            _db.PaperRolls.Add(new PaperRoll
            {
                RollNo = no,
                PaperGrade = parent.PaperGrade, WidthMm = w,
                BasisWeight = parent.BasisWeight, GrainDirection = parent.GrainDirection,
                OriginalLengthM = parent.RemainingLengthM, RemainingLengthM = parent.RemainingLengthM,
                CoreDiameterInch = parent.CoreDiameterInch,
                MfgDate = parent.MfgDate, MfgLotNo = parent.MfgLotNo,
                LocationCd = parent.LocationCd, WarehouseCd = parent.WarehouseCd,
                Status = PaperRollStatus.InStock,
                ParentRollNo = parent.RollNo,
                Remarks = $"スリッター親 {parent.RollNo} 子巾 {w}mm",
                Creator = userName,
            });
            created.Add(no);
        }

        // 残端も保存する場合
        int remnantWidth = parent.WidthMm - totalChildWidth;
        if (req.KeepRemnant && remnantWidth > 0)
        {
            var no = await _seq.NextAsync(Prefix);
            _db.PaperRolls.Add(new PaperRoll
            {
                RollNo = no,
                PaperGrade = parent.PaperGrade, WidthMm = remnantWidth,
                BasisWeight = parent.BasisWeight, GrainDirection = parent.GrainDirection,
                OriginalLengthM = parent.RemainingLengthM, RemainingLengthM = parent.RemainingLengthM,
                CoreDiameterInch = parent.CoreDiameterInch,
                MfgDate = parent.MfgDate, MfgLotNo = parent.MfgLotNo,
                LocationCd = parent.LocationCd, WarehouseCd = parent.WarehouseCd,
                Status = PaperRollStatus.Remnant,
                ParentRollNo = parent.RollNo,
                Remarks = $"スリッター親 {parent.RollNo} 残端 {remnantWidth}mm",
                Creator = userName,
            });
            created.Add(no);
        }

        // 親ロールを廃棄状态に
        parent.Status = PaperRollStatus.Disposed;
        parent.RemainingLengthM = 0;
        parent.Modifier = userName; parent.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
        return created;
    }

    public async Task DisposeAsync(string rollNo, string? userName)
    {
        var roll = await _db.PaperRolls.FirstOrDefaultAsync(x => x.RollNo == rollNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        roll.Status = PaperRollStatus.Disposed;
        roll.Modifier = userName; roll.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
