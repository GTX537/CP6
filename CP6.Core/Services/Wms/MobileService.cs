using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class MobileService : IMobileService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private const string Prefix = "MTK";

    public MobileService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db; _seq = seq; _stock = stock;
    }

    public async Task<List<MobileTask>> GetTasksAsync(MobileTaskQuery q)
    {
        var query = _db.MobileTasks.AsNoTracking().Where(x => !x.IsDeleted);
        // 作業者指定時は「本人割当」＋「未割当プール（誰でも取れる）」を両方表示
        if (!string.IsNullOrWhiteSpace(q.AssignedTo)) query = query.Where(x => x.AssignedTo == q.AssignedTo || x.AssignedTo == null);
        if (!string.IsNullOrWhiteSpace(q.TaskType)) query = query.Where(x => x.TaskType == q.TaskType);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.OpenOnly) query = query.Where(x => x.Status == MobileTaskStatus.Pending || x.Status == MobileTaskStatus.InProgress);

        return await query
            .OrderBy(x => x.Status)            // 未着手 → 進行中 → 完了
            .ThenBy(x => x.Priority)           // 至急優先
            .ThenByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<MobileTask?> GetAsync(string mobileTaskNo)
        => await _db.MobileTasks.AsNoTracking().FirstOrDefaultAsync(x => x.MobileTaskNo == mobileTaskNo && !x.IsDeleted);

    public async Task<string> CreateAsync(MobileTaskDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.TaskType))
            throw new InvalidOperationException("taskType required");

        var no = await _seq.NextAsync(Prefix);
        _db.MobileTasks.Add(new MobileTask
        {
            MobileTaskNo = no,
            TaskType = dto.TaskType,
            AssignedTo = dto.AssignedTo,
            Priority = dto.Priority == 0 ? 2 : dto.Priority,
            Status = MobileTaskStatus.Pending,
            RelatedNo = dto.RelatedNo, RelatedType = dto.RelatedType,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName, LotNo = dto.LotNo,
            WarehouseCd = dto.WarehouseCd,
            FromLocationCd = dto.FromLocationCd, ToLocationCd = dto.ToLocationCd,
            Qty = dto.Qty, UnitCd = dto.UnitCd,
            Instruction = dto.Instruction, Remarks = dto.Remarks,
            Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task StartAsync(string mobileTaskNo, string? userName)
    {
        var task = await Load(mobileTaskNo);
        if (task.Status == MobileTaskStatus.Completed || task.Status == MobileTaskStatus.Cancelled)
            throw new InvalidOperationException("WM-MSG-043");
        if (task.Status == MobileTaskStatus.Pending)
        {
            task.Status = MobileTaskStatus.InProgress;
            task.StartedAt = DateTime.Now;
            task.Modifier = userName; task.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<MobileScanResult> ScanAsync(MobileScanRequest req)
    {
        var code = (req.Barcode ?? string.Empty).Trim();
        var result = new MobileScanResult { Barcode = code };
        if (string.IsNullOrEmpty(code))
        {
            result.Kind = MobileScanKind.Unknown;
            result.Message = "WM-MSG-300"; // 空スキャン
            return result;
        }

        // 1) ロケーション解決（LocationCd 完全一致 or バーコード一致）
        var loc = await _db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && (x.LocationCd == code || x.Barcode == code));
        if (loc != null)
        {
            result.Kind = MobileScanKind.Location;
            result.LocationCd = loc.LocationCd;
            result.LocationName = loc.LocationName;
            result.IsBlocked = loc.IsBlocked;
            result.Stocks = await StockLinesAt(loc.WarehouseCd, loc.LocationCd, null);
        }
        else
        {
            // 2) 製品解決（ProductCd で在庫検索）
            var stocks = await StockLinesAt(req.WarehouseCd, null, code);
            if (stocks.Count > 0)
            {
                result.Kind = MobileScanKind.Product;
                result.ProductCd = code;
                result.Stocks = stocks;
            }
            else
            {
                result.Kind = MobileScanKind.Unknown;
                result.Message = "WM-MSG-301"; // 解決不能
            }
        }

        // 3) 作業指示との照合（任意）
        if (!string.IsNullOrWhiteSpace(req.TaskNo))
        {
            var task = await _db.MobileTasks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MobileTaskNo == req.TaskNo && !x.IsDeleted);
            if (task != null)
            {
                result.Matched = result.Kind switch
                {
                    MobileScanKind.Location => code == task.FromLocationCd || code == task.ToLocationCd,
                    MobileScanKind.Product => code == task.ProductCd,
                    _ => false,
                };
                if (result.Matched == false) result.Message = "WM-MSG-302"; // 指示と不一致
            }
        }

        return result;
    }

    public async Task CompleteAsync(string mobileTaskNo, MobileCompleteRequest req, string? userName)
    {
        var task = await Load(mobileTaskNo);
        if (task.Status == MobileTaskStatus.Completed || task.Status == MobileTaskStatus.Cancelled)
            throw new InvalidOperationException("WM-MSG-043");

        var doneQty = req.ScannedQty ?? task.Qty;
        if (!string.IsNullOrWhiteSpace(req.ToLocationCd)) task.ToLocationCd = req.ToLocationCd;

        // MOVE 種別のみ実在庫を動かす（他は伝票側の確定フローが在庫を動かす）
        if (task.TaskType == MobileTaskType.Move)
        {
            if (string.IsNullOrWhiteSpace(task.WarehouseCd) || string.IsNullOrWhiteSpace(task.FromLocationCd)
                || string.IsNullOrWhiteSpace(task.ToLocationCd) || string.IsNullOrWhiteSpace(task.ProductCd))
                throw new InvalidOperationException("WM-MSG-303"); // MOVE に必要な項目不足
            if (doneQty <= 0) throw new InvalidOperationException("WM-MSG-031");

            var (outTxn, inTxn) = await _stock.MoveAsync(new StockMoveRequest
            {
                WarehouseCd = task.WarehouseCd!,
                FromLocationCd = task.FromLocationCd!,
                ToLocationCd = task.ToLocationCd!,
                ProductCd = task.ProductCd!,
                LotNo = task.LotNo ?? "",
                Qty = doneQty,
                OperatorCd = userName,
                Remark = $"モバイル {mobileTaskNo} MOVE",
            });
            task.OutTxnNo = outTxn;
            task.InTxnNo = inTxn;
        }

        task.ScannedQty = doneQty;
        task.Status = MobileTaskStatus.Completed;
        task.DoneAt = DateTime.Now;
        if (task.StartedAt == null) task.StartedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(req.Remarks)) task.Remarks = req.Remarks;
        task.Modifier = userName; task.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string mobileTaskNo, string? userName)
    {
        var task = await Load(mobileTaskNo);
        if (task.Status == MobileTaskStatus.Completed)
            throw new InvalidOperationException("WM-MSG-043");
        task.Status = MobileTaskStatus.Cancelled;
        task.Modifier = userName; task.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ───────── helpers ─────────

    private async Task<MobileTask> Load(string no)
        => await _db.MobileTasks.FirstOrDefaultAsync(x => x.MobileTaskNo == no && !x.IsDeleted)
           ?? throw new InvalidOperationException("WM-MSG-070");

    /// <summary>指定ロケ or 製品の現在在庫（PhysicalQty &gt; 0、上位 20 件）</summary>
    private async Task<List<MobileStockLine>> StockLinesAt(string? warehouseCd, string? locationCd, string? productCd)
    {
        var q = _db.Stocks.AsNoTracking().Where(s => !s.IsDeleted && s.PhysicalQty > 0);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(s => s.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(locationCd)) q = q.Where(s => s.LocationCd == locationCd);
        if (!string.IsNullOrWhiteSpace(productCd)) q = q.Where(s => s.ProductCd == productCd);

        return await q
            .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)
            .Take(20)
            .Select(s => new MobileStockLine
            {
                WarehouseCd = s.WarehouseCd,
                LocationCd = s.LocationCd,
                ProductCd = s.ProductCd,
                LotNo = s.LotNo,
                PhysicalQty = s.PhysicalQty,
                AvailableQty = s.AvailableQty,
                UnitCd = s.UnitCd,
                ExpiryDate = s.ExpiryDate,
            })
            .ToListAsync();
    }
}
