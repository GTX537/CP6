using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class CrossDockService : ICrossDockService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private const string Prefix = "XD";

    public CrossDockService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db; _seq = seq; _stock = stock;
    }

    public async Task<List<CrossDockOrder>> SearchAsync(CrossDockSearchQuery q)
    {
        var query = _db.CrossDockOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.XDockNo)) query = query.Where(x => x.XDockNo.Contains(q.XDockNo));
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (!string.IsNullOrWhiteSpace(q.InboundNo)) query = query.Where(x => x.InboundNo == q.InboundNo);
        if (!string.IsNullOrWhiteSpace(q.OutboundNo)) query = query.Where(x => x.OutboundNo == q.OutboundNo);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.DateFrom.HasValue) query = query.Where(x => x.CreateDate >= q.DateFrom.Value);
        if (q.DateTo.HasValue) query = query.Where(x => x.CreateDate <= q.DateTo.Value);
        return await query.OrderByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<CrossDockOrder?> GetAsync(string xdockNo)
        => await _db.CrossDockOrders.AsNoTracking().FirstOrDefaultAsync(x => x.XDockNo == xdockNo && !x.IsDeleted);

    public async Task<string> CreateAsync(CrossDockOrderDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ProductCd)) throw new InvalidOperationException("ProductCd required");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd)) throw new InvalidOperationException("WarehouseCd required");
        if (string.IsNullOrWhiteSpace(dto.TempLocationCd)) throw new InvalidOperationException("TempLocationCd required");
        if (dto.Qty <= 0) throw new InvalidOperationException("WM-MSG-021: Qty must be > 0");

        var no = await _seq.NextAsync(Prefix);
        _db.CrossDockOrders.Add(new CrossDockOrder
        {
            XDockNo = no, InboundNo = dto.InboundNo, OutboundNo = dto.OutboundNo,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName,
            Qty = dto.Qty, SupplierCd = dto.SupplierCd, CustomerCd = dto.CustomerCd,
            FromDock = dto.FromDock, ToDock = dto.ToDock,
            WarehouseCd = dto.WarehouseCd, TempLocationCd = dto.TempLocationCd,
            LotNo = dto.LotNo ?? "",
            Status = CrossDockStatus.Planned,
            OperatorCd = dto.OperatorCd ?? userName,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task ExecuteAsync(string xdockNo, string? userName)
    {
        var order = await _db.CrossDockOrders.FirstOrDefaultAsync(x => x.XDockNo == xdockNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status != CrossDockStatus.Planned)
            throw new InvalidOperationException("WM-MSG-043: 計画状态でないため実行不可");

        var lot = string.IsNullOrWhiteSpace(order.LotNo)
            ? $"XD{DateTime.Today:yyyyMMdd}-{xdockNo[^4..]}"
            : order.LotNo;

        // 1. IN（仮置きロケへ着荷）
        var inTxn = await _stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.IN,
            WarehouseCd = order.WarehouseCd,
            LocationCd = order.TempLocationCd,
            ProductCd = order.ProductCd, LotNo = lot, Qty = order.Qty,
            RelatedNo = xdockNo, RelatedType = "XDOCK_IN",
            OperatorCd = userName,
            Remark = $"クロスドック IN {xdockNo}",
        });

        // 2. OUT（即出荷、同ロケから）
        var outTxn = await _stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.OUT,
            WarehouseCd = order.WarehouseCd,
            LocationCd = order.TempLocationCd,
            ProductCd = order.ProductCd, LotNo = lot, Qty = order.Qty,
            RelatedNo = xdockNo, RelatedType = "XDOCK_OUT",
            OperatorCd = userName,
            Remark = $"クロスドック OUT {xdockNo}",
        });

        order.InTxnNo = inTxn;
        order.OutTxnNo = outTxn;
        order.LotNo = lot;
        order.Status = CrossDockStatus.Executed;
        order.ExecutedAt = DateTime.Now;
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string xdockNo, string? userName)
    {
        var order = await _db.CrossDockOrders.FirstOrDefaultAsync(x => x.XDockNo == xdockNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status == CrossDockStatus.Executed)
            throw new InvalidOperationException("WM-MSG-043: 実行済は取消不可");
        order.Status = CrossDockStatus.Cancelled;
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
